using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TreeWizard.Editor.Tests
{
    public class NormalMapGeneratorTests
    {
        const int Size = 64;

        static Color32[] Image(System.Func<int, int, float> brightness) =>
            Enumerable.Range(0, Size * Size).Select(i =>
            {
                var v = (byte)Mathf.RoundToInt(Mathf.Clamp01(brightness(i % Size, i / Size)) * 255f);
                return new Color32(v, v, v, 255);
            }).ToArray();

        // Four ridges across the tile, so it tiles seamlessly.
        static float Ridges(int x, int y) => 0.5f + 0.5f * Mathf.Sin(x * 2f * Mathf.PI * 4f / Size);

        [Test]
        public void FlatImage_GivesFlatNormals()
        {
            var normals = NormalMapGenerator.FromAlbedo(Image((x, y) => 0.4f), Size, Size);

            foreach (var n in normals)
            {
                Assert.AreEqual(128, n.r, 1);
                Assert.AreEqual(128, n.g, 1);
                Assert.AreEqual(255, n.b, 1);
            }
        }

        [Test]
        public void Slopes_TiltNormalsAwayFromTheHighSide()
        {
            var normals = NormalMapGenerator.FromAlbedo(Image(Ridges), Size, Size);

            // Ridges repeat every 16 px. At x = 0 brightness rises to the right, so the surface faces
            // left (red below the midpoint); at x = 8 it falls, so it faces right.
            Assert.Less(normals[0].r, 120);
            Assert.Greater(normals[8].r, 136);
            Assert.AreEqual(128, normals[0].g, 2, "no slope up the tile");
        }

        [Test]
        public void SeamlessInput_GivesSeamlessNormals()
        {
            var normals = NormalMapGenerator.FromAlbedo(Image(Ridges), Size, Size);

            for (var y = 0; y < Size; y++)
            {
                var first = normals[y * Size];
                var last = normals[y * Size + Size - 1];
                var next = normals[y * Size + 1];
                // The step across the wrap is no bigger than an ordinary step inside the tile.
                Assert.LessOrEqual(Mathf.Abs(first.r - last.r), Mathf.Abs(next.r - first.r) + 2);
            }
        }

        [Test]
        public void ZeroStrength_IsFlat()
        {
            var normals = NormalMapGenerator.FromAlbedo(Image(Ridges), Size, Size, 0f);

            Assert.IsTrue(normals.All(n => n.r == 128 && n.g == 128 && n.b == 255));
        }
    }
}
