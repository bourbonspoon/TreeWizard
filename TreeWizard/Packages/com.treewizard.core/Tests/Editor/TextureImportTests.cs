using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TreeWizard.Editor.Tests
{
    public class TextureImportTests
    {
        string _folder;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "TreeWizardTextureImportTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_folder, true);

        // Sparse foliage: 30% of pixels solid, scattered, which a plain box filter averages to about 30% alpha,
        // below the cutoff, so distant leaves would vanish.
        static LeafSheetSlicer.Atlas SparseLeaves(int size)
        {
            var random = new System.Random(1);
            var atlas = new LeafSheetSlicer.Atlas { size = size, pixels = new Color32[size * size] };
            for (var i = 0; i < atlas.pixels.Length; i++)
                atlas.pixels[i] = new Color32(60, 140, 40, (byte)(random.NextDouble() < 0.3 ? 255 : 0));
            atlas.cells.Add(LeafCell.FullTexture);
            return atlas;
        }

        static float Coverage(Color32[] pixels) => pixels.Count(p => p.a >= 128) / (float)pixels.Length;

        [Test]
        public void LeafAtlas_MipmapsKeepTheShareOfSolidPixels()
        {
            var atlas = TextureImport.LeafAtlas(SparseLeaves(256));
            try
            {
                var top = Coverage(atlas.texture.GetPixels32(0));
                Assert.AreEqual(0.3f, top, 0.03f);
                for (var mip = 1; mip < 5; mip++)
                    Assert.AreEqual(top, Coverage(atlas.texture.GetPixels32(mip)), 0.06f, $"mip {mip}");
                Assert.AreEqual(1, atlas.cells.Count);
                Assert.AreEqual(TextureWrapMode.Clamp, atlas.texture.wrapMode);
            }
            finally
            {
                Object.DestroyImmediate(atlas.texture);
                Object.DestroyImmediate(atlas);
            }
        }

        [Test]
        public void Load_ReadsAPng_AndScalesItDownToFit()
        {
            var path = Path.Combine(_folder, "Bark.png");
            var source = new Texture2D(64, 32, TextureFormat.RGBA32, false);
            source.SetPixels32(Enumerable.Repeat(new Color32(120, 90, 60, 255), 64 * 32).ToArray());
            File.WriteAllBytes(path, source.EncodeToPNG());
            Object.DestroyImmediate(source);

            var full = TextureImport.Load(path);
            var small = TextureImport.Load(path, 16);
            try
            {
                Assert.AreEqual((64, 32), (full.width, full.height));
                Assert.AreEqual((16, 8), (small.width, small.height));
                Assert.AreEqual("Bark", full.name);
                Assert.AreEqual(TextureWrapMode.Repeat, full.wrapMode);
                Assert.IsTrue(full.isReadable);
                Assert.AreEqual(new Color32(120, 90, 60, 255), full.GetPixels32()[0]);
            }
            finally
            {
                Object.DestroyImmediate(full);
                Object.DestroyImmediate(small);
            }
        }

        [Test]
        public void Load_RejectsFilesThatArentImages()
        {
            var path = Path.Combine(_folder, "notes.png");
            File.WriteAllText(path, "not an image");

            Assert.Throws<IOException>(() => TextureImport.Load(path));
        }

        [Test]
        public void NormalMap_IsLinear_SoItsValuesArentTreatedAsColours()
        {
            var flat = Enumerable.Repeat(new Color32(128, 128, 255, 255), 16).ToArray();
            var map = TextureImport.NormalMap(flat, 4, 4);
            try
            {
                Assert.IsFalse(map.isDataSRGB);
                Assert.AreEqual(flat[0], map.GetPixels32()[0]);
            }
            finally
            {
                Object.DestroyImmediate(map);
            }
        }
    }
}
