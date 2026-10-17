using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TreeWizard.Editor.Tests
{
    public class LeafSheetSlicerTests
    {
        static readonly Color32 Green = new Color32(60, 140, 40, 252);
        static readonly Color32 Red = new Color32(190, 60, 30, 252);
        static readonly Color32 Clear = new Color32(0, 255, 0, 1); // background residue, like real sheets

        const int W = 200, H = 120;

        // Two leaves with stems, a fragment just beside the green one, and a stray speck.
        static LeafSheetSlicer.Sheet MakeSheet()
        {
            var px = Enumerable.Repeat(Clear, W * H).ToArray();
            void Rect(int x0, int y0, int w, int h, Color32 c)
            {
                for (var y = y0; y < y0 + h; y++)
                for (var x = x0; x < x0 + w; x++)
                    px[y * W + x] = c;
            }
            Rect(10, 30, 30, 60, Green); // green leaf
            Rect(24, 10, 3, 20, Green);  // its stem, centre x = 25
            Rect(44, 60, 4, 4, Green);   // fragment 4px away: joins the green leaf
            Rect(120, 30, 40, 70, Red);  // red leaf
            Rect(139, 15, 3, 15, Red);   // its stem, centre x = 140
            Rect(80, 110, 2, 2, Green);  // speck: dropped
            return new LeafSheetSlicer.Sheet { pixels = px, width = W, height = H };
        }

        [Test]
        public void FindsEachLeaf_AndDropsSpecks()
        {
            var atlas = LeafSheetSlicer.Build(new[] { MakeSheet() });

            Assert.AreEqual(2, atlas.cells.Count);
            Assert.AreEqual(1, atlas.cells.Count(c => c.autumn), "red leaf is autumn");
            Assert.AreEqual(1, atlas.cells.Count(c => !c.autumn), "green leaf is summer");
        }

        [Test]
        public void Pivot_IsAtTheStem()
        {
            var atlas = LeafSheetSlicer.Build(new[] { MakeSheet() });
            var green = atlas.cells.Single(c => !c.autumn);

            // Green bounds start at x 10; the stem's centre is at 25.5 in pixel space. The pivot sits
            // above the bottom padding, and the same padding is on the left.
            var widthPx = green.uvRect.width * atlas.size;
            var heightPx = green.uvRect.height * atlas.size;
            var padding = green.pivot.y * heightPx;
            Assert.Greater(padding, 0f);
            Assert.AreEqual(25.5f - 10f + padding, green.pivot.x * widthPx, 1f);
            Assert.AreEqual(widthPx / heightPx, green.aspect, 1e-3f);
        }

        [Test]
        public void Cells_StayInsideTheAtlas_AndDontOverlap()
        {
            var atlas = LeafSheetSlicer.Build(new[] { MakeSheet(), MakeSheet(), MakeSheet() });

            Assert.AreEqual(6, atlas.cells.Count);
            foreach (var c in atlas.cells)
            {
                Assert.GreaterOrEqual(c.uvRect.xMin, 0f);
                Assert.GreaterOrEqual(c.uvRect.yMin, 0f);
                Assert.LessOrEqual(c.uvRect.xMax, 1f);
                Assert.LessOrEqual(c.uvRect.yMax, 1f);
            }
            for (var i = 0; i < atlas.cells.Count; i++)
            for (var j = i + 1; j < atlas.cells.Count; j++)
                Assert.IsFalse(atlas.cells[i].uvRect.Overlaps(atlas.cells[j].uvRect), $"cells {i} and {j} overlap");
        }

        [Test]
        public void ShrinksToFit_WhenOverMaxSize()
        {
            var atlas = LeafSheetSlicer.Build(Enumerable.Repeat(MakeSheet(), 8).ToArray(), maxSize: 128);

            Assert.AreEqual(128, atlas.size);
            Assert.AreEqual(16, atlas.cells.Count);
        }

        [Test]
        public void Bleed_ReplacesBackgroundColour()
        {
            var atlas = LeafSheetSlicer.Build(new[] { MakeSheet() });
            var green = atlas.cells.Single(c => !c.autumn);

            // A see-through pixel just inside the cell's padding should carry leaf colour, not the green-screen residue.
            var x = Mathf.FloorToInt(green.uvRect.xMin * atlas.size) + 2;
            var y = Mathf.FloorToInt(green.uvRect.center.y * atlas.size);
            var p = atlas.pixels[y * atlas.size + x];
            Assert.Less(p.a, 128);
            Assert.AreEqual(Green.g, p.g, 2);
            Assert.AreEqual(Green.r, p.r, 2);
        }
    }
}
