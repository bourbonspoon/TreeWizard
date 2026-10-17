using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TreeWizard.Editor
{
    /// <summary>Saves generated trees as ordinary assets that don't need TreeWizard at runtime.</summary>
    public static class TreeBaker
    {
        /// <summary>
        /// Writes a prefab with the tree's mesh embedded and its materials assigned. Baking over an
        /// existing prefab updates its mesh in place, so instances already in scenes pick up the change.
        /// </summary>
        public static GameObject BakeToPrefab(TreeWizardTree tree, string prefabPath)
        {
            if (tree.Mesh == null)
                tree.Rebuild();

            var name = Path.GetFileNameWithoutExtension(prefabPath);
            var materials = tree.GetComponent<MeshRenderer>().sharedMaterials;

            // Re-bake: edit the existing prefab rather than replacing the file. Replacing it gives the
            // mesh a new identity, which breaks anything that references it.
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var existing = AssetDatabase.LoadAllAssetsAtPath(prefabPath).OfType<Mesh>().FirstOrDefault();
            if (prefab != null && existing != null && prefab.TryGetComponent<MeshFilter>(out var filter)
                && prefab.TryGetComponent<MeshRenderer>(out var renderer))
            {
                EditorUtility.CopySerialized(tree.Mesh, existing);
                existing.name = name;
                existing.hideFlags = HideFlags.None;
                filter.sharedMesh = existing;
                renderer.sharedMaterials = materials;
                EditorUtility.SetDirty(existing);
                PrefabUtility.SavePrefabAsset(prefab);
                return prefab;
            }

            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            try
            {
                go.GetComponent<MeshRenderer>().sharedMaterials = materials;
                prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }

            // A mesh can only be embedded once the prefab file exists.
            var mesh = Object.Instantiate(tree.Mesh);
            mesh.name = name;
            mesh.hideFlags = HideFlags.None;
            AssetDatabase.AddObjectToAsset(mesh, prefab);
            prefab.GetComponent<MeshFilter>().sharedMesh = mesh;
            PrefabUtility.SavePrefabAsset(prefab);
            return prefab;
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
