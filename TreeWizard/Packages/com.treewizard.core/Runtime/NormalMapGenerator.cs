using UnityEngine;

namespace TreeWizard
{
    /// <summary>
    /// Builds tangent-space normal maps from colour textures, treating brightness as height, which
    /// suits bark (crevices are dark). Edges wrap, so a seamless tile gives a seamless normal map.
    /// </summary>
    public static class NormalMapGenerator
    {
        public const float DefaultStrength = 2.5f;

        /// <param name="pixels">Colour pixels, bottom row first, as <see cref="Texture2D.GetPixels32()"/> returns them.</param>
        /// <param name="strength">Relief depth. 0 is flat.</param>
        /// <returns>Normal map pixels in Unity's convention (+Y up), bottom row first.</returns>
        public static Color32[] FromAlbedo(Color32[] pixels, int width, int height, float strength = DefaultStrength)
        {
            var h = new float[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                h[i] = (0.2126f * p.r + 0.7152f * p.g + 0.0722f * p.b) / 255f;
            }

            // Keep surface detail only: broad shading in the photo would otherwise tilt whole areas.
            var broad = Blur(h, width, height, Mathf.Max(4, Mathf.Min(width, height) / 16));
            for (var i = 0; i < h.Length; i++)
                h[i] -= broad[i];
            h = Blur(h, width, height, 1); // soften pixel noise

            var result = new Color32[pixels.Length];
            for (var y = 0; y < height; y++)
            {
                int up = Wrap(y + 1, height) * width, down = Wrap(y - 1, height) * width, row = y * width;
                for (var x = 0; x < width; x++)
                {
                    int right = Wrap(x + 1, width), left = Wrap(x - 1, width);
                    var dx = h[row + right] - h[row + left];
                    var dy = h[up + x] - h[down + x];
                    var n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                    result[row + x] = new Color32(Encode(n.x), Encode(n.y), Encode(n.z), 255);
                }
            }
            return result;
        }

        static byte Encode(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt((v * 0.5f + 0.5f) * 255f), 0, 255);

        static int Wrap(int i, int n) => (i % n + n) % n;

        /// <summary>Three box blurs, which approximate a Gaussian. Wraps at the edges.</summary>
        static float[] Blur(float[] source, int width, int height, int radius)
        {
            var a = (float[])source.Clone();
            var b = new float[source.Length];
            for (var pass = 0; pass < 3; pass++)
            {
                BoxHorizontal(a, b, width, height, radius);
                BoxVertical(b, a, width, height, radius);
            }
            return a;
        }

        static void BoxHorizontal(float[] src, float[] dst, int width, int height, int r)
        {
            var scale = 1f / (2 * r + 1);
            for (var y = 0; y < height; y++)
            {
                var row = y * width;
                var sum = 0f;
                for (var k = -r; k <= r; k++)
                    sum += src[row + Wrap(k, width)];
                for (var x = 0; x < width; x++)
                {
                    dst[row + x] = sum * scale;
                    sum += src[row + Wrap(x + r + 1, width)] - src[row + Wrap(x - r, width)];
                }
            }
        }

        static void BoxVertical(float[] src, float[] dst, int width, int height, int r)
        {
            var scale = 1f / (2 * r + 1);
            for (var x = 0; x < width; x++)
            {
                var sum = 0f;
                for (var k = -r; k <= r; k++)
                    sum += src[Wrap(k, height) * width + x];
                for (var y = 0; y < height; y++)
                {
                    dst[y * width + x] = sum * scale;
                    sum += src[Wrap(y + r + 1, height) * width + x] - src[Wrap(y - r, height) * width + x];
                }
            }
        }
    }
}
