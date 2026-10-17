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
        public void Bake_WritesPrefabWithEmbeddedMesh()
        {
            var path = $"{Folder}/Baked.prefab";
            var prefab = TreeBaker.BakeToPrefab(_tree, path);

            var mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(mesh);
            Assert.AreEqual(path, AssetDatabase.GetAssetPath(mesh), "mesh lives inside the prefab file");
            Assert.AreEqual(_tree.Mesh.vertexCount, mesh.vertexCount);
            Assert.AreEqual(HideFlags.None, mesh.hideFlags);
            Assert.IsNull(prefab.GetComponent<TreeWizardTree>(), "baked prefab doesn't need TreeWizard at runtime");
        }

        [Test]
        public void Rebake_UpdatesTheSameMeshInPlace()
        {
            var path = $"{Folder}/Baked.prefab";
            var first = TreeBaker.BakeToPrefab(_tree, path).GetComponent<MeshFilter>().sharedMesh;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(first, out _, out long firstId);
            var firstVertex = first.vertices[^1];

            _tree.definition.seed = 99;
            _tree.Rebuild();
            var second = TreeBaker.BakeToPrefab(_tree, path).GetComponent<MeshFilter>().sharedMesh;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(second, out _, out long secondId);

            Assert.AreEqual(firstId, secondId, "existing references to the mesh stay valid");
            Assert.AreNotEqual(firstVertex, second.vertices[^1], "mesh data was replaced");
            Assert.AreEqual(1, AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().Count(), "no orphaned meshes");
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
