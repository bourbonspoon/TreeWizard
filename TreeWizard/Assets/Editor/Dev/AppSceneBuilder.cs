using System.Collections.Generic;
using TreeWizard;
using TreeWizard.App;
using TreeWizard.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace TreeWizardDev
{
    /// <summary>
    /// Builds the standalone app's scene and UI assets from code, so they're reproducible:
    /// TreeWizard/Dev/Rebuild App Scene.
    /// </summary>
    public static class AppSceneBuilder
    {
        const string AppFolder = "Assets/App";
        const string ScenePath = AppFolder + "/TreeWizardApp.unity";
        const string PanelSettingsPath = AppFolder + "/UI/AppPanelSettings.asset";
        const string ThemePath = AppFolder + "/UI/AppTheme.tss";
        const string LayoutPath = AppFolder + "/UI/App.uxml";
        const string GroundMaterialPath = AppFolder + "/Ground.mat";

        [MenuItem("TreeWizard/Dev/Rebuild App Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Skybox;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.sun = sun;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(10f, 1f, 10f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = GroundMaterial();
            Object.DestroyImmediate(ground.GetComponent<Collider>());

            var tree = new GameObject("Tree").AddComponent<TreeWizardTree>();
            var oak = AssetDatabase.LoadAssetAtPath<TreePreset>(TreeWizardMenus.DefaultPreset);
            tree.ApplyPreset(oak);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 40f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 500f;
            var orbit = cameraObject.AddComponent<OrbitCamera>();

            var uiObject = new GameObject("App UI");
            var document = uiObject.AddComponent<UIDocument>();
            document.panelSettings = PanelSettings();
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LayoutPath);
            orbit.ui = document;

            var app = uiObject.AddComponent<TreeWizardApp>();
            var so = new SerializedObject(app);
            so.FindProperty("tree").objectReferenceValue = tree;
            so.FindProperty("orbit").objectReferenceValue = orbit;
            var presets = so.FindProperty("presets");
            var all = FindPresets();
            presets.arraySize = all.Count;
            for (var i = 0; i < all.Count; i++)
                presets.GetArrayElementAtIndex(i).objectReferenceValue = all[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log($"[AppSceneBuilder] Built {ScenePath} with {all.Count} presets");
        }

        static List<TreePreset> FindPresets()
        {
            var presets = new List<TreePreset>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(TreePreset), new[] { TreeWizardMenus.PackageRoot + "/Presets" }))
                presets.Add(AssetDatabase.LoadAssetAtPath<TreePreset>(AssetDatabase.GUIDToAssetPath(guid)));
            presets.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return presets;
        }

        static PanelSettings PanelSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            }
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            settings.scaleMode = PanelScaleMode.ConstantPhysicalSize; // same physical size on retina and standard displays
            settings.referenceDpi = 96f;
            settings.fallbackDpi = 96f;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        static Material GroundMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, GroundMaterialPath);
            }
            material.SetColor("_BaseColor", new Color(0.36f, 0.4f, 0.3f));
            material.SetFloat("_Smoothness", 0.05f);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
