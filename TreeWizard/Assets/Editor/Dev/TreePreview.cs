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
        const string OakAtlas = "Packages/com.treewizard.core/Presets/Oak/OakLeaves.asset";

        public static void Render()
        {
            try
            {
                var outPath = Arg("-previewOut") ?? "TreePreview.png";
                var seeds = int.TryParse(Arg("-previewSeeds"), out var n) ? Mathf.Max(1, n) : 4;
                var autumn = float.TryParse(Arg("-previewAutumn"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var a) ? a : 0f;
                RenderTo(outPath, seeds, autumn);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("TreeWizard/Dev/Render Preview PNG")]
        static void RenderFromMenu() => RenderTo(Path.GetFullPath("../Scratch/TreePreview.png"), 4, 0f);

        static void RenderTo(string outPath, int seeds, float autumn)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var bark = Matte(new Color(0.33f, 0.24f, 0.17f));
            var ground = Matte(new Color(0.45f, 0.43f, 0.38f));
            var atlas = AssetDatabase.LoadAssetAtPath<LeafAtlas>(OakAtlas);
            var leaf = Matte(atlas != null ? Color.white : new Color(0.22f, 0.42f, 0.14f));
            if (atlas != null)
            {
                leaf.SetTexture("_BaseMap", atlas.texture);
                leaf.SetFloat("_AlphaClip", 1f);
                leaf.SetFloat("_Cutoff", 0.5f);
                leaf.EnableKeyword("_ALPHATEST_ON");
            }

            for (var i = 0; i < seeds; i++)
            {
                var tree = new GameObject($"Tree {i + 1}").AddComponent<TreeWizardTree>();
                tree.transform.position = new Vector3(i * Spacing, 0f, 0f);
                tree.definition.seed = i + 1;
                tree.barkMaterial = bark;
                tree.leafMaterial = leaf;
                tree.leafAtlas = atlas;
                tree.definition.leaves.autumn = autumn;
                tree.Rebuild();
                Debug.Log($"[TreePreview] seed {i + 1}: {tree.Mesh.vertexCount} verts, {tree.Mesh.triangles.Length / 3} tris");
            }

            var centerX = (seeds - 1) * Spacing * 0.5f;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.position = new Vector3(centerX, 0f, 0f);
            floor.transform.localScale = new Vector3(seeds * Spacing * 0.2f, 1f, 3f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = ground;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.5f, 0.6f);

            var width = TileWidth * seeds;
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.75f, 0.88f);
            cam.fieldOfView = 32f;
            cam.aspect = (float)width / Height;
            var distance = Mathf.Max(5.5f, seeds * Spacing * 0.5f / cam.aspect) / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            cam.transform.position = new Vector3(centerX, 3.5f, -distance);
            cam.transform.LookAt(new Vector3(centerX, 3.8f, 0f));

            var rt = new RenderTexture(width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render(); // warm-up: the first frame can draw with stale per-material constants
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
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
