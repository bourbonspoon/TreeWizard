using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TreeWizard.Editor
{
    /// <summary>Slices leaf sheets into an atlas texture plus a <see cref="LeafAtlas"/> asset.</summary>
    public static class LeafAtlasBuilder
    {
        /// <param name="sheetPaths">PNG files with transparent backgrounds and stems pointing down. Can be outside the project.</param>
        /// <param name="assetPath">Where to write the LeafAtlas, e.g. "Assets/Trees/OakLeaves.asset". The texture goes beside it as a .png.</param>
        public static LeafAtlas Build(IReadOnlyList<string> sheetPaths, string assetPath, int maxSize = 2048)
        {
            var sheets = new List<LeafSheetSlicer.Sheet>();
            foreach (var path in sheetPaths)
                sheets.Add(Load(path));
            var result = LeafSheetSlicer.Build(sheets, maxSize);

            var pngPath = Path.ChangeExtension(assetPath, ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(pngPath));
            var texture = new Texture2D(result.size, result.size, TextureFormat.RGBA32, false);
            texture.SetPixels32(result.pixels);
            File.WriteAllBytes(pngPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.mipMapsPreserveCoverage = true; // stops cutout leaves thinning out with distance
            importer.alphaTestReferenceValue = 0.5f;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = Mathf.Max(result.size, 32);
            importer.SaveAndReimport();

            var atlas = AssetDatabase.LoadAssetAtPath<LeafAtlas>(assetPath);
            if (atlas == null)
            {
                atlas = ScriptableObject.CreateInstance<LeafAtlas>();
                AssetDatabase.CreateAsset(atlas, assetPath);
            }
            atlas.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
            atlas.cells = result.cells;
            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssets();
            return atlas;
        }

        static LeafSheetSlicer.Sheet Load(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path)))
                throw new IOException($"Couldn't read image {path}");
            var sheet = new LeafSheetSlicer.Sheet { pixels = texture.GetPixels32(), width = texture.width, height = texture.height };
            Object.DestroyImmediate(texture);
            return sheet;
        }
    }
}
