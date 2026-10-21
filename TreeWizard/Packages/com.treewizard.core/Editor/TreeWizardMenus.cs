using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TreeWizard.Editor
{
    public static class TreeWizardMenus
    {
        public const string PackageRoot = "Packages/com.treewizard.core";
        public const string DefaultPreset = PackageRoot + "/Presets/Oak/Oak.asset";

        [MenuItem("GameObject/3D Object/TreeWizard Tree", false, 20)]
        static void CreateTree(MenuCommand command)
        {
            var go = ObjectFactory.CreateGameObject("Tree", typeof(TreeWizardTree)); // registers undo
            var tree = go.GetComponent<TreeWizardTree>();
            var preset = AssetDatabase.LoadAssetAtPath<TreePreset>(DefaultPreset);
            if (preset != null)
                tree.ApplyPreset(preset);

            ObjectFactory.PlaceGameObject(go, command.context as GameObject);
            tree.Rebuild();
            Selection.activeGameObject = go;
        }

        [MenuItem("Assets/TreeWizard/Bake Impostor", false, 1100)]
        static void BakeImpostor()
        {
            foreach (var path in SelectedPrefabs())
                ImpostorBaker.Bake(path);
        }

        [MenuItem("Assets/TreeWizard/Remove Impostor", false, 1101)]
        static void RemoveImpostor()
        {
            foreach (var path in SelectedPrefabs())
                ImpostorBaker.Remove(path);
        }

        [MenuItem("Assets/TreeWizard/Bake Impostor", true)]
        [MenuItem("Assets/TreeWizard/Remove Impostor", true)]
        static bool PrefabsSelected() => SelectedPrefabs().Any();

        static IEnumerable<string> SelectedPrefabs() =>
            Selection.GetFiltered<GameObject>(SelectionMode.Assets).Select(AssetDatabase.GetAssetPath).Where(p => p.EndsWith(".prefab"));

        [MenuItem("GameObject/TreeWizard/Wind", false, 21)]
        static void CreateWind(MenuCommand command)
        {
            var go = ObjectFactory.CreateGameObject("Wind", typeof(TreeWizardWind));
            ObjectFactory.PlaceGameObject(go, command.context as GameObject);
            go.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            Selection.activeGameObject = go;
        }
    }
}
