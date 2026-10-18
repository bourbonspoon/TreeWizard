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
        /// <summary>Root cross-sections are this tall for their radius, and wider by <see cref="RootSpread"/>: low, broad ridges.</summary>
        public const float RootSquash = 0.6f;
        public const float RootSpread = 1.25f;

        /// <param name="cells">The cells the skeleton's leaves index into, or null for the whole texture.</param>
        public static MeshData Build(TreeSkeleton skeleton, TreeDefinition def, IReadOnlyList<LeafCell> cells = null)
        {
            var data = new MeshData();
            foreach (var branch in skeleton.branches)
                AddBranch(data, branch, def.mesh, def.trunk.radius);
            var canopy = new Canopy(skeleton, def.leaves.canopyNormals);
            foreach (var leaf in skeleton.leaves)
            {
                var cell = cells != null && leaf.cell >= 0 && leaf.cell < cells.Count ? cells[leaf.cell] : LeafCell.FullTexture;
                AddLeaf(data, leaf, cell, def.leaves.shape, canopy);
            }
            return data;
        }

        static void AddBranch(MeshData data, Branch branch, MeshSettings settings, float trunkRadius)
        {
            // Roots are mostly buried, but the ridge that shows needs more than the 4 sides a thin branch gets.
            var radial = branch.level == 0 ? settings.radialSegments
                : branch.level == Branch.RootLevel ? Mathf.Max(Mathf.Min(settings.minRadialSegments, settings.radialSegments), Mathf.CeilToInt(settings.radialSegments * 0.75f))
                : Mathf.Clamp(Mathf.CeilToInt(settings.radialSegments * branch.radius / trunkRadius),
                    Mathf.Min(settings.minRadialSegments, settings.radialSegments), settings.radialSegments);
            var ring = radial + 1; // the seam vertex is duplicated so U can run 0..1
            var circumference = 2f * Mathf.PI * branch.radius;
            var tris = data.Triangles(MeshData.BarkSubmesh);
            var first = data.vertices.Count;

            var root = branch.level == Branch.RootLevel;
            foreach (var node in branch.nodes)
            {
                var binormal = Vector3.Cross(node.tangent, node.normal);
                var v = node.distance / circumference; // keeps bark texels roughly square
                for (var j = 0; j <= radial; j++)
                {
                    var a = (float)j / radial * 2f * Mathf.PI;
                    var outward = Mathf.Cos(a) * node.normal + Mathf.Sin(a) * binormal;
                    var offset = outward * node.radius;
                    var normal = outward;
                    if (root)
                    {
                        // Squash the ring vertically and widen it across: scale along up and along the
                        // horizontal side, and scale normals by the inverse so lighting follows the new shape.
                        var side = Vector3.Cross(Vector3.up, node.tangent).normalized;
                        offset = Squash(offset, side, RootSpread, RootSquash);
                        normal = Squash(outward, side, 1f / RootSpread, 1f / RootSquash).normalized;
                    }
                    data.AddVertex(node.position + offset, normal, new Vector2((float)j / radial, v));
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

        static Vector3 Squash(Vector3 v, Vector3 side, float across, float up)
        {
            var s = Vector3.Dot(v, side);
            v += side * (s * (across - 1f));
            v.y *= up;
            return v;
        }

        static void AddLeaf(MeshData data, in Leaf leaf, in LeafCell cell, LeafShape shape, Canopy canopy)
        {
            AddCard(data, leaf.position, leaf.up, leaf.width, leaf.normal, leaf.size, cell, canopy);
            if (shape == LeafShape.Cross)
                AddCard(data, leaf.position, leaf.up, leaf.normal, -leaf.width, leaf.size, cell, canopy);
        }

        /// <summary>
        /// Rounds leaf lighting: blends each leaf vertex normal towards the direction out from the
        /// crown's centre, so the canopy shades like one volume rather than many flat cards.
        /// </summary>
        sealed class Canopy
        {
            readonly Vector3 _center;
            readonly Vector3 _inverseExtents;
            readonly float _blend;

            public Canopy(TreeSkeleton skeleton, float blend)
            {
                _blend = blend;
                if (skeleton.leaves.Count == 0)
                    return;
                var bounds = new Bounds(skeleton.leaves[0].position, Vector3.zero);
                foreach (var leaf in skeleton.leaves)
                    bounds.Encapsulate(leaf.position);
                _center = bounds.center;
                // Measure direction in a space where the crown is a sphere, so tall or flat crowns shade evenly.
                var e = Vector3.Max(bounds.extents, Vector3.one * 0.01f);
                _inverseExtents = new Vector3(1f / e.x, 1f / e.y, 1f / e.z);
            }

            public Vector3 Normal(Vector3 position, Vector3 cardNormal)
            {
                if (_blend <= 0f)
                    return cardNormal;
                var outward = Vector3.Scale(position - _center, _inverseExtents);
                if (outward.sqrMagnitude < 1e-8f)
                    return cardNormal;
                return Vector3.Lerp(cardNormal, outward.normalized, _blend).normalized;
            }
        }

        // Double-sided: the back is separate geometry rather than a two-sided material, so it
        // survives export to tools that ignore material culling settings.
        static void AddCard(MeshData data, Vector3 position, Vector3 up, Vector3 width, Vector3 normal, float size, in LeafCell cell, Canopy canopy)
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
                var i0 = data.AddVertex(v0, canopy.Normal(v0, n), new Vector2(uv.xMin, uv.yMin));
                data.AddVertex(v1, canopy.Normal(v1, n), new Vector2(uv.xMin, uv.yMax));
                data.AddVertex(v2, canopy.Normal(v2, n), new Vector2(uv.xMax, uv.yMax));
                data.AddVertex(v3, canopy.Normal(v3, n), new Vector2(uv.xMax, uv.yMin));
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
