using System.IO;
using System.Linq;
using NUnit.Framework;
using TreeWizard.Fbx;
using N = System.Numerics;

namespace TreeWizard.Editor.Tests
{
    public class FbxWriterTests
    {
        static byte[] Bytes(FbxScene scene, FbxWriteOptions options = null)
        {
            using (var ms = new MemoryStream())
            {
                FbxSceneWriter.Write(ms, scene, options);
                return ms.ToArray();
            }
        }

        // A quad (two triangles) with one triangle per material.
        static FbxScene QuadScene(string texture = null)
        {
            var mesh = FbxMesh.FromSubmeshes("Quad",
                new[] { new N.Vector3(0, 0, 0), new N.Vector3(0, 1, 0), new N.Vector3(1, 1, 0), new N.Vector3(1, 0, 0) },
                Enumerable.Repeat(new N.Vector3(0, 0, -1), 4).ToArray(),
                new[] { new N.Vector2(0, 0), new N.Vector2(0, 1), new N.Vector2(1, 1), new N.Vector2(1, 0) },
                new[] { new[] { 0, 1, 2 }, new[] { 0, 2, 3 } });
            var model = new FbxModel("Quad") { Mesh = mesh, Translation = new N.Vector3(2, 0, 0) };
            model.Materials.Add(new FbxMaterial("A") { DiffuseTexture = texture });
            model.Materials.Add(new FbxMaterial("B"));
            var scene = new FbxScene();
            scene.Models.Add(model);
            return scene;
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Output_ParsesStrictly(bool compress)
        {
            var nodes = FbxTestReader.Read(Bytes(QuadScene("a.png"), new FbxWriteOptions { CompressArrays = compress }));

            CollectionAssert.AreEqual(
                new[] { "FBXHeaderExtension", "FileId", "CreationTime", "Creator", "GlobalSettings", "Documents", "References", "Definitions", "Objects", "Connections", "Takes" },
                nodes.Select(n => n.Name).ToArray());
            CollectionAssert.AreEqual(FbxBinaryWriter.FileId, (byte[])nodes[1].Properties[0]);
            Assert.AreEqual(FbxBinaryWriter.CreationTime, nodes[2].Properties[0]);
        }

        [Test]
        public void LargeArrays_RoundTripThroughCompression()
        {
            var values = Enumerable.Range(0, 5000).Select(i => i * 0.25).ToArray();
            using (var ms = new MemoryStream())
            {
                FbxBinaryWriter.Write(ms, new[] { new FbxNode("Data", values) });
                var node = FbxTestReader.Read(ms.ToArray()).Single();
                CollectionAssert.AreEqual(values, (double[])node.Properties[0]);
                Assert.Less(ms.Length, values.Length * 8, "compressed");
            }
        }

        [Test]
        public void Objects_AreDefinedAndConnected()
        {
            var nodes = FbxTestReader.Read(Bytes(QuadScene("a.png")));
            var objects = nodes.Single(n => n.Name == "Objects").Children;
            var definitions = nodes.Single(n => n.Name == "Definitions");
            var connections = nodes.Single(n => n.Name == "Connections").Children;

            foreach (var type in new[] { "Model", "Geometry", "Material", "Texture", "Video" })
            {
                var defined = definitions.Children.Single(c => c.Name == "ObjectType" && (string)c.Properties[0] == type);
                Assert.AreEqual(objects.Count(o => o.Name == type), defined.Find("Count").Properties[0], type);
            }

            var ids = objects.Select(o => (long)o.Properties[0]).ToList();
            CollectionAssert.AllItemsAreUnique(ids);
            foreach (var c in connections)
            {
                CollectionAssert.Contains(ids, (long)c.Properties[1], "child exists");
                var parent = (long)c.Properties[2];
                Assert.IsTrue(parent == 0 || ids.Contains(parent), "parent exists or is the scene root");
            }

            var model = objects.Single(o => o.Name == "Model");
            var materialLinks = connections.Where(c => (long)c.Properties[2] == (long)model.Properties[0])
                .Select(c => objects.Single(o => (long)o.Properties[0] == (long)c.Properties[1]))
                .Where(o => o.Name == "Material")
                .Select(o => ((string)o.Properties[1]).Split('\0')[0]);
            CollectionAssert.AreEqual(new[] { "A", "B" }, materialLinks, "material order follows the model's list");
        }

        [Test]
        public void NormalMap_IsLinkedToItsMaterial()
        {
            var scene = QuadScene("a.png");
            scene.Models[0].Materials[0].NormalTexture = "a_Normal.png";
            var nodes = FbxTestReader.Read(Bytes(scene));
            var objects = nodes.Single(n => n.Name == "Objects").Children;
            var connections = nodes.Single(n => n.Name == "Connections").Children;

            var normalTexture = objects.Single(o => o.Name == "Texture" && (string)o.Find("RelativeFilename").Properties[0] == "a_Normal.png");
            var link = connections.Single(c => (long)c.Properties[1] == (long)normalTexture.Properties[0]);
            Assert.AreEqual("OP", link.Properties[0]);
            Assert.AreEqual("NormalMap", link.Properties[3]);
        }

        [Test]
        public void Geometry_EncodesPolygonsAndPerPolygonMaterials()
        {
            var geometry = Geometry(Bytes(QuadScene()));

            CollectionAssert.AreEqual(new[] { 0, 1, ~2, 0, 2, ~3 }, (int[])geometry.Find("PolygonVertexIndex").Properties[0]);
            var materials = geometry.Find("LayerElementMaterial");
            Assert.AreEqual("ByPolygon", materials.Find("MappingInformationType").Properties[0]);
            CollectionAssert.AreEqual(new[] { 0, 1 }, (int[])materials.Find("Materials").Properties[0]);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 0, 2, 3 }, (int[])geometry.Find("LayerElementUV").Find("UVIndex").Properties[0]);
        }

        [Test]
        public void ConvertFromLeftHanded_MirrorsXAndReversesWinding()
        {
            var geometry = Geometry(Bytes(QuadScene(), new FbxWriteOptions { ConvertFromLeftHanded = true }));

            var vertices = (double[])geometry.Find("Vertices").Properties[0];
            CollectionAssert.AreEqual(new double[] { 0, 0, 0, 0, 1, 0, -1, 1, 0, -1, 0, 0 }, vertices);
            CollectionAssert.AreEqual(new[] { 2, 1, ~0, 3, 2, ~0 }, (int[])geometry.Find("PolygonVertexIndex").Properties[0]);
            var normals = (double[])geometry.Find("LayerElementNormal").Find("Normals").Properties[0];
            Assert.AreEqual(-1d, normals[2]);
            Assert.AreEqual(0d, normals[0]); // -0 compares equal; X of (0,0,-1) stays 0
        }

        [Test]
        public void TexturePaths_AreRelativeToTheFile()
        {
            var dir = Path.Combine(Path.GetTempPath(), "TreeWizardFbxTest");
            var nodes = FbxSceneWriter.BuildDocument(QuadScene(Path.Combine(dir, "Textures", "a.png")), new FbxWriteOptions(), dir);
            var texture = nodes.Single(n => n.Name == "Objects").Children.Single(o => o.Name == "Texture");

            Assert.AreEqual("Textures/a.png", texture.Find("RelativeFilename").Properties[0]);
            StringAssert.EndsWith("TreeWizardFbxTest/Textures/a.png", (string)texture.Find("FileName").Properties[0]);
        }

        static FbxNode Geometry(byte[] file) =>
            FbxTestReader.Read(file).Single(n => n.Name == "Objects").Children.Single(o => o.Name == "Geometry");
    }
}
