using System.IO;
using System.Linq;
using TreeWizard.Fbx;
using UnityEngine;
using N = System.Numerics;

namespace TreeWizard
{
    /// <summary>
    /// Exports trees as FBX, with each material's texture saved as a PNG beside the file.
    /// Works in the Editor and in built players.
    /// </summary>
    public static class TreeFbxExporter
    {
        /// <summary>Writes <paramref name="tree"/> to <paramref name="fbxPath"/>, plus NAME_Bark.png and NAME_Leaves.png when its materials have textures.</summary>
        public static void Export(TreeWizardTree tree, string fbxPath)
        {
            var data = TreeGenerator.Generate(tree.definition, tree.leafAtlas != null ? tree.leafAtlas.cells : null);
            Export(data, fbxPath, tree.barkMaterial, tree.leafMaterial);
        }

        public static void Export(MeshData data, string fbxPath, Material bark, Material leaves)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(fbxPath));
            var name = Path.GetFileNameWithoutExtension(fbxPath);

            var mesh = FbxMesh.FromSubmeshes(name,
                data.vertices.Select(v => new N.Vector3(v.x, v.y, v.z)).ToArray(),
                data.normals.Select(n => new N.Vector3(n.x, n.y, n.z)).ToArray(),
                data.uvs.Select(uv => new N.Vector2(uv.x, uv.y)).ToArray(),
                new[] { data.Triangles(MeshData.BarkSubmesh), data.Triangles(MeshData.LeafSubmesh) });
            var model = new FbxModel(name) { Mesh = mesh };
            model.Materials.Add(ToFbxMaterial(bark, $"{name}_Bark", directory, false));
            model.Materials.Add(ToFbxMaterial(leaves, $"{name}_Leaves", directory, true));

            var scene = new FbxScene();
            scene.Models.Add(model);
            FbxSceneWriter.WriteFile(fbxPath, scene, new FbxWriteOptions { ConvertFromLeftHanded = true });
        }

        static FbxMaterial ToFbxMaterial(Material source, string name, string directory, bool cutout)
        {
            var material = new FbxMaterial(name);
            if (source == null)
                return material;

            var color = MainColor(source).linear; // FBX colours are linear
            material.DiffuseColor = new N.Vector3(color.r, color.g, color.b);
            if (source.mainTexture != null)
            {
                // Opaque materials get an RGB file, so importers don't hook up an alpha channel they shouldn't use.
                var albedo = ReadPixels(source.mainTexture, cutout);
                try
                {
                    var file = name + ".png";
                    File.WriteAllBytes(Path.Combine(directory, file), albedo.EncodeToPNG());
                    material.DiffuseTexture = file;
                    material.AlphaFromDiffuseTexture = cutout;

                    // An imported normal map is GPU-compressed with swizzled channels, so rather than copy
                    // it back out, rebuild it from the colour texture the way the presets' maps were made.
                    // One made at runtime (TextureImport) is plain readable pixels, which are kept as they are.
                    var bump = source.HasProperty("_BumpMap") ? source.GetTexture("_BumpMap") : null;
                    if (bump != null)
                    {
                        var runtimeMap = bump is Texture2D map && map.isReadable ? map : null;
                        var normals = runtimeMap != null
                            ? new Texture2D(runtimeMap.width, runtimeMap.height, TextureFormat.RGB24, false)
                            : new Texture2D(albedo.width, albedo.height, TextureFormat.RGB24, false);
                        normals.SetPixels32(runtimeMap != null
                            ? runtimeMap.GetPixels32()
                            : NormalMapGenerator.FromAlbedo(albedo.GetPixels32(), albedo.width, albedo.height));
                        var normalFile = name + "_Normal.png";
                        File.WriteAllBytes(Path.Combine(directory, normalFile), normals.EncodeToPNG());
                        Object.DestroyImmediate(normals);
                        material.NormalTexture = normalFile;
                    }
                }
                finally
                {
                    Object.DestroyImmediate(albedo);
                }
            }
            return material;
        }

        static Color MainColor(Material material)
        {
            foreach (var property in new[] { "_BaseColor", "_Color" })
            {
                if (material.HasProperty(property))
                    return material.GetColor(property);
            }
            return Color.white;
        }

        /// <summary>
        /// Saves any texture as PNG, including ones that aren't CPU-readable or are GPU-compressed.
        /// </summary>
        public static void SaveTexturePng(Texture texture, string path)
        {
            var readable = ReadPixels(texture, true);
            try
            {
                File.WriteAllBytes(path, readable.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(readable);
            }
        }

        /// <summary>
        /// A CPU-readable copy of any texture, made by drawing it into a render texture and reading
        /// that back. The caller destroys it.
        /// </summary>
        static Texture2D ReadPixels(Texture texture, bool keepAlpha)
        {
            var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var readable = new Texture2D(texture.width, texture.height, keepAlpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                readable.Apply();
                return readable;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
