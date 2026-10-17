using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TreeWizard.Editor.Tests
{
    /// <summary>Exports a tree, imports it back through Unity's FBX importer and compares.</summary>
    public class TreeFbxImportTests
    {
        const string Folder = "Assets/__TreeWizardFbxTests";
        const string FbxPath = Folder + "/Oak.fbx";

        MeshData _source;
        Mesh _imported;
        GameObject _go;

        [OneTimeSetUp]
        public void ExportAndImport()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", "__TreeWizardFbxTests");

            _go = new GameObject("Oak");
            var tree = _go.AddComponent<TreeWizardTree>();
            var preset = AssetDatabase.LoadAssetAtPath<TreePreset>(TreeWizardMenus.DefaultPreset);
            Assert.IsNotNull(preset, "oak preset");
            tree.ApplyPreset(preset);

            _source = TreeGenerator.Generate(tree.definition, tree.leafAtlas.cells);
            TreeFbxExporter.Export(tree, FbxPath);
            AssetDatabase.Refresh();
            _imported = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<Mesh>().Single();
        }

        [OneTimeTearDown]
        public void CleanUp()
        {
            Object.DestroyImmediate(_go);
            AssetDatabase.DeleteAsset(Folder);
        }

        [Test]
        public void KeepsEveryTriangle_InTwoSubmeshes()
        {
            Assert.AreEqual(2, _imported.subMeshCount);
            Assert.AreEqual(_source.Triangles(MeshData.BarkSubmesh).Count, (int)_imported.GetIndexCount(0));
            Assert.AreEqual(_source.Triangles(MeshData.LeafSubmesh).Count, (int)_imported.GetIndexCount(1));
        }

        [Test]
        public void ComesBackInTheSamePlace_NotMirroredOrScaled()
        {
            var imported = _imported.vertices;
            var bounds = new Bounds(_source.vertices[0], Vector3.zero);
            foreach (var v in _source.vertices)
                bounds.Encapsulate(v);
            Assert.AreEqual(bounds.center.x, _imported.bounds.center.x, 1e-3f);
            Assert.AreEqual(bounds.size.y, _imported.bounds.size.y, 1e-3f);

            // Trees aren't symmetric, so a mirrored import would miss these.
            for (var i = 0; i < _source.vertices.Count; i += 97)
            {
                var v = _source.vertices[i];
                Assert.Less(imported.Min(p => (p - v).sqrMagnitude), 1e-8f, $"source vertex {i} at {v}");
            }
        }

        [Test]
        public void Triangles_StillFaceOutward()
        {
            var vertices = _imported.vertices;
            var normals = _imported.normals;
            for (var s = 0; s < _imported.subMeshCount; s++)
            {
                var tris = _imported.GetTriangles(s);
                for (var i = 0; i < tris.Length; i += 3)
                {
                    var face = Vector3.Cross(vertices[tris[i + 1]] - vertices[tris[i]], vertices[tris[i + 2]] - vertices[tris[i]]);
                    if (face.sqrMagnitude < 1e-12f)
                        continue;
                    var normal = normals[tris[i]] + normals[tris[i + 1]] + normals[tris[i + 2]];
                    Assert.Greater(Vector3.Dot(face, normal), 0f, $"submesh {s} triangle {i / 3} is inside out");
                }
            }
        }

        [Test]
        public void Materials_AndTextures_ComeAlong()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
            var materialNames = importer.GetExternalObjectMap().Keys.Select(k => k.name)
                .Concat(AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<Material>().Select(m => m.name))
                .ToList();
            CollectionAssert.IsSupersetOf(materialNames, new[] { "Oak_Bark", "Oak_Leaves" });

            var bark = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<Material>().Single(m => m.name == "Oak_Bark");
            var source = _go.GetComponent<TreeWizardTree>().barkMaterial.color;
            Assert.AreEqual(source.r, bark.color.r, 0.01f, "colour survives the round trip");
            Assert.AreEqual(source.b, bark.color.b, 0.01f, "colour survives the round trip");

            Assert.IsTrue(File.Exists($"{Folder}/Oak_Leaves.png"), "leaf texture written beside the FBX");
            var leafTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}/Oak_Leaves.png");
            Assert.IsNotNull(leafTexture);
        }
    }
}
