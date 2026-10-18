using System;
using System.Collections.Generic;
using UnityEngine;

namespace TreeWizard
{
    public struct BranchNode
    {
        public Vector3 position;
        /// <summary>Direction of growth.</summary>
        public Vector3 tangent;
        /// <summary>Perpendicular to the tangent, parallel-transported so rings don't twist.</summary>
        public Vector3 normal;
        public float radius;
        /// <summary>Distance from the start of the branch.</summary>
        public float distance;
    }

    public class Branch
    {
        /// <summary>0 for the trunk, 1+ for branch levels.</summary>
        public int level;
        /// <summary>Index of the parent in <see cref="TreeSkeleton.branches"/>, -1 for the trunk.</summary>
        public int parent = -1;
        /// <summary>Base radius, ignoring trunk flare. Drives how many sides the mesh gets.</summary>
        public float radius;
        public readonly List<BranchNode> nodes = new List<BranchNode>();

        public float Length => nodes[nodes.Count - 1].distance;

        /// <summary>Interpolated node at <paramref name="t"/> along the branch's length (0 = base, 1 = tip).</summary>
        public BranchNode Sample(float t)
        {
            var d = Mathf.Clamp01(t) * Length;
            var i = 1;
            while (i < nodes.Count - 1 && nodes[i].distance < d)
                i++;

            var a = nodes[i - 1];
            var b = nodes[i];
            var span = b.distance - a.distance;
            var f = span > 0f ? Mathf.Clamp01((d - a.distance) / span) : 0f;
            var tangent = Vector3.Slerp(a.tangent, b.tangent, f).normalized;
            return new BranchNode
            {
                position = Vector3.Lerp(a.position, b.position, f),
                tangent = tangent,
                normal = Vector3.ProjectOnPlane(Vector3.Slerp(a.normal, b.normal, f), tangent).normalized,
                radius = Mathf.Lerp(a.radius, b.radius, f),
                distance = d,
            };
        }
    }

    public struct Leaf
    {
        /// <summary>Where the leaf's stem meets the branch.</summary>
        public Vector3 position;
        /// <summary>Direction from stem to leaf tip.</summary>
        public Vector3 up;
        /// <summary>Across the leaf. Front face normal is cross(up, width).</summary>
        public Vector3 width;
        public Vector3 normal;
        public float size;
        /// <summary>Index into the leaf cells, or -1 for the whole texture.</summary>
        public int cell;
    }

    /// <summary>
    /// The tree as center lines and leaf placements, before any mesh is built.
    /// </summary>
    public class TreeSkeleton
    {
        const float GoldenAngle = 137.50776f;
        const float BranchTipRadius = 0.2f;   // fraction of a branch's base radius
        const float MinRadius = 0.002f;
        const float MaxWobble = 1.5f;         // radians, spread over a branch's segments
        const int LeafStream = 1000;

        public readonly List<Branch> branches = new List<Branch>();
        public readonly List<Leaf> leaves = new List<Leaf>();

        /// <param name="cells">Leaf atlas cells to pick from, or null to use the whole leaf texture.</param>
        public static TreeSkeleton Generate(TreeDefinition def, IReadOnlyList<LeafCell> cells = null)
        {
            var skeleton = new TreeSkeleton();
            var trunk = def.trunk;
            skeleton.branches.Add(Grow(0, -1, Vector3.zero, Vector3.up, trunk.height, trunk.radius, trunk.segments,
                trunk.crookedness, 0f, t => TrunkRadius(trunk, t), new Rng(Rng.Combine(def.seed, 0))));

            // Each level grows off every branch of the previous one: [levelStart, levelEnd).
            int levelStart = 0, levelEnd = 1;
            var levels = Mathf.Min(def.branchLevels?.Length ?? 0, TreeDefinition.MaxBranchLevels);
            for (var level = 1; level <= levels; level++)
            {
                var settings = def.branchLevels[level - 1];
                if (settings == null || settings.count <= 0)
                    break;

                var next = skeleton.branches.Count;
                for (var p = levelStart; p < levelEnd; p++)
                    skeleton.AddChildren(p, level, settings, def.seed);

                if (skeleton.branches.Count == next)
                    break;
                levelStart = next;
                levelEnd = skeleton.branches.Count;
            }

            if (def.leaves.enabled)
            {
                var picker = new CellPicker(cells);
                for (var b = levelStart; b < levelEnd; b++)
                    skeleton.AddLeaves(b, def.leaves, def.seed, picker);
            }

            return skeleton;
        }

        static float TrunkRadius(TrunkSettings trunk, float t)
        {
            var r = Mathf.Lerp(trunk.radius, trunk.radius * trunk.tipRadius, t);
            if (trunk.flareHeight > 0f && t < trunk.flareHeight)
            {
                var f = 1f - t / trunk.flareHeight;
                r *= 1f + trunk.flare * f * f;
            }
            return Mathf.Max(r, MinRadius);
        }

        void AddChildren(int parentIndex, int level, BranchLevelSettings s, int seed)
        {
            var parent = branches[parentIndex];
            var rng = new Rng(Rng.Combine(Rng.Combine(seed, level), parentIndex));
            var start = Mathf.Min(s.start, s.end);
            var end = Mathf.Max(s.start, s.end);
            var azimuthOffset = rng.Range(0f, 360f);

            for (var i = 0; i < s.count; i++)
            {
                var f = Mathf.Clamp01((i + 0.5f + rng.Range(-0.4f, 0.4f)) / s.count);
                var t = Mathf.Lerp(start, end, f);
                var at = parent.Sample(t);

                var azimuth = azimuthOffset + i * GoldenAngle + rng.Range(-15f, 15f);
                var angle = s.angle + rng.Range(-s.angleVariance, s.angleVariance);
                var direction = AwayFrom(at, azimuth, angle);

                var length = parent.Length * s.length * Mathf.Lerp(1f, s.lengthAtEnd, f)
                             * (1f + rng.Range(-1f, 1f) * s.lengthVariance);
                var growRng = rng.Fork(); // drawn every iteration so skipped branches don't reshuffle the rest
                if (length < 0.01f)
                    continue;

                var radius = Mathf.Max(at.radius * s.radius, MinRadius);
                var branch = Grow(level, parentIndex, at.position, direction, length, radius, Mathf.Max(2, s.segments),
                    s.crookedness, s.gravity, u => Mathf.Lerp(radius, radius * BranchTipRadius, u), growRng);
                branches.Add(branch);
            }
        }

        void AddLeaves(int branchIndex, LeafSettings s, int seed, CellPicker picker)
        {
            var branch = branches[branchIndex];
            var rng = new Rng(Rng.Combine(Rng.Combine(seed, LeafStream), branchIndex));
            var azimuthOffset = rng.Range(0f, 360f);

            for (var i = 0; i < s.count; i++)
            {
                var f = Mathf.Clamp01((i + 0.5f + rng.Range(-0.4f, 0.4f)) / s.count);
                var at = branch.Sample(Mathf.Lerp(s.start, 1f, f));

                var azimuth = azimuthOffset + i * GoldenAngle + rng.Range(-15f, 15f);
                var up = AwayFrom(at, azimuth, s.angle + rng.Range(-15f, 15f));

                // Turn the card to face somewhere between the sky and away from the trunk, then roll it a little.
                var away = new Vector3(at.position.x, 0f, at.position.z);
                var facing = Vector3.Lerp(Vector3.up, away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.up, s.outward);
                var width = Vector3.Cross(facing, up); // makes the card normal, cross(up, width), point along facing
                if (width.sqrMagnitude < 1e-4f)
                    width = Vector3.Cross(Vector3.right, up);
                width = Quaternion.AngleAxis(rng.Range(-30f, 30f), up) * width.normalized;

                leaves.Add(new Leaf
                {
                    position = at.position,
                    up = up,
                    width = width,
                    normal = Vector3.Cross(up, width).normalized,
                    size = s.size * (1f + rng.Range(-1f, 1f) * s.sizeVariance),
                    cell = picker.Pick(rng.Value() < s.autumn, rng.Value()),
                });
            }
        }

        /// <summary>Weighted choice of leaf cell, from the autumn or summer pool.</summary>
        class CellPicker
        {
            readonly IReadOnlyList<LeafCell> _cells;
            readonly List<int> _summer = new List<int>();
            readonly List<int> _autumn = new List<int>();
            readonly List<int> _all = new List<int>();

            public CellPicker(IReadOnlyList<LeafCell> cells)
            {
                _cells = cells;
                if (cells == null)
                    return;
                for (var i = 0; i < cells.Count; i++)
                {
                    if (cells[i].weight <= 0f)
                        continue;
                    _all.Add(i);
                    (cells[i].autumn ? _autumn : _summer).Add(i);
                }
            }

            /// <param name="roll">Uniform in [0, 1).</param>
            public int Pick(bool autumn, float roll)
            {
                var pool = autumn ? _autumn : _summer;
                if (pool.Count == 0)
                    pool = _all;
                if (pool.Count == 0)
                    return -1;

                var total = 0f;
                foreach (var i in pool)
                    total += _cells[i].weight;
                var target = roll * total;
                foreach (var i in pool)
                {
                    target -= _cells[i].weight;
                    if (target < 0f)
                        return i;
                }
                return pool[pool.Count - 1];
            }
        }

        /// <summary>Direction tilted <paramref name="angle"/> degrees off the node's tangent, on the side picked by <paramref name="azimuth"/>.</summary>
        static Vector3 AwayFrom(in BranchNode at, float azimuth, float angle)
        {
            var az = azimuth * Mathf.Deg2Rad;
            var side = Mathf.Cos(az) * at.normal + Mathf.Sin(az) * Vector3.Cross(at.tangent, at.normal);
            var a = angle * Mathf.Deg2Rad;
            return (Mathf.Cos(a) * at.tangent + Mathf.Sin(a) * side).normalized;
        }

        static Branch Grow(int level, int parent, Vector3 origin, Vector3 direction, float length, float radius,
            int segments, float crookedness, float gravity, Func<float, float> radiusAt, Rng rng)
        {
            var branch = new Branch { level = level, parent = parent, radius = radius };
            var position = origin;
            var dir = direction.normalized;
            var step = length / segments;
            var pull = gravity >= 0f ? Vector3.down : Vector3.up;
            var bendPerStep = Mathf.Abs(gravity) * (Mathf.PI * 0.5f) / segments;
            var wobblePerStep = crookedness * MaxWobble / Mathf.Sqrt(segments);

            for (var i = 0; i <= segments; i++)
            {
                branch.nodes.Add(new BranchNode
                {
                    position = position,
                    radius = radiusAt((float)i / segments),
                    distance = i * step,
                });
                dir = Vector3.RotateTowards(dir, rng.OnUnitSphere(), wobblePerStep, 0f);
                dir = Vector3.RotateTowards(dir, pull, bendPerStep, 0f);
                position += dir * step;
            }

            ComputeFrames(branch.nodes);
            return branch;
        }

        static void ComputeFrames(List<BranchNode> nodes)
        {
            var n = nodes.Count;
            var normal = Vector3.zero;
            var previousTangent = Vector3.zero;
            for (var i = 0; i < n; i++)
            {
                var node = nodes[i];
                node.tangent = (nodes[Mathf.Min(i + 1, n - 1)].position - nodes[Mathf.Max(i - 1, 0)].position).normalized;
                normal = i == 0
                    ? AnyPerpendicular(node.tangent)
                    : Vector3.ProjectOnPlane(Quaternion.FromToRotation(previousTangent, node.tangent) * normal, node.tangent).normalized;
                node.normal = normal;
                previousTangent = node.tangent;
                nodes[i] = node;
            }
        }

        static Vector3 AnyPerpendicular(Vector3 v)
        {
            var other = Mathf.Abs(v.y) < 0.99f ? Vector3.up : Vector3.right;
            return Vector3.Cross(v, other).normalized;
        }
    }
}
