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
            /// <summary>Multiplies the leaf texture, e.g. to darken bright source sheets.</summary>
            public Color LeafTint = Color.white;
        }

        static Species[] All => new[]
        {
            new Species
            {
                Name = "Oak",
                LeafSheetPrefixes = new[] { "oak-twigs" },
                BarkFile = "Oak-bark-seamless-texture-251.png",
                Definition = () => new TreeDefinition { roots = { count = 6 } },
            },
            new Species
            {
                Name = "Spruce",
                LeafSheetPrefixes = new[] { "spruce-branch-tips", "six-spruce-branch-tips" },
                BarkFile = "Spruce-bark-texture-seamless-tile-258.png",
                Definition = Spruce,
                LeafTint = new Color(0.62f, 0.72f, 0.72f), // spruce is a darker blue-green than the generated sheets
            },
            new Species
            {
                Name = "Beech",
                LeafSheetPrefixes = new[] { "beech-branch-tips" },
                BarkFile = "Beech-bark-seamless-texture-271.png", // the smoothest of the three, as beech bark is
                Definition = Beech,
            },
        };

        static TreeDefinition Beech() => new TreeDefinition
        {
            // A smooth grey trunk under a broad, dense dome: limbs spread wide and shorten towards the top.
            trunk = new TrunkSettings
            {
                height = 9f,
                radius = 0.28f,
                tipRadius = 0.12f,
                flare = 0.5f,
                flareHeight = 0.06f,
                crookedness = 0.06f,
                segments = 12,
            },
            roots = new RootSettings { count = 7, length = 6f, radius = 0.55f },
            branchLevels = new[]
            {
                new BranchLevelSettings
                {
                    count = 16, start = 0.28f, end = 0.95f,
                    angle = 48f, angleVariance = 10f,
                    length = 0.58f, lengthAtEnd = 0.35f, lengthVariance = 0.2f,
                    radius = 0.55f, gravity = 0.18f, crookedness = 0.2f, segments = 7,
                },
                // Twigs fan out to the sides and droop a little, in the layered sprays beech is known for.
                new BranchLevelSettings
                {
                    count = 10, start = 0.25f, end = 1f,
                    angle = 50f, angleVariance = 12f, flatten = 0.4f,
                    length = 0.42f, lengthAtEnd = 0.6f, lengthVariance = 0.25f,
                    radius = 0.55f, gravity = 0.25f, crookedness = 0.2f, segments = 4,
                },
            },
            leaves = new LeafSettings
            {
                count = 9, start = 0.2f, size = 0.6f, sizeVariance = 0.25f, angle = 50f,
                flatten = 0.3f, outward = 0.5f, canopyNormals = 0.65f,
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
            roots = new RootSettings { count = 5, length = 7f }, // spruce roots are shallow and spread wide
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
                // Twigs spread mostly flat to either side of each limb and hang down, carrying the sprays.
                new BranchLevelSettings
                {
                    count = 9, start = 0.15f, end = 0.95f,
                    angle = 60f, angleVariance = 12f, flatten = 0.7f,
                    length = 0.5f, lengthAtEnd = 0.55f, lengthVariance = 0.25f,
                    radius = 0.6f, gravity = 0.4f, crookedness = 0.15f, segments = 3,
                },
            },
            // Many small tips laid flat along each twig, fishbone style. Cards that face the camera read as broad
            // leaves at a distance, so they stay flat and the layers give the conifer look.
            leaves = new LeafSettings
            {
                count = 14, start = 0f, size = 0.36f, sizeVariance = 0.25f, angle = 55f,
                flatten = 0.9f, outward = 0f, canopyNormals = 0.85f,
            },
            mesh = new MeshSettings { radialSegments = 8, minRadialSegments = 3 },
        };

        /// <summary>Rebuilds one preset, e.g. "Beech", leaving the others' files untouched.</summary>
        public static void Build(string name) => Build(All.Single(s => s.Name == name));

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
            var bark = TreeMaterial($"{dir}/{species.Name}Bark.mat", barkTexture, false, barkNormal);
            var leaves = TreeMaterial($"{dir}/{species.Name}Leaves.mat", atlas.texture, true, tint: species.LeafTint);

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
                var texture = TextureImport.Load(source, BarkSize); // larger sources only bloat the package: it's imported at this size
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

        const int BarkSize = 1024;

        static void ImportTiling(string path, bool normalMap)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (normalMap)
                importer.textureType = TextureImporterType.NormalMap;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.maxTextureSize = BarkSize;
            importer.SaveAndReimport();
        }

        static IEnumerable<string> SourceFiles() =>
            Directory.Exists(RawTextures) ? Directory.GetFiles(RawTextures, "*.png") : Enumerable.Empty<string>();

        const string TreeShader = "Packages/com.treewizard.core/Shaders/Tree.shadergraph";

        /// <summary>A TreeWizard/Tree material (URP Lit plus wind). Leaves are alpha-clipped and flutter.</summary>
        static Material TreeMaterial(string path, Texture2D texture, bool leaves, Texture2D normalMap = null, Color? tint = null)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(TreeShader);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.SetColor("_BaseColor", texture != null ? tint ?? Color.white : new Color(0.36f, 0.27f, 0.2f));
            material.SetTexture("_BaseMap", texture);
            material.SetTexture("_BumpMap", normalMap);
            material.SetFloat("_BumpScale", 1f);
            material.SetFloat("_Smoothness", 0.15f);
            material.SetFloat("_WindStrength", 1f);
            material.SetFloat("_WindFlutter", leaves ? 1f : 0f);
            material.SetFloat("_AlphaClip", leaves ? 1f : 0f);
            material.SetFloat("_Cutoff", 0.5f);
            if (leaves)
            {
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = (int)RenderQueue.AlphaTest;
            }
            else
            {
                material.DisableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType", "Opaque");
                material.renderQueue = (int)RenderQueue.Geometry;
            }
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
