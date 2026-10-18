using System;
using System.IO;
using TreeWizard;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TreeWizardDev
{
    /// <summary>
    /// Renders a row of generated trees (one per seed) to a PNG, to eyeball generator changes
    /// without opening the Editor:
    ///   Unity -batchmode -projectPath . -executeMethod TreeWizardDev.TreePreview.Render -previewOut out.png [-previewSeeds 4] [-previewAutumn 0.5]
    /// Don't pass -nographics; it needs a GPU.
    /// </summary>
    public static class TreePreview
    {
        const int TileWidth = 400;
        const int Height = 600;
        const float Spacing = 7f;

        public static void Render()
        {
            try
            {
                var outPath = Arg("-previewOut") ?? "TreePreview.png";
                var preset = Arg("-previewPreset") ?? "Oak";
                var seeds = int.TryParse(Arg("-previewSeeds"), out var n) ? Mathf.Max(1, n) : 4;
                var autumn = float.TryParse(Arg("-previewAutumn"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var a) ? a : 0f;
                RenderTo(outPath, preset, seeds, autumn);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("TreeWizard/Dev/Render Preview PNG")]
        static void RenderFromMenu() => RenderTo(Path.GetFullPath("../Scratch/TreePreview.png"), "Oak", 4, 0f);

        /// <summary>
        /// Renders a row of trees from the named preset (one per seed) in a temporary scene, then
        /// reopens the scene that was open before. <paramref name="tweak"/> can change the settings first, for trying ideas
        /// without rebuilding the preset.
        /// </summary>
        /// <param name="baseCloseUp">Frame the base of the first tree from standing height instead, to judge roots and flare.</param>
        public static void RenderTo(string outPath, string presetName, int seeds, float autumn, Action<TreeDefinition> tweak = null, bool baseCloseUp = false)
        {
            var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            var presetPath = $"Packages/com.treewizard.core/Presets/{presetName}/{presetName}.asset";
            if (AssetDatabase.LoadAssetAtPath<TreePreset>(presetPath) == null)
                throw new FileNotFoundException($"No preset named {presetName}");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                // Loaded after the new scene: opening it unloads unreferenced assets, which would leave a preset
                // loaded before it without its leaf atlas.
                var preset = AssetDatabase.LoadAssetAtPath<TreePreset>(presetPath);
                var bounds = new Bounds();
                var spacing = Mathf.Max(Spacing, preset.definition.trunk.height * 0.8f);
                for (var i = 0; i < seeds; i++)
                {
                    var tree = new GameObject($"Tree {i + 1}").AddComponent<TreeWizardTree>();
                    tree.transform.position = new Vector3(i * spacing, 0f, 0f);
                    tree.ApplyPreset(preset);
                    tree.definition.seed = i + 1;
                    tree.definition.leaves.autumn = autumn;
                    tweak?.Invoke(tree.definition);
                    tree.Rebuild();
                    var b = tree.GetComponent<MeshRenderer>().bounds;
                    if (i == 0)
                        bounds = b;
                    else
                        bounds.Encapsulate(b);
                    Debug.Log($"[TreePreview] {presetName} seed {i + 1}: {tree.Mesh.vertexCount} verts, {tree.Mesh.triangles.Length / 3} tris");
                }

                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.transform.position = new Vector3(bounds.center.x, 0f, 0f);
                floor.transform.localScale = new Vector3(bounds.size.x * 0.2f + 2f, 1f, 4f);
                floor.GetComponent<MeshRenderer>().sharedMaterial = Matte(new Color(0.45f, 0.43f, 0.38f));

                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.6f;
                sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.45f, 0.5f, 0.6f);

                if (baseCloseUp)
                {
                    var trunk = preset.definition.trunk;
                    var reach = trunk.radius * 10f;
                    Capture(new Bounds(new Vector3(0f, trunk.radius * 2f, 0f), new Vector3(reach, trunk.radius * 4f, reach)), outPath, 1200, 800, 25f);
                    return;
                }

                var width = TileWidth * seeds;
                var cam = new GameObject("Camera").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.62f, 0.75f, 0.88f);
                cam.fieldOfView = 32f;
                cam.aspect = (float)width / Height;
                var halfFov = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                var distance = Mathf.Max(bounds.extents.y * 1.1f, bounds.extents.x * 1.05f / cam.aspect) / halfFov + bounds.extents.z;
                cam.transform.position = new Vector3(bounds.center.x, bounds.center.y * 0.8f, -distance);
                cam.transform.LookAt(new Vector3(bounds.center.x, bounds.center.y, 0f));
                cam.farClipPlane = distance * 3f;

                WriteRender(cam, width, Height, outPath);
            }
            finally
            {
                if (!string.IsNullOrEmpty(previous))
                    EditorSceneManager.OpenScene(previous);
            }
        }

        /// <summary>Renders <paramref name="target"/> from a 3/4 view with a temporary camera. Works while the Editor is unfocused.</summary>
        public static void Capture(GameObject target, string outPath, int width = 900, int height = 900) =>
            Capture(target.GetComponent<Renderer>().bounds, outPath, width, height);

        /// <summary>Renders the region <paramref name="bounds"/> from a 3/4 view, e.g. part of a tree for a close-up.</summary>
        public static void Capture(Bounds bounds, string outPath, int width = 900, int height = 900, float pitch = 10f)
        {
            var cam = new GameObject("TreePreview Camera") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
            try
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.62f, 0.75f, 0.88f);
                cam.fieldOfView = 30f;
                cam.aspect = (float)width / height;
                var distance = bounds.extents.magnitude / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                cam.transform.position = bounds.center + Quaternion.Euler(pitch, -30f, 0f) * Vector3.back * distance;
                cam.transform.LookAt(bounds.center);
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = distance * 4f;
                WriteRender(cam, width, height, outPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cam.gameObject);
            }
        }

        static void WriteRender(Camera cam, int width, int height, string outPath)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render(); // warm-up: the first frame can draw with stale per-material constants
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(rt);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            Debug.Log($"[TreePreview] wrote {Path.GetFullPath(outPath)}");
        }

        // The empty scene's default reflections look muddy at grazing angles, so keep everything rough.
        static Material Matte(Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            material.SetFloat("_Smoothness", 0f);
            material.SetFloat("_EnvironmentReflections", 0f);
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            return material;
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
