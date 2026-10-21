using System.IO;
using TreeWizard;
using TreeWizard.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TreeWizardDev
{
    /// <summary>
    /// Renders a preset's full tree beside its impostor from several views, one row per view, to check the
    /// impostor matches: shape, lighting, shadows. Bakes into a scratch folder that it deletes afterwards.
    /// </summary>
    public static class ImpostorPreview
    {
        const string Folder = "Assets/__ImpostorPreview";

        /// <summary>Each view is (pitch, yaw) of the camera in degrees, and the sun is (pitch, yaw).</summary>
        public static void Compare(string presetName, string outPath, Vector2[] views, Vector2 sun, int tile = 600, ImpostorSettings settings = null, System.Action<Material> tweak = null, float sunIntensity = 1.6f, bool orthographic = false)
        {
            var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            var async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false; // a new shader would otherwise skip its first draws
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", "__ImpostorPreview");
            try
            {
                var preset = AssetDatabase.LoadAssetAtPath<TreePreset>($"Packages/com.treewizard.core/Presets/{presetName}/{presetName}.asset");
                var source = new GameObject("Source").AddComponent<TreeWizardTree>();
                source.ApplyPreset(preset);
                source.Rebuild();
                var prefab = TreeBaker.BakeToPrefab(source, $"{Folder}/{presetName}.prefab", settings ?? new ImpostorSettings());
                Object.DestroyImmediate(source.gameObject);

                var material = prefab.transform.Find(ImpostorBaker.PartName).GetComponent<MeshRenderer>().sharedMaterial;
                tweak?.Invoke(material);
                var radius = material.GetVector("_ImpostorBounds").w;
                var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var group = tree.GetComponent<LODGroup>();

                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.transform.localScale = Vector3.one * radius * 1.2f;
                floor.GetComponent<MeshRenderer>().sharedMaterial = Matte(new Color(0.45f, 0.43f, 0.38f));
                var light = new GameObject("Sun").AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = sunIntensity;
                light.shadows = LightShadows.Soft;
                light.transform.rotation = Quaternion.Euler(sun.x, sun.y, 0f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.45f, 0.5f, 0.6f);

                // Each row: the same camera on the full tree (left) and the impostor (right).
                var sheet = new Texture2D(tile * 2, tile * views.Length, TextureFormat.RGB24, false);
                var focus = new Vector3(0f, group.transform.TransformPoint(group.localReferencePoint).y, 0f);
                for (var i = 0; i < views.Length; i++)
                {
                    var row = views.Length - 1 - i; // first view at the top
                    var rotation = Quaternion.Euler(views[i].x, views[i].y, 0f);
                    for (var level = 0; level < 2; level++)
                    {
                        group.ForceLOD(level);
                        var shot = Render(rotation, focus, radius * 1.3f, tile, tile, orthographic);
                        sheet.SetPixels(level * tile, row * tile, tile, tile, shot.GetPixels());
                        Object.DestroyImmediate(shot);
                    }
                }
                sheet.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
                File.WriteAllBytes(outPath, sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);
                Debug.Log($"[ImpostorPreview] wrote {Path.GetFullPath(outPath)}");
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
                if (!string.IsNullOrEmpty(previous))
                    EditorSceneManager.OpenScene(previous);
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        static Texture2D Render(Quaternion rotation, Vector3 target, float halfWidth, int width, int height, bool orthographic)
        {
            var cam = new GameObject("Camera") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            try
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.62f, 0.75f, 0.88f);
                cam.fieldOfView = 20f;
                cam.aspect = (float)width / height;
                var distance = halfWidth / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / cam.aspect;
                cam.transform.SetPositionAndRotation(target - rotation * Vector3.forward * distance, rotation);
                cam.nearClipPlane = distance * 0.2f;
                cam.farClipPlane = distance * 3f;
                cam.orthographic = orthographic;
                cam.orthographicSize = halfWidth;
                cam.targetTexture = rt;
                cam.Render(); // warm-up: the first frame can draw with stale per-material constants
                cam.Render();
                RenderTexture.active = rt;
                var shot = new Texture2D(width, height, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                RenderTexture.active = null;
                return shot;
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(cam.gameObject);
            }
        }

        static Material Matte(Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0f);
            return material;
        }
    }
}
