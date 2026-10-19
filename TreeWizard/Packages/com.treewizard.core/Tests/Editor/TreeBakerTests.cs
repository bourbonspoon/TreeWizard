using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TreeWizard.Editor.Tests
{
    public class TreeBakerTests
    {
        const string Folder = "Assets/__TreeWizardTests";
        GameObject _go;
        TreeWizardTree _tree;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", "__TreeWizardTests");
            _go = new GameObject("Test Tree");
            _tree = _go.AddComponent<TreeWizardTree>();
            _tree.Rebuild();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            AssetDatabase.DeleteAsset(Folder);
        }

        [Test]
        public void Bake_WritesBarkAndLeavesAsChildren_WithMeshesBesideThePrefab()
        {
            var path = $"{Folder}/Baked.prefab";
            var prefab = TreeBaker.BakeToPrefab(_tree, path);

            Assert.IsNull(prefab.GetComponent<MeshFilter>(), "the root holds no mesh of its own");
            Assert.IsNull(prefab.GetComponent<TreeWizardTree>(), "baked prefab doesn't need TreeWizard at runtime");
            var bark = Part(prefab, TreeBaker.BarkPart);
            var leaves = Part(prefab, TreeBaker.LeavesPart);
            Assert.AreEqual($"{Folder}/Baked_Bark.mesh", AssetDatabase.GetAssetPath(bark.sharedMesh));
            Assert.AreEqual($"{Folder}/Baked_Leaves.mesh", AssetDatabase.GetAssetPath(leaves.sharedMesh));
            Assert.AreEqual(0, AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().Count(), "nothing embedded in the prefab");

            Assert.AreEqual(_tree.Mesh.GetTriangles(MeshData.BarkSubmesh).Length, bark.sharedMesh.triangles.Length);
            Assert.AreEqual(_tree.Mesh.GetTriangles(MeshData.LeafSubmesh).Length, leaves.sharedMesh.triangles.Length);
            Assert.Greater(leaves.sharedMesh.triangles.Length, 0);
            Assert.AreEqual(_tree.Mesh.vertexCount, bark.sharedMesh.vertexCount + leaves.sharedMesh.vertexCount, "each part keeps only its own vertices");
            Assert.AreEqual(1, bark.sharedMesh.subMeshCount);
            Assert.AreEqual(_tree.barkMaterial, bark.GetComponent<MeshRenderer>().sharedMaterial);
            Assert.AreEqual(_tree.leafMaterial, leaves.GetComponent<MeshRenderer>().sharedMaterial);
        }

        [Test]
        public void Submesh_KeepsTheSameTriangles()
        {
            var source = _tree.Mesh;
            var part = TreeBaker.Submesh(source, MeshData.LeafSubmesh);
            try
            {
                var sourceIndices = source.GetTriangles(MeshData.LeafSubmesh);
                var partIndices = part.triangles;
                for (var i = 0; i < partIndices.Length; i += 97)
                {
                    Assert.AreEqual(source.vertices[sourceIndices[i]], part.vertices[partIndices[i]]);
                    Assert.AreEqual(source.normals[sourceIndices[i]], part.normals[partIndices[i]]);
                    Assert.AreEqual(source.uv[sourceIndices[i]], part.uv[partIndices[i]]);
                }
                Assert.AreEqual(part.vertexCount, part.tangents.Length, "tangents come across for normal mapping");
            }
            finally
            {
                Object.DestroyImmediate(part);
            }
        }

        [Test]
        public void Rebake_UpdatesEverythingInPlace()
        {
            var path = $"{Folder}/Baked.prefab";
            var first = TreeBaker.BakeToPrefab(_tree, path);
            var ids = Ids(first);
            var firstVertex = Part(first, TreeBaker.BarkPart).sharedMesh.vertices[^1];

            _tree.definition.seed = 99;
            _tree.Rebuild();
            var second = TreeBaker.BakeToPrefab(_tree, path);

            CollectionAssert.AreEqual(ids, Ids(second), "existing references to the prefab, its parts and meshes stay valid");
            Assert.AreNotEqual(firstVertex, Part(second, TreeBaker.BarkPart).sharedMesh.vertices[^1], "mesh data was replaced");
            Assert.AreEqual(2, second.transform.childCount);
        }

        [Test]
        public void Rebake_ConvertsAnOldSingleMeshPrefab_KeepingTheRoot()
        {
            // The layout earlier versions baked: one two-material mesh embedded on the root.
            var path = $"{Folder}/Old.prefab";
            var go = new GameObject("Old", typeof(MeshFilter), typeof(MeshRenderer));
            GameObject old;
            try
            {
                old = PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
            var embedded = Object.Instantiate(_tree.Mesh);
            AssetDatabase.AddObjectToAsset(embedded, old);
            old.GetComponent<MeshFilter>().sharedMesh = embedded;
            PrefabUtility.SavePrefabAsset(old);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(old, out var guid, out long rootId);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(old.transform, out _, out long transformId);

            var baked = TreeBaker.BakeToPrefab(_tree, path);

            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(baked, out var newGuid, out long newRootId);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(baked.transform, out _, out long newTransformId);
            Assert.AreEqual(guid, newGuid);
            Assert.AreEqual(rootId, newRootId, "things nesting or referencing the root still find it");
            Assert.AreEqual(transformId, newTransformId);
            Assert.IsNull(baked.GetComponent<MeshFilter>());
            Assert.IsNull(baked.GetComponent<MeshRenderer>());
            Assert.AreEqual(0, AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().Count(), "the old embedded mesh is gone");
            Assert.IsNotNull(Part(baked, TreeBaker.LeavesPart).sharedMesh);
        }

        [Test]
        public void Parts_FollowTheRootsLayerAndStaticFlags()
        {
            var path = $"{Folder}/Baked.prefab";
            var prefab = TreeBaker.BakeToPrefab(_tree, path);
            prefab.layer = 4;
            GameObjectUtility.SetStaticEditorFlags(prefab, StaticEditorFlags.BatchingStatic);
            PrefabUtility.SavePrefabAsset(prefab);

            prefab = TreeBaker.BakeToPrefab(_tree, path);
            foreach (Transform part in prefab.transform)
            {
                Assert.AreEqual(4, part.gameObject.layer);
                Assert.AreEqual(StaticEditorFlags.BatchingStatic, GameObjectUtility.GetStaticEditorFlags(part.gameObject));
            }
        }

        static MeshFilter Part(GameObject prefab, string name)
        {
            var part = prefab.transform.Find(name);
            Assert.IsNotNull(part, $"{name} child");
            return part.GetComponent<MeshFilter>();
        }

        static List<string> Ids(GameObject prefab)
        {
            var objects = new List<Object> { prefab, prefab.transform };
            foreach (Transform part in prefab.transform)
                objects.AddRange(new Object[] { part.gameObject, part.GetComponent<MeshFilter>(), part.GetComponent<MeshRenderer>(), part.GetComponent<MeshFilter>().sharedMesh });
            return objects.Select(o => AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out var guid, out long id) ? $"{guid}:{id}" : "none").ToList();
        }

        [Test]
        public void Preset_RoundTripsSettings_WithoutSharingThem()
        {
            _tree.definition.seed = 1234;
            _tree.definition.trunk.height = 9f;
            var preset = TreeBaker.SaveAsPreset(_tree, $"{Folder}/Preset.asset");

            var other = new GameObject("Other").AddComponent<TreeWizardTree>();
            try
            {
                other.ApplyPreset(preset);
                Assert.AreEqual(JsonUtility.ToJson(_tree.definition), JsonUtility.ToJson(other.definition));

                other.definition.trunk.height = 2f;
                Assert.AreEqual(9f, preset.definition.trunk.height, "editing a tree must not edit its preset");
            }
            finally
            {
                Object.DestroyImmediate(other.gameObject);
            }
        }
    }
}
