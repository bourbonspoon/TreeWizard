using System.Collections.Generic;
using UnityEngine;

namespace TreeWizard
{
    /// <summary>
    /// Finds each leaf or twig on transparent-background sheets and packs them all into one atlas.
    /// Sheets must have stems pointing down: the lowest point of each cutout becomes its pivot.
    /// Pixel arrays are bottom row first, as <see cref="Texture2D.GetPixels32()"/> returns them.
    /// </summary>
    public static class LeafSheetSlicer
    {
        public struct Sheet
        {
            public Color32[] pixels;
            public int width;
            public int height;
        }

        public class Atlas
        {
            public Color32[] pixels;
            public int size;
            public readonly List<LeafCell> cells = new List<LeafCell>();
        }

        class Cutout
        {
            public Color32[] pixels;
            public int width;
            public int height;
            public Vector2 pivot;
            public bool autumn;
        }

        const byte Solid = 128;
        const int Padding = 8;               // source pixels kept around each cutout for filtering
        const float MinPieceFraction = 0.05f; // pieces smaller than this share of the largest are fragments
        const int StemRows = 3;

        public static Atlas Build(IReadOnlyList<Sheet> sheets, int maxSize = 2048)
        {
            var cutouts = new List<Cutout>();
            foreach (var sheet in sheets)
                cutouts.AddRange(FindCutouts(sheet));
            return Pack(cutouts, maxSize);
        }

        static List<Cutout> FindCutouts(Sheet sheet)
        {
            int w = sheet.width, h = sheet.height;
            var px = sheet.pixels;

            // Label 8-connected solid pieces.
            var labels = new int[px.Length];
            var bounds = new List<RectInt> { default }; // index = label; 0 is background
            var areas = new List<int> { 0 };
            var stack = new Stack<int>();
            for (var start = 0; start < px.Length; start++)
            {
                if (labels[start] != 0 || px[start].a < Solid)
                    continue;

                var id = bounds.Count;
                int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, area = 0;
                labels[start] = id;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var i = stack.Pop();
                    int x = i % w, y = i / w;
                    area++;
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                    for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                            continue;
                        var j = ny * w + nx;
                        if (labels[j] == 0 && px[j].a >= Solid)
                        {
                            labels[j] = id;
                            stack.Push(j);
                        }
                    }
                }
                bounds.Add(new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1));
                areas.Add(area);
            }

            // Big pieces are cutouts. Fragments (a detached leaf tip, a gap in a stem) join a big
            // piece they nearly touch; anything else is a stray speck and is dropped.
            var largest = 0;
            foreach (var a in areas)
                largest = Mathf.Max(largest, a);
            var owner = new int[bounds.Count];
            var groups = new List<int>();
            for (var id = 1; id < bounds.Count; id++)
            {
                if (areas[id] >= largest * MinPieceFraction)
                {
                    owner[id] = id;
                    groups.Add(id);
                }
            }
            var groupBounds = new Dictionary<int, RectInt>();
            foreach (var g in groups)
                groupBounds[g] = bounds[g];
            for (var id = 1; id < bounds.Count; id++)
            {
                if (owner[id] != 0)
                    continue;
                int best = 0, bestGap = int.MaxValue;
                foreach (var g in groups)
                {
                    var gap = Gap(bounds[id], bounds[g]);
                    if (gap < bestGap) { best = g; bestGap = gap; }
                }
                if (best != 0 && bestGap <= Padding)
                {
                    owner[id] = best;
                    groupBounds[best] = Union(groupBounds[best], bounds[id]);
                }
            }

            var cutouts = new List<Cutout>();
            foreach (var g in groups)
            {
                var r = groupBounds[g];
                int cw = r.width + 2 * Padding, ch = r.height + 2 * Padding;
                var crop = new Color32[cw * ch];
                double sumR = 0, sumG = 0, sumB = 0, stemX = 0;
                int solid = 0, stemCount = 0;
                for (var y = r.yMin; y < r.yMax; y++)
                for (var x = r.xMin; x < r.xMax; x++)
                {
                    var i = y * w + x;
                    if (labels[i] != 0 && owner[labels[i]] != g)
                        continue; // another cutout, or a dropped speck, inside our bounds
                    var c = px[i];
                    if (c.a < 16)
                        c.a = 0; // near-invisible background residue
                    crop[(y - r.yMin + Padding) * cw + (x - r.xMin + Padding)] = c;
                    if (c.a < Solid)
                        continue;
                    solid++;
                    sumR += c.r; sumG += c.g; sumB += c.b;
                    if (y < r.yMin + StemRows) { stemX += x; stemCount++; }
                }

                Bleed(crop, cw, ch);
                Color.RGBToHSV(new Color((float)(sumR / solid / 255), (float)(sumG / solid / 255), (float)(sumB / solid / 255)),
                    out var hue, out var saturation, out _);
                cutouts.Add(new Cutout
                {
                    pixels = crop,
                    width = cw,
                    height = ch,
                    pivot = new Vector2(((float)(stemX / stemCount) - r.xMin + Padding + 0.5f) / cw, (float)Padding / ch),
                    // Greens through blue-greens (spruce) are summer; reds, oranges, yellows and browns are autumn.
                    autumn = !(hue >= 55f / 360f && hue <= 200f / 360f && saturation > 0.12f),
                });
            }
            return cutouts;
        }

        static int Gap(RectInt a, RectInt b)
        {
            var gx = Mathf.Max(0, Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax));
            var gy = Mathf.Max(0, Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax));
            return Mathf.Max(gx, gy);
        }

        static RectInt Union(RectInt a, RectInt b)
        {
            int xMin = Mathf.Min(a.xMin, b.xMin), yMin = Mathf.Min(a.yMin, b.yMin);
            return new RectInt(xMin, yMin, Mathf.Max(a.xMax, b.xMax) - xMin, Mathf.Max(a.yMax, b.yMax) - yMin);
        }

        /// <summary>
        /// Gives see-through pixels, and the 1px solid rim (which carries leftover background colour),
        /// the colour of the nearest solid interior pixel, so filtering and mipmaps don't pull in a halo.
        /// Alpha is left alone.
        /// </summary>
        static void Bleed(Color32[] px, int w, int h)
        {
            var done = new bool[px.Length];
            var queue = new Queue<int>();
            for (var y = 1; y < h - 1; y++)
            for (var x = 1; x < w - 1; x++)
            {
                var i = y * w + x;
                if (px[i].a >= Solid && px[i - 1].a >= Solid && px[i + 1].a >= Solid && px[i - w].a >= Solid && px[i + w].a >= Solid)
                {
                    done[i] = true;
                    queue.Enqueue(i);
                }
            }

            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                int x = i % w, y = i / w;
                var c = px[i];
                if (x > 0) Spread(i - 1);
                if (x < w - 1) Spread(i + 1);
                if (y > 0) Spread(i - w);
                if (y < h - 1) Spread(i + w);

                void Spread(int j)
                {
                    if (done[j])
                        return;
                    done[j] = true;
                    px[j] = new Color32(c.r, c.g, c.b, px[j].a);
                    queue.Enqueue(j);
                }
            }
        }

        static Atlas Pack(List<Cutout> cutouts, int maxSize)
        {
            if (cutouts.Count == 0)
                return new Atlas { size = 4, pixels = new Color32[16] };

            long area = 0;
            foreach (var c in cutouts)
                area += (long)c.width * c.height;

            // Smallest power-of-two square that holds everything at full resolution,
            // otherwise maxSize with every cutout shrunk by the same amount.
            RectInt[] placed = null;
            var size = 64;
            var scale = 1f;
            for (; size < maxSize; size *= 2)
            {
                if ((long)size * size >= area && TryPlace(cutouts, size, 1f, out placed))
                    break;
            }
            if (placed == null)
            {
                size = maxSize;
                scale = Mathf.Min(1f, Mathf.Sqrt(0.9f * size * size / area));
                while (!TryPlace(cutouts, size, scale, out placed))
                    scale *= 0.97f;
            }

            var atlas = new Atlas { size = size, pixels = new Color32[size * size] };
            Fill(atlas.pixels, AverageColour(cutouts));
            for (var i = 0; i < cutouts.Count; i++)
            {
                var c = cutouts[i];
                var r = placed[i];
                Blit(c, atlas.pixels, size, r);
                atlas.cells.Add(new LeafCell
                {
                    uvRect = new Rect((float)r.x / size, (float)r.y / size, (float)r.width / size, (float)r.height / size),
                    pivot = c.pivot,
                    aspect = (float)c.width / c.height,
                    autumn = c.autumn,
                    weight = 1f,
                });
            }
            return atlas;
        }

        /// <summary>Shelf packing, tallest first. Returns rects in the same order as <paramref name="cutouts"/>.</summary>
        static bool TryPlace(List<Cutout> cutouts, int size, float scale, out RectInt[] placed)
        {
            placed = new RectInt[cutouts.Count];
            var order = new List<int>();
            for (var i = 0; i < cutouts.Count; i++)
                order.Add(i);
            order.Sort((a, b) => cutouts[b].height.CompareTo(cutouts[a].height));

            int x = 0, y = 0, shelf = 0;
            foreach (var i in order)
            {
                var w = Mathf.Max(1, Mathf.RoundToInt(cutouts[i].width * scale));
                var h = Mathf.Max(1, Mathf.RoundToInt(cutouts[i].height * scale));
                if (x + w > size)
                {
                    x = 0;
                    y += shelf;
                    shelf = 0;
                }
                if (w > size || y + h > size)
                {
                    placed = null;
                    return false;
                }
                placed[i] = new RectInt(x, y, w, h);
                x += w;
                shelf = Mathf.Max(shelf, h);
            }
            return true;
        }

        /// <summary>Copies a cutout into the atlas, box-filtering when it has been shrunk.</summary>
        static void Blit(Cutout c, Color32[] dst, int dstSize, RectInt r)
        {
            float sx = (float)c.width / r.width, sy = (float)c.height / r.height;
            for (var y = 0; y < r.height; y++)
            {
                var y0 = Mathf.Min((int)(y * sy), c.height - 1);
                var y1 = Mathf.Clamp((int)((y + 1) * sy), y0 + 1, c.height);
                for (var x = 0; x < r.width; x++)
                {
                    var x0 = Mathf.Min((int)(x * sx), c.width - 1);
                    var x1 = Mathf.Clamp((int)((x + 1) * sx), x0 + 1, c.width);
                    int sr = 0, sg = 0, sb = 0, sa = 0, n = 0;
                    for (var yy = y0; yy < y1; yy++)
                    for (var xx = x0; xx < x1; xx++)
                    {
                        var p = c.pixels[yy * c.width + xx];
                        sr += p.r; sg += p.g; sb += p.b; sa += p.a;
                        n++;
                    }
                    dst[(r.y + y) * dstSize + r.x + x] = new Color32((byte)(sr / n), (byte)(sg / n), (byte)(sb / n), (byte)(sa / n));
                }
            }
        }

        static Color32 AverageColour(List<Cutout> cutouts)
        {
            long r = 0, g = 0, b = 0, n = 0;
            foreach (var c in cutouts)
            foreach (var p in c.pixels)
            {
                if (p.a < Solid)
                    continue;
                r += p.r; g += p.g; b += p.b;
                n++;
            }
            return n == 0 ? new Color32(0, 0, 0, 0) : new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 0);
        }

        static void Fill(Color32[] px, Color32 c)
        {
            for (var i = 0; i < px.Length; i++)
                px[i] = c;
        }
    }
}
