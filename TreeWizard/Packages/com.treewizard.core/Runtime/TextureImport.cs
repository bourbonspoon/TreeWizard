using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TreeWizard
{
    /// <summary>
    /// Turns image files into tree textures at runtime, for apps where people bring their own bark and
    /// leaves. The slow parts (<see cref="LeafSheetSlicer.Build"/>, <see cref="NormalMapGenerator.FromAlbedo"/>)
    /// work on plain pixel arrays, so they can run on a worker thread between these calls.
    /// Textures and atlases returned here belong to the caller, who destroys them.
    /// </summary>
    public static class TextureImport
    {
        /// <summary>PNG and JPG, the formats <see cref="ImageConversion.LoadImage(Texture2D, byte[])"/> reads.</summary>
        public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg" };

        /// <summary>
        /// Reads a PNG or JPG into a readable, mipmapped, tiling texture, scaled down to fit
        /// <paramref name="maxSize"/>. Throws <see cref="IOException"/> if the file isn't an image.
        /// </summary>
        public static Texture2D Load(string path, int maxSize = 2048)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = Path.GetFileNameWithoutExtension(path) };
            if (!texture.LoadImage(File.ReadAllBytes(path)))
            {
                Object.DestroyImmediate(texture);
                throw new IOException($"{Path.GetFileName(path)} isn't a PNG or JPG image.");
            }
            if (Mathf.Max(texture.width, texture.height) > maxSize)
            {
                var scaled = Resize(texture, maxSize);
                Object.DestroyImmediate(texture);
                texture = scaled;
            }
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.anisoLevel = 4;
            return texture;
        }

        /// <summary>Reads a leaf sheet's pixels for <see cref="LeafSheetSlicer.Build"/>.</summary>
        public static LeafSheetSlicer.Sheet LoadSheet(string path, int maxSize = 4096)
        {
            var texture = Load(path, maxSize);
            try
            {
                return new LeafSheetSlicer.Sheet { pixels = texture.GetPixels32(), width = texture.width, height = texture.height };
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>A normal map texture from pixels such as <see cref="NormalMapGenerator.FromAlbedo"/> returns.</summary>
        public static Texture2D NormalMap(Color32[] pixels, int width, int height, string name = "Normal")
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true, linear: true)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                anisoLevel = 4,
            };
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }

        /// <summary>
        /// Reads a normal map file. It must use Unity's convention (OpenGL, +Y up); its colours are taken as
        /// data, not sRGB.
        /// </summary>
        public static Texture2D LoadNormalMap(string path, int maxSize = 2048)
        {
            var source = Load(path, maxSize);
            try
            {
                return NormalMap(source.GetPixels32(), source.width, source.height, source.name);
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        /// <summary>
        /// A <see cref="LeafAtlas"/> with its texture, from <see cref="LeafSheetSlicer.Build"/>'s result. The
        /// mipmaps keep the share of solid pixels the same at every level, so alpha-clipped leaves don't
        /// thin out with distance. Destroy both the atlas and its texture when done.
        /// </summary>
        public static LeafAtlas LeafAtlas(LeafSheetSlicer.Atlas atlas, string name = "Leaves", float cutoff = 0.5f)
        {
            var texture = new Texture2D(atlas.size, atlas.size, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 4,
            };
            var levels = MipsPreservingCoverage(atlas.pixels, atlas.size, atlas.size, cutoff);
            for (var mip = 0; mip < texture.mipmapCount && mip < levels.Count; mip++)
                texture.SetPixels32(levels[mip], mip);
            texture.Apply(false);

            var result = ScriptableObject.CreateInstance<LeafAtlas>();
            result.name = name;
            result.texture = texture;
            result.cells = new List<LeafCell>(atlas.cells);
            return result;
        }

        /// <summary>
        /// The full mip chain, down to 1x1, each level a 2x2 box filter of the one above. Each level's alpha
        /// is then scaled so the same share of pixels passes <paramref name="cutoff"/> as in the top level.
        /// </summary>
        static List<Color32[]> MipsPreservingCoverage(Color32[] pixels, int width, int height, float cutoff)
        {
            var levels = new List<Color32[]> { pixels };
            var target = Coverage(pixels, cutoff, 1f);
            var current = pixels;
            int w = width, h = height;
            while (w > 1 || h > 1)
            {
                int nw = Mathf.Max(1, w / 2), nh = Mathf.Max(1, h / 2);
                current = Downsample(current, w, h, nw, nh); // filtered from the unscaled level, so scaling doesn't compound
                w = nw;
                h = nh;
                levels.Add(ScaleAlpha(current, AlphaScaleFor(current, cutoff, target)));
            }
            return levels;
        }

        static Color32[] Downsample(Color32[] src, int w, int h, int nw, int nh)
        {
            var dst = new Color32[nw * nh];
            for (var y = 0; y < nh; y++)
            for (var x = 0; x < nw; x++)
            {
                int r = 0, g = 0, b = 0, a = 0, n = 0;
                for (var sy = y * 2; sy < Mathf.Min(y * 2 + 2, h); sy++)
                for (var sx = x * 2; sx < Mathf.Min(x * 2 + 2, w); sx++)
                {
                    var p = src[sy * w + sx];
                    r += p.r; g += p.g; b += p.b; a += p.a;
                    n++;
                }
                dst[y * nw + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
            }
            return dst;
        }

        static float Coverage(Color32[] px, float cutoff, float scale)
        {
            var threshold = cutoff * 255f / scale;
            var solid = 0;
            foreach (var p in px)
            {
                if (p.a >= threshold)
                    solid++;
            }
            return (float)solid / px.Length;
        }

        static float AlphaScaleFor(Color32[] px, float cutoff, float target)
        {
            float low = 0f, high = 4f;
            for (var i = 0; i < 12; i++)
            {
                var mid = (low + high) * 0.5f;
                if (Coverage(px, cutoff, mid) < target)
                    low = mid;
                else
                    high = mid;
            }
            // Small levels have few distinct alphas, so coverage jumps; take whichever side lands nearer.
            return Mathf.Abs(Coverage(px, cutoff, low) - target) < Mathf.Abs(Coverage(px, cutoff, high) - target) ? low : high;
        }

        static Color32[] ScaleAlpha(Color32[] px, float scale)
        {
            var scaled = (Color32[])px.Clone();
            for (var i = 0; i < scaled.Length; i++)
                scaled[i].a = (byte)Mathf.Min(255, Mathf.RoundToInt(scaled[i].a * scale));
            return scaled;
        }

        static Texture2D Resize(Texture2D source, int maxSize)
        {
            var scale = (float)maxSize / Mathf.Max(source.width, source.height);
            int w = Mathf.Max(1, Mathf.RoundToInt(source.width * scale)), h = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
            var target = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                var result = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = source.name };
                result.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                result.Apply(true);
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
