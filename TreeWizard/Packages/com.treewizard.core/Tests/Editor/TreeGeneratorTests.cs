using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace TreeWizard.Editor.Tests
{
    public class TreeGeneratorTests
    {
        [Test]
        public void SameSeed_GivesIdenticalMesh()
        {
            var a = TreeGenerator.Generate(new TreeDefinition { seed = 42 });
            var b = TreeGenerator.Generate(new TreeDefinition { seed = 42 });

            CollectionAssert.AreEqual(a.vertices, b.vertices);
            CollectionAssert.AreEqual(a.Triangles(MeshData.BarkSubmesh), b.Triangles(MeshData.BarkSubmesh));
            CollectionAssert.AreEqual(a.Triangles(MeshData.LeafSubmesh), b.Triangles(MeshData.LeafSubmesh));
        }

        [Test]
        public void DifferentSeed_GivesDifferentMesh()
        {
            var a = TreeGenerator.Generate(new TreeDefinition { seed = 1 });
            var b = TreeGenerator.Generate(new TreeDefinition { seed = 2 });

            CollectionAssert.AreNotEqual(a.vertices, b.vertices);
        }

        [Test]
        public void BranchAndLeafCounts_FollowSettings()
        {
            var def = new TreeDefinition();
            var skeleton = TreeSkeleton.Generate(def);

            var level1 = def.branchLevels[0].count;
            var level2 = level1 * def.branchLevels[1].count;
            Assert.AreEqual(1, skeleton.branches.Count(b => b.level == 0));
            Assert.AreEqual(level1, skeleton.branches.Count(b => b.level == 1));
            Assert.AreEqual(level2, skeleton.branches.Count(b => b.level == 2));
            Assert.AreEqual(level2 * def.leaves.count, skeleton.leaves.Count);
        }

        [Test]
        public void ChildBranches_GrowFromTheirParent()
        {
            var skeleton = TreeSkeleton.Generate(new TreeDefinition());

            foreach (var branch in skeleton.branches.Where(b => b.parent >= 0))
            {
                var parent = skeleton.branches[branch.parent];
                Assert.AreEqual(branch.level - 1, parent.level);
                var origin = branch.nodes[0].position;
                var closest = Enumerable.Range(0, 101).Min(i => Vector3.Distance(parent.Sample(i / 100f).position, origin));
                Assert.Less(closest, parent.Length / 50f, "branch should start on its parent's center line");
            }
        }

        [Test]
        public void ZeroCountLevel_StopsBranching_AndLeavesMoveToLastLevel()
        {
            var def = new TreeDefinition();
            def.branchLevels[1].count = 0;
            var skeleton = TreeSkeleton.Generate(def);

            Assert.IsFalse(skeleton.branches.Any(b => b.level == 2));
            Assert.AreEqual(def.branchLevels[0].count * def.leaves.count, skeleton.leaves.Count);
        }

        [Test]
        public void DisabledLeaves_LeaveLeafSubmeshEmpty()
        {
            var def = new TreeDefinition();
            def.leaves.enabled = false;

            Assert.IsEmpty(TreeGenerator.Generate(def).Triangles(MeshData.LeafSubmesh));
        }

        [Test]
        public void CrossLeaves_DoubleTheLeafTriangles()
        {
            var quad = new TreeDefinition();
            var cross = new TreeDefinition();
            cross.leaves.shape = LeafShape.Cross;

            Assert.AreEqual(
                TreeGenerator.Generate(quad).Triangles(MeshData.LeafSubmesh).Count * 2,
                TreeGenerator.Generate(cross).Triangles(MeshData.LeafSubmesh).Count);
        }

        [Test]
        public void Mesh_IsWellFormed()
        {
            var data = TreeGenerator.Generate(new TreeDefinition());

            Assert.AreEqual(data.vertices.Count, data.normals.Count);
            Assert.AreEqual(data.vertices.Count, data.uvs.Count);
            Assert.IsTrue(data.vertices.All(IsFinite), "no NaN or infinite positions");
            Assert.IsTrue(data.normals.All(n => Mathf.Abs(n.magnitude - 1f) < 1e-3f), "normals are unit length");
            for (var s = 0; s < MeshData.SubmeshCount; s++)
            {
                var tris = data.Triangles(s);
                Assert.AreEqual(0, tris.Count % 3);
                Assert.IsTrue(tris.All(i => i >= 0 && i < data.vertices.Count), "indices in range");
            }
        }

        [Test]
        public void Triangles_FaceTheirVertexNormals()
        {
            var data = TreeGenerator.Generate(new TreeDefinition());

            for (var s = 0; s < MeshData.SubmeshCount; s++)
            {
                var tris = data.Triangles(s);
                for (var i = 0; i < tris.Count; i += 3)
                {
                    var v0 = data.vertices[tris[i]];
                    var face = Vector3.Cross(data.vertices[tris[i + 1]] - v0, data.vertices[tris[i + 2]] - v0);
                    if (face.sqrMagnitude < 1e-12f)
                        continue; // sliver at a sharp tip
                    var normal = data.normals[tris[i]] + data.normals[tris[i + 1]] + data.normals[tris[i + 2]];
                    Assert.Greater(Vector3.Dot(face, normal), 0f, $"submesh {s} triangle {i / 3} is wound inside out");
                }
            }
        }

        [Test]
        public void DefaultTree_StaysLightweight()
        {
            Assert.Less(TreeGenerator.Generate(new TreeDefinition()).TriangleCount, 10000);
        }

        [Test]
        public void MaxLevels_IsEnforced()
        {
            var def = new TreeDefinition
            {
                branchLevels = Enumerable.Range(0, 5).Select(_ => new BranchLevelSettings { count = 2 }).ToArray(),
            };
            var skeleton = TreeSkeleton.Generate(def);

            Assert.AreEqual(TreeDefinition.MaxBranchLevels, skeleton.branches.Max(b => b.level));
        }

        static readonly LeafCell[] TwoSeasons =
        {
            new LeafCell { uvRect = new Rect(0f, 0f, 0.5f, 1f), pivot = new Vector2(0.5f, 0f), aspect = 0.5f, weight = 1f },
            new LeafCell { uvRect = new Rect(0.5f, 0f, 0.5f, 1f), pivot = new Vector2(0.5f, 0f), aspect = 0.5f, weight = 1f, autumn = true },
        };

        [TestCase(0f, 0)]
        [TestCase(1f, 1)]
        public void Autumn_PicksMatchingCells(float autumn, int expectedCell)
        {
            var def = new TreeDefinition();
            def.leaves.autumn = autumn;

            CollectionAssert.AreEquivalent(new[] { expectedCell }, TreeSkeleton.Generate(def, TwoSeasons).leaves.Select(l => l.cell).Distinct());
        }

        [Test]
        public void Autumn_HalfwayMixesCells()
        {
            var def = new TreeDefinition();
            def.leaves.autumn = 0.5f;
            var leaves = TreeSkeleton.Generate(def, TwoSeasons).leaves;

            var share = leaves.Count(l => l.cell == 1) / (float)leaves.Count;
            Assert.AreEqual(0.5f, share, 0.1f);
        }

        [Test]
        public void ZeroWeightCells_AreNeverPicked()
        {
            var cells = TwoSeasons.ToArray();
            cells[0].weight = 0f;

            Assert.IsTrue(TreeSkeleton.Generate(new TreeDefinition(), cells).leaves.All(l => l.cell == 1),
                "with the only summer cell disabled, summer leaves fall back to any enabled cell");
        }

        [Test]
        public void LeafCards_UseTheirCellsUvsAndProportions()
        {
            var def = new TreeDefinition();
            def.leaves.autumn = 1f;
            var data = TreeGenerator.Generate(def, TwoSeasons);

            var leafVerts = data.Triangles(MeshData.LeafSubmesh).Distinct().ToList();
            Assert.IsTrue(leafVerts.All(i => data.uvs[i].x >= 0.5f && data.uvs[i].x <= 1f), "UVs stay in the autumn cell");

            // First card: v0..v3 are bottom-left, top-left, top-right, bottom-right.
            var first = leafVerts.Min();
            var height = Vector3.Distance(data.vertices[first], data.vertices[first + 1]);
            var width = Vector3.Distance(data.vertices[first], data.vertices[first + 3]);
            Assert.AreEqual(0.5f, width / height, 1e-3f);
        }

        static bool IsFinite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
