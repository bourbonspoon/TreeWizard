using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TreeWizard.Editor.Tests
{
    public class ImpostorBakerTests
    {
        const string Folder = "Assets/__TreeWizardImpostorTests";
        static ImpostorSettings Small() => new ImpostorSettings { frames = 8, atlasSize = 256 };
        GameObject _go;
        TreeWizardTree _tree;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets", "__TreeWizardImpostorTests");
            _go = new GameObject("Test Tree");
            _tree = _go.AddComponent<TreeWizardTree>();
            _tree.ApplyPreset(AssetDatabase.LoadAssetAtPath<TreePreset>(TreeWizardMenus.DefaultPreset));
            _tree.Rebuild();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            AssetDatabase.DeleteAsset(Folder);
        }

        [Test]
        public void Bake_AddsAnImpostorLevel_WithItsFilesBesideThePrefab()
        {
            var prefab = TreeBaker.BakeToPrefab(_tree, $"{Folder}/Tree.prefab", Small());

            foreach (var file in new[] { "Tree_Impostor_Albedo.png", "Tree_Impostor_Normal.png", "Tree_Impostor.mat", "Tree_Impostor.mesh" })
                Assert.IsTrue(File.Exists($"{Folder}/{file}"), file);

            var group = prefab.GetComponent<LODGroup>();
            Assert.IsNotNull(group);
            Assert.AreEqual(LODFadeMode.CrossFade, group.fadeMode);
            Assert.IsFalse(group.animateCrossFading, "animated fades take renderers off the GPU Resident Drawer");
            var lods = group.GetLODs();
            Assert.AreEqual(2, lods.Length);
            CollectionAssert.AreEquivalent(new[] { TreeBaker.BarkPart, TreeBaker.LeavesPart }, lods[0].renderers.Select(r => r.name));
            var impostor = prefab.transform.Find(ImpostorBaker.PartName).GetComponent<MeshRenderer>();
            CollectionAssert.AreEqual(new Renderer[] { impostor }, lods[1].renderers);
            Assert.AreEqual(Small().transition, lods[0].screenRelativeTransitionHeight, 1e-5f);

            var material = impostor.sharedMaterial;
            Assert.AreEqual(ImpostorBaker.ShaderName, material.shader.name);
            Assert.IsFalse(ShaderUtil.ShaderHasError(material.shader), "the impostor shader graph compiles");
            Assert.AreEqual($"{Folder}/Tree_Impostor_Albedo.png", AssetDatabase.GetAssetPath(material.GetTexture("_ImpostorAlbedo")));
            Assert.AreEqual(256, material.GetTexture("_ImpostorAlbedo").width);
            Assert.AreEqual(8f, material.GetFloat("_ImpostorFrames"));
            var bounds = material.GetVector("_ImpostorBounds");
            Assert.Greater(bounds.w, _tree.Mesh.bounds.extents.y, "the sphere holds the whole tree");
        }

        [Test]
        public void Rebake_RecapturesInPlace_KeepingIdsAndSettings()
        {
            var path = $"{Folder}/Tree.prefab";
            var settings = Small();
            settings.transition = 0.3f;
            var ids = Ids(TreeBaker.BakeToPrefab(_tree, path, settings));
            var firstAtlas = File.ReadAllBytes($"{Folder}/Tree_Impostor_Albedo.png");

            _tree.definition.seed = 99;
            _tree.Rebuild();
            var prefab = TreeBaker.BakeToPrefab(_tree, path); // no settings: keeps the impostor and its settings

            CollectionAssert.AreEqual(ids, Ids(prefab), "nested prefabs and placements keep their references");
            CollectionAssert.AreNotEqual(firstAtlas, File.ReadAllBytes($"{Folder}/Tree_Impostor_Albedo.png"), "the new tree was captured");
            var read = ImpostorBaker.ReadSettings(prefab);
            Assert.AreEqual(0.3f, read.transition, 1e-5f);
            Assert.AreEqual(8, read.frames);
            Assert.AreEqual(256, read.atlasSize);
        }

        [Test]
        public void Atlas_PutsEachViewWhereTheShaderLooksForIt()
        {
            // A white block with a small red marker above and to one side of it: any flip or mirror of a frame
            // moves the marker away from where the frame maths says it is.
            var white = MakeMaterial("White", Color.white);
            var red = MakeMaterial("Red", Color.red);
            var root = new GameObject("Marker");
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.transform.SetParent(root.transform, false);
            block.GetComponent<MeshRenderer>().sharedMaterial = white;
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = new Vector3(1.5f, 1f, 0.5f);
            marker.transform.localScale = Vector3.one * 0.4f;
            marker.GetComponent<MeshRenderer>().sharedMaterial = red;
            var markerPosition = marker.transform.localPosition;
            var path = $"{Folder}/Marker.prefab";
            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            var settings = new ImpostorSettings { frames = 8, atlasSize = 512 };
            var prefab = ImpostorBaker.Bake(path, settings);
            var material = prefab.transform.Find(ImpostorBaker.PartName).GetComponent<MeshRenderer>().sharedMaterial;
            var sphere = material.GetVector("_ImpostorBounds");
            var center = (Vector3)sphere;
            var albedo = Load($"{Folder}/Marker_Impostor_Albedo.png");
            var normals = Load($"{Folder}/Marker_Impostor_Normal.png");
            var cell = albedo.width / settings.frames;

            for (var y = 0; y < settings.frames; y++)
            for (var x = 0; x < settings.frames; x++)
            {
                var direction = ImpostorBaker.FrameDirection(x, y, settings.frames);
                var at = ImpostorBaker.FrameUV(markerPosition, center, sphere.w, direction);
                var pixel = albedo.GetPixel(x * cell + (int)(at.x * cell), y * cell + (int)(at.y * cell));
                Assert.Greater(pixel.a, 0.5f, $"frame {x},{y}: marker covered");
                Assert.Greater(pixel.r, 0.6f, $"frame {x},{y}: marker is red");
                Assert.Less(pixel.g, 0.3f, $"frame {x},{y}: marker is red");
            }

            // Seen side on from -X, the block's face points straight at the viewer.
            var side = ImpostorBaker.FrameDirection(0, 0, settings.frames);
            Assert.AreEqual(-1f, side.x, 1e-4f);
            var face = ImpostorBaker.FrameUV(new Vector3(-0.5f, 0f, 0f), center, sphere.w, side);
            var n = normals.GetPixel((int)(face.x * cell), (int)(face.y * cell));
            Assert.Less(n.r, 0.1f, "normal points along -X, toward the viewer");
            Assert.AreEqual(0.5f, n.g, 0.05f);
            Assert.AreEqual(0.5f, n.b, 0.05f);
            Object.DestroyImmediate(albedo);
            Object.DestroyImmediate(normals);
        }

        [Test]
        public void Remove_TakesTheImpostorOff()
        {
            var path = $"{Folder}/Tree.prefab";
            TreeBaker.BakeToPrefab(_tree, path, Small());
            ImpostorBaker.Remove(path);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsFalse(ImpostorBaker.HasImpostor(prefab));
            Assert.IsNull(prefab.GetComponent<LODGroup>());
            Assert.IsFalse(File.Exists($"{Folder}/Tree_Impostor.mat"));
            Assert.IsNotNull(prefab.transform.Find(TreeBaker.LeavesPart), "the tree itself stays");

            TreeBaker.BakeToPrefab(_tree, path);
            Assert.IsFalse(ImpostorBaker.HasImpostor(AssetDatabase.LoadAssetAtPath<GameObject>(path)), "a plain re-bake doesn't bring it back");
        }

        static Material MakeMaterial(string name, Color colour)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", colour);
            AssetDatabase.CreateAsset(material, $"{Folder}/{name}.mat");
            return material;
        }

        static Texture2D Load(string path)
        {
            var texture = new Texture2D(2, 2);
            texture.LoadImage(File.ReadAllBytes(path));
            return texture;
        }

        static List<string> Ids(GameObject prefab)
        {
            var impostor = prefab.transform.Find(ImpostorBaker.PartName);
            var renderer = impostor.GetComponent<MeshRenderer>();
            var material = renderer.sharedMaterial;
            var objects = new Object[]
            {
                prefab, prefab.GetComponent<LODGroup>(), impostor.gameObject, renderer, impostor.GetComponent<MeshFilter>().sharedMesh,
                material, material.GetTexture("_ImpostorAlbedo"), material.GetTexture("_ImpostorNormals"),
            };
            return objects.Select(o => AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out var guid, out long id) ? $"{guid}:{id}" : "none").ToList();
        }
    }
}
