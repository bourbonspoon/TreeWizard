using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TreeWizard.Editor
{
    /// <summary>How an impostor is captured, and when it takes over from the full tree.</summary>
    [Serializable]
    public class ImpostorSettings
    {
        [Tooltip("Views along each side of the atlas, spread over the upper half of a sphere. More turn more smoothly, each at a lower resolution.")]
        [Range(4, 32)] public int frames = 16;

        [Tooltip("Width and height of the impostor textures in pixels.")]
        [Range(256, 8192)] public int atlasSize = 2048;

        [Tooltip("Share of the screen's height the tree fills when the impostor takes over from the full tree.")]
        [Range(0.01f, 1f)] public float transition = 0.15f;

        [Tooltip("Share of the screen's height below which the tree isn't drawn at all. 0 always draws it.")]
        [Range(0f, 0.5f)] public float cull;

        [Tooltip("Share of each level's range spent dithering into the next.")]
        [Range(0f, 1f)] public float fadeWidth = 0.25f;

        public ImpostorSettings Clone() => (ImpostorSettings)MemberwiseClone();
    }

    /// <summary>
    /// Bakes octahedral impostors: flat stand-ins for distant trees, lit at runtime from albedo and normal
    /// atlases, that cast shadows and turn to face the camera. The prefab root gets a LODGroup whose first
    /// level is the full tree and whose last is the impostor, cross-fading with URP's dither.
    /// </summary>
    public static class ImpostorBaker
    {
        public const string PartName = "Impostor";
        public const string ShaderName = "TreeWizard/Impostor";
        const string CaptureShaderName = "Hidden/TreeWizard/ImpostorCapture";
        const int Supersample = 2;

        static readonly int AlbedoId = Shader.PropertyToID("_ImpostorAlbedo");
        static readonly int NormalsId = Shader.PropertyToID("_ImpostorNormals");
        static readonly int FramesId = Shader.PropertyToID("_ImpostorFrames");
        static readonly int BoundsId = Shader.PropertyToID("_ImpostorBounds");
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        /// <summary>Whether the prefab has an impostor from an earlier bake.</summary>
        public static bool HasImpostor(GameObject prefab) =>
            prefab != null && prefab.transform.Find(PartName) != null && prefab.TryGetComponent<LODGroup>(out _);

        /// <summary>The settings the prefab's impostor was baked with, as later edited on its LODGroup. Null without one.</summary>
        public static ImpostorSettings ReadSettings(GameObject prefab)
        {
            if (!HasImpostor(prefab))
                return null;
            var settings = new ImpostorSettings();
            var material = prefab.transform.Find(PartName).TryGetComponent<MeshRenderer>(out var renderer) ? renderer.sharedMaterial : null;
            if (material != null && material.HasFloat(FramesId))
            {
                settings.frames = Mathf.RoundToInt(material.GetFloat(FramesId));
                if (material.GetTexture(AlbedoId) is Texture albedo)
                    settings.atlasSize = albedo.width;
            }
            var lods = prefab.GetComponent<LODGroup>().GetLODs();
            if (lods.Length >= 2)
            {
                settings.transition = lods[0].screenRelativeTransitionHeight;
                settings.fadeWidth = lods[0].fadeTransitionWidth;
                settings.cull = lods[^1].screenRelativeTransitionHeight;
            }
            return settings;
        }

        /// <summary>
        /// Captures the prefab and adds or updates its impostor. Everything but an existing impostor is captured.
        /// Writes Name_Impostor_Albedo.png, Name_Impostor_Normal.png, Name_Impostor.mat and Name_Impostor.mesh
        /// beside the prefab, adds an Impostor child and sets up a LODGroup on the root. Re-baking updates all
        /// of them in place, keeping their IDs. <paramref name="settings"/> null keeps the prefab's current
        /// settings, or uses the defaults for a first bake.
        /// </summary>
        public static GameObject Bake(string prefabPath, ImpostorSettings settings = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null)
                throw new ArgumentException($"No prefab at {prefabPath}.", nameof(prefabPath));
            settings = Sanitize((settings ?? ReadSettings(asset) ?? new ImpostorSettings()).Clone());

            var name = Path.GetFileNameWithoutExtension(prefabPath);
            var folder = Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var existing = root.transform.Find(PartName);
                var renderers = root.GetComponentsInChildren<MeshRenderer>()
                    .Where(r => existing == null || !r.transform.IsChildOf(existing))
                    .Where(r => r.TryGetComponent<MeshFilter>(out var f) && f.sharedMesh != null)
                    .ToArray();
                if (renderers.Length == 0)
                    throw new InvalidOperationException($"{prefabPath} has no meshes to capture.");

                var parts = renderers.Select(r => new Part
                {
                    mesh = r.GetComponent<MeshFilter>().sharedMesh,
                    toRoot = root.transform.worldToLocalMatrix * r.transform.localToWorldMatrix,
                    materials = r.sharedMaterials,
                }).ToList();
                var (center, radius) = BoundingSphere(parts);

                var cell = Mathf.Max(4, Mathf.RoundToInt(settings.atlasSize / (float)settings.frames / 4f) * 4);
                var size = cell * settings.frames;
                Capture(parts, center, radius, settings.frames, cell, out var albedoPixels, out var normalPixels);
                var albedo = SaveTexture(albedoPixels, size, $"{folder}/{name}_{PartName}_Albedo.png", true);
                var normals = SaveTexture(normalPixels, size, $"{folder}/{name}_{PartName}_Normal.png", false);

                var material = SaveMaterial($"{folder}/{name}_{PartName}.mat", renderers);
                material.SetTexture(AlbedoId, albedo);
                material.SetTexture(NormalsId, normals);
                material.SetFloat(FramesId, settings.frames);
                material.SetVector(BoundsId, new Vector4(center.x, center.y, center.z, radius));
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);

                var mesh = TreeBaker.SaveMesh(Quad(center, radius), $"{folder}/{name}_{PartName}{TreeBaker.MeshExtension}");
                var impostor = TreeBaker.SetPart(root, PartName, mesh, material);

                if (!root.TryGetComponent<LODGroup>(out var group))
                    group = root.AddComponent<LODGroup>();
                group.fadeMode = LODFadeMode.CrossFade;
                group.animateCrossFading = false; // time-based fading takes renderers off the GPU Resident Drawer
                group.SetLODs(new[]
                {
                    new LOD(settings.transition, renderers) { fadeTransitionWidth = settings.fadeWidth },
                    new LOD(settings.cull, new Renderer[] { impostor }) { fadeTransitionWidth = settings.fadeWidth },
                });
                group.RecalculateBounds();

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        }

        /// <summary>Takes the impostor off the prefab: its child, the LODGroup and the files beside it.</summary>
        public static void Remove(string prefabPath)
        {
            var name = Path.GetFileNameWithoutExtension(prefabPath);
            var folder = Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var part = root.transform.Find(PartName);
                if (part != null)
                    Object.DestroyImmediate(part.gameObject);
                if (root.TryGetComponent<LODGroup>(out var group))
                    Object.DestroyImmediate(group);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            foreach (var file in new[] { "_Albedo.png", "_Normal.png", ".mat", TreeBaker.MeshExtension })
                AssetDatabase.DeleteAsset($"{folder}/{name}_{PartName}{file}");
        }

        static ImpostorSettings Sanitize(ImpostorSettings s)
        {
            s.frames = Mathf.Clamp(s.frames, 2, 64);
            s.atlasSize = Mathf.Clamp(s.atlasSize, 64, 8192);
            s.transition = Mathf.Clamp(s.transition, 0.002f, 1f);
            s.cull = Mathf.Clamp(s.cull, 0f, s.transition * 0.99f);
            s.fadeWidth = Mathf.Clamp01(s.fadeWidth);
            return s;
        }

        struct Part
        {
            public Mesh mesh;
            public Matrix4x4 toRoot;
            public Material[] materials;
        }

        // Centred on the bounding box, just large enough to hold every vertex.
        static (Vector3 center, float radius) BoundingSphere(List<Part> parts)
        {
            var points = parts.SelectMany(p => p.mesh.vertices.Select(v => p.toRoot.MultiplyPoint3x4(v))).ToList();
            var box = new Bounds(points[0], Vector3.zero);
            foreach (var point in points)
                box.Encapsulate(point);
            var radius = points.Max(p => (p - box.center).magnitude);
            return (box.center, Mathf.Max(radius * 1.02f, 0.01f));
        }

        // The direction a frame is captured from: toward the viewer, on the upper hemisphere. Matches TWHemiOctDecode.
        public static Vector3 FrameDirection(int x, int y, int frames)
        {
            var last = frames - 1f;
            var px = x / last * 2f - 1f;
            var py = y / last * 2f - 1f;
            var dx = (px + py) * 0.5f;
            var dz = (px - py) * 0.5f;
            return new Vector3(dx, 1f - Mathf.Abs(dx) - Mathf.Abs(dz), dz).normalized;
        }

        // Right and up across a frame. Matches TWImpostorBasis.
        public static void FrameBasis(Vector3 direction, out Vector3 right, out Vector3 up)
        {
            var reference = Mathf.Abs(direction.y) > 0.999f ? Vector3.forward : Vector3.up;
            right = Vector3.Cross(reference, direction).normalized;
            up = Vector3.Cross(direction, right);
        }

        /// <summary>Where a point in the prefab root's space lands in a frame, 0-1 across it.</summary>
        public static Vector2 FrameUV(Vector3 point, Vector3 center, float radius, Vector3 direction)
        {
            FrameBasis(direction, out var right, out var up);
            var p = point - center;
            return new Vector2(Vector3.Dot(p, right), Vector3.Dot(p, up)) / (2f * radius) + new Vector2(0.5f, 0.5f);
        }

        static void Capture(List<Part> parts, Vector3 center, float radius, int frames, int cell, out Color32[] albedo, out Color32[] normals)
        {
            var bigCell = cell * Supersample;
            var big = bigCell * frames;
            var albedoTarget = new RenderTexture(new RenderTextureDescriptor(big, big, RenderTextureFormat.ARGB32, 24) { sRGB = true });
            var normalTarget = new RenderTexture(new RenderTextureDescriptor(big, big, RenderTextureFormat.ARGB32, 0) { sRGB = false });
            var captureShader = Shader.Find(CaptureShaderName);
            var materials = new Dictionary<Material, Material>();
            var cmd = new CommandBuffer { name = "TreeWizard Impostor Capture" };
            try
            {
                cmd.SetRenderTarget(new RenderTargetIdentifier[] { albedoTarget, normalTarget }, albedoTarget);
                cmd.ClearRenderTarget(true, true, Color.clear);
                cmd.SetGlobalVector("_TWCaptureBounds", new Vector4(center.x, center.y, center.z, radius));
                var projection = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-radius, radius, -radius, radius, 0.5f * radius, 3.5f * radius), true);
                for (var y = 0; y < frames; y++)
                for (var x = 0; x < frames; x++)
                {
                    var direction = FrameDirection(x, y, frames);
                    FrameBasis(direction, out var right, out var up);
                    var eye = center + direction * (2f * radius);
                    var view = Matrix4x4.identity; // camera looking along -direction, OpenGL-style view space
                    view.SetRow(0, new Vector4(right.x, right.y, right.z, -Vector3.Dot(right, eye)));
                    view.SetRow(1, new Vector4(up.x, up.y, up.z, -Vector3.Dot(up, eye)));
                    view.SetRow(2, new Vector4(direction.x, direction.y, direction.z, -Vector3.Dot(direction, eye)));

                    cmd.SetViewport(new Rect(x * bigCell, y * bigCell, bigCell, bigCell));
                    cmd.SetGlobalVector("_TWCaptureDirection", direction);
                    foreach (var part in parts)
                    {
                        cmd.SetGlobalMatrix("_TWCaptureObjectToClip", projection * view * part.toRoot);
                        cmd.SetGlobalMatrix("_TWCaptureObjectToRoot", part.toRoot);
                        for (var sub = 0; sub < part.mesh.subMeshCount; sub++)
                        {
                            var source = part.materials.Length > 0 ? part.materials[Mathf.Min(sub, part.materials.Length - 1)] : null;
                            if (source == null)
                                continue;
                            if (!materials.TryGetValue(source, out var capture))
                                materials[source] = capture = CaptureMaterial(captureShader, source);
                            cmd.DrawMesh(part.mesh, Matrix4x4.identity, capture, sub, 0);
                        }
                    }
                }
                Graphics.ExecuteCommandBuffer(cmd);
                Downsample(Read(albedoTarget), Read(normalTarget), big, out albedo, out normals);
                Dilate(albedo, normals, cell * frames, cell);
            }
            finally
            {
                cmd.Release();
                foreach (var material in materials.Values)
                    Object.DestroyImmediate(material);
                Object.DestroyImmediate(albedoTarget);
                Object.DestroyImmediate(normalTarget);
            }
        }

        static Material CaptureMaterial(Shader shader, Material source)
        {
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            if (source.HasTexture("_BaseMap"))
            {
                material.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
                material.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap"));
                material.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap"));
            }
            if (source.HasColor("_BaseColor"))
                material.SetColor("_BaseColor", source.GetColor("_BaseColor"));
            var clipped = source.IsKeywordEnabled("_ALPHATEST_ON") || (source.HasFloat("_AlphaClip") && source.GetFloat("_AlphaClip") > 0.5f);
            material.SetFloat("_Cutoff", clipped && source.HasFloat("_Cutoff") ? source.GetFloat("_Cutoff") : -1f);
            return material;
        }

        static Color32[] Read(RenderTexture target)
        {
            var previous = RenderTexture.active;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, !target.sRGB);
            try
            {
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
                return texture.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(texture);
            }
        }

        // Averages each Supersample x Supersample block. Colour, normal and depth are weighted by coverage, so
        // edges don't darken; alpha becomes the share covered.
        static void Downsample(Color32[] albedoIn, Color32[] normalsIn, int bigSize, out Color32[] albedo, out Color32[] normals)
        {
            var size = bigSize / Supersample;
            albedo = new Color32[size * size];
            normals = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                float coverage = 0f, r = 0f, g = 0f, b = 0f, depth = 0f;
                var normal = Vector3.zero;
                for (var sy = 0; sy < Supersample; sy++)
                for (var sx = 0; sx < Supersample; sx++)
                {
                    var i = (y * Supersample + sy) * bigSize + x * Supersample + sx;
                    var c = albedoIn[i];
                    if (c.a == 0)
                        continue;
                    var n = normalsIn[i];
                    coverage += 1f;
                    r += c.r; g += c.g; b += c.b;
                    normal += new Vector3(n.r / 127.5f - 1f, n.g / 127.5f - 1f, n.b / 127.5f - 1f);
                    depth += n.a;
                }
                if (coverage == 0f)
                    continue;
                var o = y * size + x;
                albedo[o] = new Color32((byte)(r / coverage + 0.5f), (byte)(g / coverage + 0.5f), (byte)(b / coverage + 0.5f),
                    (byte)(coverage / (Supersample * Supersample) * 255f + 0.5f));
                normal = normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.up;
                normals[o] = new Color32((byte)((normal.x * 0.5f + 0.5f) * 255f + 0.5f), (byte)((normal.y * 0.5f + 0.5f) * 255f + 0.5f),
                    (byte)((normal.z * 0.5f + 0.5f) * 255f + 0.5f), (byte)(depth / coverage + 0.5f));
            }
        }

        // Spreads the nearest covered pixel's colour, normal and depth over the empty pixels of each frame, so
        // filtering and mipmaps at the silhouette never pull in black or a neighbouring frame.
        static void Dilate(Color32[] albedo, Color32[] normals, int size, int cell)
        {
            var filled = new bool[albedo.Length];
            var queue = new int[albedo.Length];
            int head = 0, tail = 0;
            for (var i = 0; i < albedo.Length; i++)
            {
                if (albedo[i].a > 0)
                {
                    filled[i] = true;
                    queue[tail++] = i;
                }
            }
            while (head < tail)
            {
                var i = queue[head++];
                int x = i % size, y = i / size;
                for (var k = 0; k < 4; k++)
                {
                    int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (nx < 0 || ny < 0 || nx >= size || ny >= size || nx / cell != x / cell || ny / cell != y / cell)
                        continue;
                    var n = ny * size + nx;
                    if (filled[n])
                        continue;
                    filled[n] = true;
                    var c = albedo[i];
                    albedo[n] = new Color32(c.r, c.g, c.b, 0);
                    normals[n] = normals[i];
                    queue[tail++] = n;
                }
            }
        }

        // Overwrites the file in place, so its GUID and import settings are kept.
        static Texture2D SaveTexture(Color32[] pixels, int size, string path, bool colour)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, !colour);
            try
            {
                texture.SetPixels32(pixels);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var maxSize = Mathf.NextPowerOfTwo(size);
            if (importer.sRGBTexture != colour || importer.mipMapsPreserveCoverage != colour || importer.wrapMode != TextureWrapMode.Clamp
                || importer.npotScale != TextureImporterNPOTScale.None || importer.maxTextureSize < maxSize || importer.alphaIsTransparency
                || importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = colour;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false; // the empty pixels already hold the edge colours
                importer.mipmapEnabled = true;
                importer.mipMapsPreserveCoverage = colour;
                importer.alphaTestReferenceValue = 0.5f;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, maxSize);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Made once, then left to the user apart from what the bake owns, so tweaks to cutoff or smoothness stay.
        static Material SaveMaterial(string path, MeshRenderer[] sources)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;
            var shader = Shader.Find(ShaderName);
            if (shader == null)
                throw new InvalidOperationException($"Shader {ShaderName} is missing.");
            material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            var smoothness = sources.SelectMany(r => r.sharedMaterials).Where(m => m != null && m.HasFloat(SmoothnessId))
                .Select(m => m.GetFloat(SmoothnessId)).DefaultIfEmpty(0.1f).Average();
            material.SetFloat(SmoothnessId, smoothness);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // A quad with corners at the centre +- radius in XY. The shader turns it to face the viewer; the bounds
        // cover the whole sphere so culling never drops it.
        static Mesh Quad(Vector3 center, float radius)
        {
            var mesh = new Mesh();
            mesh.SetVertices(new[]
            {
                center + new Vector3(-radius, -radius), center + new Vector3(radius, -radius),
                center + new Vector3(radius, radius), center + new Vector3(-radius, radius),
            });
            mesh.SetNormals(Enumerable.Repeat(Vector3.forward, 4).ToArray());
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.bounds = new Bounds(center, Vector3.one * (2f * radius));
            return mesh;
        }
    }
}
