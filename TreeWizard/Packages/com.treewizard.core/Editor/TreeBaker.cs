using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TreeWizard.Editor
{
    /// <summary>Saves generated trees as ordinary assets that don't need TreeWizard at runtime.</summary>
    public static class TreeBaker
    {
        public const string BarkPart = "Bark";
        public const string LeavesPart = "Leaves";
        public const string MeshExtension = ".mesh";

        /// <summary>
        /// Writes a prefab whose root holds two children, <see cref="BarkPart"/> and <see cref="LeavesPart"/>,
        /// each with its own mesh, so tools such as Mesh LOD generation can treat the bark and the leaf cards
        /// differently. The meshes are saved beside the prefab as Name_Bark.mesh and Name_Leaves.mesh, so they
        /// can go in Git LFS (*.mesh) while the prefab stays a small text file.
        /// Baking over an existing prefab updates it in place: the prefab, its root, its parts and their meshes
        /// keep their IDs, so anything referencing them keeps working. A prefab from an older bake, with one
        /// mesh embedded on the root, is converted, keeping the root's IDs.
        /// </summary>
        public static GameObject BakeToPrefab(TreeWizardTree tree, string prefabPath)
        {
            if (tree.Mesh == null)
                tree.Rebuild();

            var name = Path.GetFileNameWithoutExtension(prefabPath);
            var folder = Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            var materials = tree.GetComponent<MeshRenderer>().sharedMaterials;
            var bark = SaveMesh(Submesh(tree.Mesh, MeshData.BarkSubmesh), $"{folder}/{name}_{BarkPart}{MeshExtension}");
            var leaves = SaveMesh(Submesh(tree.Mesh, MeshData.LeafSubmesh), $"{folder}/{name}_{LeavesPart}{MeshExtension}");

            // Edit the existing prefab's contents rather than replacing the file. Replacing it gives every
            // object a new identity, which breaks anything that references them.
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
            var root = existing ? PrefabUtility.LoadPrefabContents(prefabPath) : new GameObject(name);
            GameObject prefab;
            try
            {
                // Older bakes kept one two-material mesh on the root.
                if (root.TryGetComponent<MeshRenderer>(out var oldRenderer))
                    Object.DestroyImmediate(oldRenderer);
                if (root.TryGetComponent<MeshFilter>(out var oldFilter))
                    Object.DestroyImmediate(oldFilter);

                SetPart(root, BarkPart, bark, materials[MeshData.BarkSubmesh]);
                SetPart(root, LeavesPart, leaves, materials[MeshData.LeafSubmesh]);
                prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                if (existing)
                    PrefabUtility.UnloadPrefabContents(root);
                else
                    Object.DestroyImmediate(root);
            }

            // The mesh an older bake embedded in the prefab is no longer used.
            var embedded = AssetDatabase.LoadAllAssetsAtPath(prefabPath).OfType<Mesh>().ToList();
            foreach (var mesh in embedded)
            {
                AssetDatabase.RemoveObjectFromAsset(mesh);
                Object.DestroyImmediate(mesh, true);
            }
            if (embedded.Count > 0)
                AssetDatabase.SaveAssets();
            return prefab;
        }

        // A child holding one mesh. It takes the root's layer, tag and static flags, so settings made on the
        // root (as a game's bake script might) carry over to the renderers on every bake.
        static void SetPart(GameObject root, string partName, Mesh mesh, Material material)
        {
            var part = root.transform.Find(partName)?.gameObject;
            if (part == null)
            {
                part = new GameObject(partName, typeof(MeshFilter), typeof(MeshRenderer));
                part.transform.SetParent(root.transform, false);
            }
            part.layer = root.layer;
            part.tag = root.tag;
            GameObjectUtility.SetStaticEditorFlags(part, GameObjectUtility.GetStaticEditorFlags(root));
            if (!part.TryGetComponent<MeshFilter>(out var filter))
                filter = part.AddComponent<MeshFilter>();
            if (!part.TryGetComponent<MeshRenderer>(out var renderer))
                renderer = part.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = material;
        }

        // Writes the mesh to its own asset file. An existing file is overwritten in place, keeping its GUID.
        static Mesh SaveMesh(Mesh mesh, string path)
        {
            mesh.name = Path.GetFileNameWithoutExtension(path);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            existing.name = Path.GetFileNameWithoutExtension(path);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssetIfDirty(existing);
            return existing;
        }

        /// <summary>One submesh of <paramref name="source"/> as a mesh of its own, holding only the vertices it uses.</summary>
        public static Mesh Submesh(Mesh source, int submesh)
        {
            var indices = source.GetTriangles(submesh);
            var vertices = source.vertices;
            var normals = source.normals;
            var tangents = source.tangents;
            var uvs = new List<Vector2>();
            source.GetUVs(0, uvs);

            var remap = new Dictionary<int, int>();
            var outVertices = new List<Vector3>();
            var outNormals = new List<Vector3>();
            var outTangents = new List<Vector4>();
            var outUvs = new List<Vector2>();
            var triangles = new int[indices.Length];
            for (var i = 0; i < indices.Length; i++)
            {
                var index = indices[i];
                if (!remap.TryGetValue(index, out var mapped))
                {
                    mapped = remap[index] = outVertices.Count;
                    outVertices.Add(vertices[index]);
                    outNormals.Add(normals[index]);
                    if (tangents.Length > 0)
                        outTangents.Add(tangents[index]);
                    if (uvs.Count > 0)
                        outUvs.Add(uvs[index]);
                }
                triangles[i] = mapped;
            }

            var mesh = new Mesh
            {
                indexFormat = outVertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            mesh.SetVertices(outVertices);
            mesh.SetNormals(outNormals);
            mesh.SetTangents(outTangents);
            mesh.SetUVs(0, outUvs);
            mesh.SetTriangles(triangles, 0);
            return mesh;
        }

        /// <summary>Saves the tree's current settings, materials and leaf atlas as a <see cref="TreePreset"/>.</summary>
        public static TreePreset SaveAsPreset(TreeWizardTree tree, string assetPath)
        {
            var preset = AssetDatabase.LoadAssetAtPath<TreePreset>(assetPath);
            if (preset == null)
            {
                preset = ScriptableObject.CreateInstance<TreePreset>();
                AssetDatabase.CreateAsset(preset, assetPath);
            }
            preset.definition = tree.definition.Clone();
            preset.barkMaterial = tree.barkMaterial;
            preset.leafMaterial = tree.leafMaterial;
            preset.leafAtlas = tree.leafAtlas;
            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssets();
            return preset;
        }
    }
}
