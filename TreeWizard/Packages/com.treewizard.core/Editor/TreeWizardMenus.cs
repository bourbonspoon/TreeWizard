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
    }
}
