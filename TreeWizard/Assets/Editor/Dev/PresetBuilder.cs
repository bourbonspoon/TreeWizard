using System;
using System.IO;
using System.Linq;
using TreeWizard.Editor;
using UnityEditor;
using UnityEngine;

namespace TreeWizardDev
{
    /// <summary>
    /// Rebuilds the package's preset leaf atlases from the source sheets in RawTextures/ (repo root,
    /// kept out of git to keep clones light, so only maintainers with the sheets can run this):
    ///   Unity -batchmode -projectPath . -executeMethod TreeWizardDev.PresetBuilder.BuildAll
    /// </summary>
    public static class PresetBuilder
    {
        const string RawTextures = "../RawTextures";
        const string Presets = "Packages/com.treewizard.core/Presets";

        [MenuItem("TreeWizard/Dev/Rebuild Preset Atlases")]
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
            var sheets = Directory.GetFiles(RawTextures, "*.png")
                .Where(p => Path.GetFileName(p).StartsWith("oak-twigs", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var atlas = LeafAtlasBuilder.Build(sheets, $"{Presets}/Oak/OakLeaves.asset");
            Debug.Log($"[PresetBuilder] Oak: {sheets.Count} sheets -> {atlas.cells.Count} cells " +
                      $"({atlas.cells.Count(c => c.autumn)} autumn), {atlas.texture.width}px");
        }
    }
}
