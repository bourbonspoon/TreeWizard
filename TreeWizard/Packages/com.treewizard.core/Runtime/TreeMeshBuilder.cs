using System.Collections.Generic;
using UnityEngine;

namespace TreeWizard
{
    /// <summary>
    /// Turns a <see cref="TreeSkeleton"/> into a <see cref="MeshData"/>: a tube per branch on the bark
    /// submesh and a double-sided card per leaf on the leaf submesh.
    /// </summary>
    public static class TreeMeshBuilder
    {
        /// <param name="cells">The cells the skeleton's leaves index into, or null for the whole texture.</param>
        public static MeshData Build(TreeSkeleton skeleton, TreeDefinition def, IReadOnlyList<LeafCell> cells = null)
        {
            var data = new MeshData();
            foreach (var branch in skeleton.branches)
                AddBranch(data, branch, def.mesh, def.trunk.radius);
            foreach (var leaf in skeleton.leaves)
            {
                var cell = cells != null && leaf.cell >= 0 && leaf.cell < cells.Count ? cells[leaf.cell] : LeafCell.FullTexture;
                AddLeaf(data, leaf, cell, def.leaves.shape);
            }
            return data;
        }

        static void AddBranch(MeshData data, Branch branch, MeshSettings settings, float trunkRadius)
        {
            var radial = branch.level == 0
                ? settings.radialSegments
                : Mathf.Clamp(Mathf.CeilToInt(settings.radialSegments * branch.radius / trunkRadius),
                    Mathf.Min(settings.minRadialSegments, settings.radialSegments), settings.radialSegments);
            var ring = radial + 1; // the seam vertex is duplicated so U can run 0..1
            var circumference = 2f * Mathf.PI * branch.radius;
            var tris = data.Triangles(MeshData.BarkSubmesh);
            var first = data.vertices.Count;

            foreach (var node in branch.nodes)
            {
                var binormal = Vector3.Cross(node.tangent, node.normal);
                var v = node.distance / circumference; // keeps bark texels roughly square
                for (var j = 0; j <= radial; j++)
                {
                    var a = (float)j / radial * 2f * Mathf.PI;
                    var outward = Mathf.Cos(a) * node.normal + Mathf.Sin(a) * binormal;
                    data.AddVertex(node.position + outward * node.radius, outward, new Vector2((float)j / radial, v));
                }
            }

            for (var i = 0; i < branch.nodes.Count - 1; i++)
            {
                for (var j = 0; j < radial; j++)
                {
                    var a = first + i * ring + j;
                    var b = a + ring;
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
                }
            }

            // Close the tip with a short cone.
            var tip = branch.nodes[branch.nodes.Count - 1];
            var lastRing = first + (branch.nodes.Count - 1) * ring;
            var apex = data.AddVertex(tip.position + tip.tangent * tip.radius, tip.tangent,
                new Vector2(0.5f, (tip.distance + tip.radius) / circumference));
            for (var j = 0; j < radial; j++)
            {
                tris.Add(lastRing + j); tris.Add(lastRing + j + 1); tris.Add(apex);
            }
        }

        static void AddLeaf(MeshData data, in Leaf leaf, in LeafCell cell, LeafShape shape)
        {
            AddCard(data, leaf.position, leaf.up, leaf.width, leaf.normal, leaf.size, cell);
            if (shape == LeafShape.Cross)
                AddCard(data, leaf.position, leaf.up, leaf.normal, -leaf.width, leaf.size, cell);
        }

        // Double-sided: the back is separate geometry rather than a two-sided material, so it
        // survives export to tools that ignore material culling settings.
        static void AddCard(MeshData data, Vector3 position, Vector3 up, Vector3 width, Vector3 normal, float size, in LeafCell cell)
        {
            // The cell's pivot (the stem) lands on the branch.
            var across = width * (size * cell.aspect);
            var height = up * size;
            var v0 = position - across * cell.pivot.x - height * cell.pivot.y;
            var v1 = v0 + height;
            var v2 = v0 + across + height;
            var v3 = v0 + across;
            var uv = cell.uvRect;
            var tris = data.Triangles(MeshData.LeafSubmesh);

            foreach (var side in new[] { 1f, -1f })
            {
                var n = normal * side;
                var i0 = data.AddVertex(v0, n, new Vector2(uv.xMin, uv.yMin));
                data.AddVertex(v1, n, new Vector2(uv.xMin, uv.yMax));
                data.AddVertex(v2, n, new Vector2(uv.xMax, uv.yMax));
                data.AddVertex(v3, n, new Vector2(uv.xMax, uv.yMin));
                if (side > 0f)
                {
                    tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
                    tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3);
                }
                else
                {
                    tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 1);
                    tris.Add(i0); tris.Add(i0 + 3); tris.Add(i0 + 2);
                }
            }
        }
    }
}
