using System;
using System.Collections.Generic;
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
    /// Builds the package's presets: per species a leaf atlas, bark texture, materials and TreePreset.
    ///   Unity -batchmode -projectPath . -executeMethod TreeWizardDev.PresetBuilder.BuildAll
    /// Textures are rebuilt only when the source images are present in RawTextures/ (repo root, kept
    /// out of git to keep clones light); otherwise the committed ones are reused. The tree shape
    /// always comes from the code below, so edit it here rather than in the preset asset.
    /// </summary>
    public static class PresetBuilder
    {
        const string RawTextures = "../RawTextures";
        const string Presets = "Packages/com.treewizard.core/Presets";

        class Species
        {
            public string Name;
            public string[] LeafSheetPrefixes;
            public string BarkFile;
            public Func<TreeDefinition> Definition;
        }

        static Species[] All => new[]
        {
            new Species
            {
                Name = "Oak",
                LeafSheetPrefixes = new[] { "oak-twigs" },
                BarkFile = "Oak-bark-seamless-texture-251.png",
                Definition = () => new TreeDefinition(),
            },
            new Species
            {
                Name = "Spruce",
                LeafSheetPrefixes = new[] { "spruce-branch-tips", "six-spruce-branch-tips" },
                BarkFile = "Spruce-bark-texture-seamless-tile-258.png",
                Definition = Spruce,
            },
        };

        static TreeDefinition Spruce() => new TreeDefinition
        {
            trunk = new TrunkSettings
            {
                height = 9f,
                radius = 0.2f,
                tipRadius = 0.04f,
                flare = 0.35f,
                flareHeight = 0.06f,
                crookedness = 0.04f,
                segments = 12,
            },
            branchLevels = new[]
            {
                // Tiers of near-horizontal, drooping limbs that shorten towards the top: the cone.
                new BranchLevelSettings
                {
                    count = 34, start = 0.14f, end = 0.97f,
                    angle = 82f, angleVariance = 8f,
                    length = 0.28f, lengthAtEnd = 0.08f, lengthVariance = 0.15f,
                    radius = 0.4f, gravity = 0.2f, crookedness = 0.08f, segments = 5,
                },
                new BranchLevelSettings
                {
                    count = 7, start = 0.15f, end = 0.95f,
                    angle = 55f, angleVariance = 12f,
                    length = 0.38f, lengthAtEnd = 0.55f, lengthVariance = 0.25f,
                    radius = 0.6f, gravity = 0.1f, crookedness = 0.15f, segments = 3,
                },
            },
            leaves = new LeafSettings
            {
                count = 5, start = 0.05f, size = 0.62f, sizeVariance = 0.25f, angle = 25f,
            },
            mesh = new MeshSettings { radialSegments = 8, minRadialSegments = 3 },
        };

        [MenuItem("TreeWizard/Dev/Rebuild Presets")]
        public static void BuildFromMenu()
        {
            foreach (var species in All)
                Build(species);
        }

        public static void BuildAll()
        {
            try
            {
                BuildFromMenu();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        static void Build(Species species)
        {
            var dir = $"{Presets}/{species.Name}";
            Directory.CreateDirectory(dir);
            var atlas = BuildAtlas(species, $"{dir}/{species.Name}Leaves.asset");
            var barkTexture = BuildBark(species, $"{dir}/{species.Name}Bark.jpg", $"{dir}/{species.Name}BarkNormal.png", out var barkNormal);
            var bark = Lit($"{dir}/{species.Name}Bark.mat", barkTexture, false, barkNormal);
            var leaves = Lit($"{dir}/{species.Name}Leaves.mat", atlas.texture, true);

            var path = $"{dir}/{species.Name}.asset";
            var preset = AssetDatabase.LoadAssetAtPath<TreePreset>(path);
            if (preset == null)
            {
                preset = ScriptableObject.CreateInstance<TreePreset>();
                AssetDatabase.CreateAsset(preset, path);
            }
            preset.definition = species.Definition();
            preset.barkMaterial = bark;
            preset.leafMaterial = leaves;
            preset.leafAtlas = atlas;
            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PresetBuilder] {species.Name}: {atlas.cells.Count} leaf cells ({atlas.cells.Count(c => c.autumn)} autumn), " +
                      $"{atlas.texture.width}px atlas, bark {(barkTexture != null ? barkTexture.width + "px" : "none")}, normal map {(barkNormal != null ? "yes" : "no")}");
        }

        static LeafAtlas BuildAtlas(Species species, string assetPath)
        {
            var sheets = SourceFiles()
                .Where(p => species.LeafSheetPrefixes.Any(prefix => Path.GetFileName(p).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (sheets.Count > 0)
                return LeafAtlasBuilder.Build(sheets, assetPath);

            var existing = AssetDatabase.LoadAssetAtPath<LeafAtlas>(assetPath);
            if (existing == null)
                throw new FileNotFoundException($"No leaf sheets for {species.Name} in {RawTextures} and no existing atlas at {assetPath}");
            return existing;
        }

        /// <summary>
        /// Copies the bark tile into the package as a JPG (it has no alpha, and JPG is a fraction of the
        /// size) and generates its normal map, kept as PNG because JPG artefacts show up in lighting.
        /// </summary>
        static Texture2D BuildBark(Species species, string assetPath, string normalPath, out Texture2D normal)
        {
            var source = Path.Combine(RawTextures, species.BarkFile);
            if (File.Exists(source))
            {
                var texture = new Texture2D(2, 2);
                texture.LoadImage(File.ReadAllBytes(source));
                File.WriteAllBytes(assetPath, texture.EncodeToJPG(90));

                var normals = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
                normals.SetPixels32(NormalMapGenerator.FromAlbedo(texture.GetPixels32(), texture.width, texture.height));
                File.WriteAllBytes(normalPath, normals.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(normals);

                ImportTiling(assetPath, false);
                ImportTiling(normalPath, true);
            }
            normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        static void ImportTiling(string path, bool normalMap)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (normalMap)
                importer.textureType = TextureImporterType.NormalMap;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }

        static IEnumerable<string> SourceFiles() =>
            Directory.Exists(RawTextures) ? Directory.GetFiles(RawTextures, "*.png") : Enumerable.Empty<string>();

        /// <summary>URP Lit material; alpha-clipped for leaves.</summary>
        static Material Lit(string path, Texture2D texture, bool cutout, Texture2D normalMap = null)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", texture != null ? Color.white : new Color(0.36f, 0.27f, 0.2f));
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Smoothness", 0.15f);
            material.SetTexture("_BumpMap", normalMap);
            material.SetFloat("_BumpScale", 1f);
            if (normalMap != null)
                material.EnableKeyword("_NORMALMAP");
            else
                material.DisableKeyword("_NORMALMAP");
            if (cutout)
            {
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
