using System;
using System.IO;
using System.Linq;
using TreeWizard;
using TreeWizard.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TreeWizardDev
{
    /// <summary>
    /// Builds the package's presets: leaf atlas, materials and TreePreset asset per species.
    ///   Unity -batchmode -projectPath . -executeMethod TreeWizardDev.PresetBuilder.BuildAll
    /// Atlases are rebuilt only when the source sheets are present in RawTextures/ (repo root, kept
    /// out of git to keep clones light); otherwise the committed atlas is reused.
    /// </summary>
    public static class PresetBuilder
    {
        const string RawTextures = "../RawTextures";
        const string Presets = "Packages/com.treewizard.core/Presets";

        [MenuItem("TreeWizard/Dev/Rebuild Presets")]
        public static void BuildFromMenu() => BuildOak();

        public static void BuildAll()
        {
            try
            {
                BuildOak();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        static void BuildOak()
        {
            var dir = $"{Presets}/Oak";
            var atlas = BuildAtlas("oak-twigs", $"{dir}/OakLeaves.asset");
            var bark = Lit($"{dir}/OakBark.mat", new Color(0.36f, 0.27f, 0.2f));
            var leaves = Lit($"{dir}/OakLeaves.mat", Color.white, atlas.texture);

            var preset = AssetDatabase.LoadAssetAtPath<TreePreset>($"{dir}/Oak.asset");
            if (preset == null)
            {
                preset = ScriptableObject.CreateInstance<TreePreset>();
                preset.definition = new TreeDefinition();
                AssetDatabase.CreateAsset(preset, $"{dir}/Oak.asset");
            }
            preset.barkMaterial = bark;
            preset.leafMaterial = leaves;
            preset.leafAtlas = atlas;
            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PresetBuilder] Oak: {atlas.cells.Count} leaf cells ({atlas.cells.Count(c => c.autumn)} autumn), {atlas.texture.width}px atlas");
        }

        static LeafAtlas BuildAtlas(string sheetPrefix, string assetPath)
        {
            var sheets = Directory.Exists(RawTextures)
                ? Directory.GetFiles(RawTextures, "*.png")
                    .Where(p => Path.GetFileName(p).StartsWith(sheetPrefix, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new System.Collections.Generic.List<string>();
            if (sheets.Count > 0)
                return LeafAtlasBuilder.Build(sheets, assetPath);

            var existing = AssetDatabase.LoadAssetAtPath<LeafAtlas>(assetPath);
            if (existing == null)
                throw new FileNotFoundException($"No '{sheetPrefix}*' sheets in {RawTextures} and no existing atlas at {assetPath}");
            return existing;
        }

        /// <summary>URP Lit material, alpha-clipped when it has a texture (leaves).</summary>
        static Material Lit(string path, Color color, Texture2D cutout = null)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.15f);
            if (cutout != null)
            {
                material.SetTexture("_BaseMap", cutout);
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", 0.5f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = (int)RenderQueue.AlphaTest;
            }
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
