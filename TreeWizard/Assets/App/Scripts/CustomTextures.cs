using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TreeWizard.App
{
    /// <summary>
    /// The person's own bark and leaf images, used in place of the preset's. Keeps the source paths, so
    /// designs can store them, and the textures and materials built from them, which follow the tree
    /// across preset changes until <see cref="Clear"/>.
    /// </summary>
    public sealed class CustomTextures : IDisposable
    {
        readonly TreeWizardTree _tree;
        TreePreset _preset;
        Texture2D _bark, _barkNormal;
        LeafAtlas _leaves;
        Material _barkMaterial, _leafMaterial;
        int _barkVersion, _leafVersion; // a newer request makes a slower, older one drop its result

        public CustomTextures(TreeWizardTree tree) => _tree = tree;

        public string BarkPath { get; private set; } = "";

        /// <summary>Empty when the normal map is generated from the bark.</summary>
        public string BarkNormalPath { get; private set; } = "";

        public IReadOnlyList<string> LeafSheetPaths { get; private set; } = Array.Empty<string>();

        public bool HasBark => _bark != null;
        public bool HasLeaves => _leaves != null;

        public event Action Changed;

        public TreeDesignFile.TextureFiles Files => new TreeDesignFile.TextureFiles
        {
            bark = BarkPath,
            barkNormal = BarkNormalPath,
            leafSheets = LeafSheetPaths.ToArray(),
        };

        /// <summary>Call after the tree takes on a preset, to put the custom textures back over it.</summary>
        public void Apply(TreePreset preset)
        {
            _preset = preset;
            RebuildBarkMaterial();
            RebuildLeafMaterial();
            Refresh();
        }

        /// <summary>
        /// Uses <paramref name="path"/> as the bark. <paramref name="normalPath"/> is an existing normal map,
        /// or empty to generate one from the bark the way the presets' were made.
        /// </summary>
        public async Task SetBark(string path, string normalPath = "")
        {
            var version = ++_barkVersion;
            var bark = TextureImport.Load(path);
            Texture2D normal;
            try
            {
                if (!string.IsNullOrEmpty(normalPath))
                {
                    normal = TextureImport.LoadNormalMap(normalPath);
                }
                else
                {
                    var pixels = bark.GetPixels32();
                    int width = bark.width, height = bark.height;
                    var normals = await Task.Run(() => NormalMapGenerator.FromAlbedo(pixels, width, height));
                    normal = TextureImport.NormalMap(normals, width, height, bark.name + "Normal");
                }
            }
            catch
            {
                Object.Destroy(bark);
                throw;
            }

            if (version != _barkVersion || _tree == null)
            {
                Object.Destroy(bark);
                Object.Destroy(normal);
                return;
            }
            DestroyBark();
            _bark = bark;
            _barkNormal = normal;
            BarkPath = path;
            BarkNormalPath = normalPath ?? "";
            RebuildBarkMaterial();
            Refresh();
            Changed?.Invoke();
        }

        /// <summary>
        /// Slices leaf sheets into an atlas. Returns how many leaves were found and the names of any sheets
        /// with no transparent background, which each became a single leaf.
        /// </summary>
        public async Task<(int leaves, string[] opaqueSheets)> SetLeaves(IReadOnlyList<string> paths)
        {
            var version = ++_leafVersion;
            var sheets = paths.Select(p => TextureImport.LoadSheet(p)).ToList();
            var opaque = paths.Where((p, i) => !sheets[i].pixels.Any(px => px.a < 128)).Select(Path.GetFileName).ToArray();
            var sliced = await Task.Run(() => LeafSheetSlicer.Build(sheets));
            if (version != _leafVersion || _tree == null)
                return (0, opaque);

            if (sliced.cells.Count == 0)
                throw new InvalidDataException("No leaves were found on those sheets.");
            DestroyLeaves();
            _leaves = TextureImport.LeafAtlas(sliced, Path.GetFileNameWithoutExtension(paths[0]));
            LeafSheetPaths = paths.ToArray();
            RebuildLeafMaterial();
            Refresh();
            Changed?.Invoke();
            return (sliced.cells.Count, opaque);
        }

        /// <summary>Back to the preset's textures.</summary>
        public void Clear()
        {
            ResetBark();
            ResetLeaves();
            Refresh();
            Changed?.Invoke();
        }

        void ResetBark()
        {
            ++_barkVersion;
            DestroyBark();
            BarkPath = BarkNormalPath = "";
            RebuildBarkMaterial();
        }

        void ResetLeaves()
        {
            ++_leafVersion;
            DestroyLeaves();
            LeafSheetPaths = Array.Empty<string>();
            RebuildLeafMaterial();
        }

        public void Dispose()
        {
            DestroyBark();
            DestroyLeaves();
        }

        // Rebuilt straight away rather than marked dirty, so the renderer never draws with a material or texture
        // that has just been destroyed.
        void Refresh()
        {
            if (_tree != null && _preset != null)
                _tree.Rebuild();
        }

        // Copies of the preset's materials, so wind, smoothness and alpha clipping stay as the preset set them.
        void RebuildBarkMaterial()
        {
            Object.Destroy(_barkMaterial);
            _barkMaterial = null;
            if (_preset == null)
                return;
            if (_bark == null)
            {
                _tree.barkMaterial = _preset.barkMaterial;
            }
            else
            {
                _barkMaterial = new Material(_preset.barkMaterial) { name = _bark.name };
                _barkMaterial.SetColor("_BaseColor", Color.white);
                _barkMaterial.SetTexture("_BaseMap", _bark);
                _barkMaterial.SetTexture("_BumpMap", _barkNormal);
                _tree.barkMaterial = _barkMaterial;
            }
        }

        void RebuildLeafMaterial()
        {
            Object.Destroy(_leafMaterial);
            _leafMaterial = null;
            if (_preset == null)
                return;
            if (_leaves == null)
            {
                _tree.leafMaterial = _preset.leafMaterial;
                _tree.leafAtlas = _preset.leafAtlas;
            }
            else
            {
                _leafMaterial = new Material(_preset.leafMaterial) { name = _leaves.name };
                _leafMaterial.SetColor("_BaseColor", Color.white); // the preset's tint was chosen for its own leaves
                _leafMaterial.SetTexture("_BaseMap", _leaves.texture);
                _tree.leafMaterial = _leafMaterial;
                _tree.leafAtlas = _leaves;
            }
        }

        void DestroyBark()
        {
            Object.Destroy(_bark);
            Object.Destroy(_barkNormal);
            _bark = _barkNormal = null;
        }

        void DestroyLeaves()
        {
            if (_leaves != null)
                Object.Destroy(_leaves.texture);
            Object.Destroy(_leaves);
            _leaves = null;
        }
    }
}
