using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using TreeWizard.UI;

namespace TreeWizard.Editor
{
    [CustomEditor(typeof(TreeWizardTree))]
    public class TreeWizardTreeEditor : UnityEditor.Editor
    {
        SerializedProperty _definition;
        VisualElement _levels;
        LeafMixPanel _leafMix;
        Label _stats;

        TreeWizardTree Tree => (TreeWizardTree)target;

        public override VisualElement CreateInspectorGUI()
        {
            _definition = serializedObject.FindProperty(nameof(TreeWizardTree.definition));
            var root = new VisualElement();

            root.Add(PresetRow());
            root.Add(SeedRow());
            root.Add(Section("Trunk", _definition.FindPropertyRelative(nameof(TreeDefinition.trunk)), true));
            root.Add(Section("Roots", _definition.FindPropertyRelative(nameof(TreeDefinition.roots)), false));
            root.Add(BranchLevels());
            root.Add(Section("Leaves", _definition.FindPropertyRelative(nameof(TreeDefinition.leaves)), true));
            root.Add(LeafMix());
            root.Add(Section("Mesh", _definition.FindPropertyRelative(nameof(TreeDefinition.mesh)), false));

            var materials = Foldout("Materials", true);
            materials.Add(new PropertyField(serializedObject.FindProperty(nameof(TreeWizardTree.barkMaterial))));
            materials.Add(new PropertyField(serializedObject.FindProperty(nameof(TreeWizardTree.leafMaterial))));
            materials.Add(new PropertyField(serializedObject.FindProperty(nameof(TreeWizardTree.leafAtlas))));
            root.Add(materials);

            _stats = new Label { style = { marginTop = 6, unityFontStyleAndWeight = FontStyle.Italic } };
            root.Add(_stats);
            root.schedule.Execute(UpdateStats).Every(250);

            var actions = Row();
            actions.style.marginTop = 6;
            actions.Add(Grow(new Button(SaveAsPreset) { text = "Save as Preset…" }));
            actions.Add(Grow(new Button(BakeToPrefab) { text = "Bake to Prefab…" }));
            root.Add(actions);
            var impostor = new Toggle("Bake Impostor")
            {
                value = EditorPrefs.GetBool(BakeImpostorKey, false),
                tooltip = "Also bake a flat, camera-facing stand-in for the distance, switched in by a LODGroup. "
                    + "A prefab that already has one is always re-captured, keeping its settings.",
            };
            impostor.RegisterValueChangedCallback(e => EditorPrefs.SetBool(BakeImpostorKey, e.newValue));
            root.Add(impostor);
            root.Add(new Button(ExportFbx) { text = "Export FBX…", tooltip = "Write the tree as FBX, with its textures as PNGs beside it" });

            // Undo or an outside edit can change the number of branch levels.
            root.TrackPropertyValue(_definition.FindPropertyRelative(nameof(TreeDefinition.branchLevels) + ".Array.size"), _ => RebuildLevels());
            return root;
        }

        // Thumbnails and weights for the atlas's leaves. Edits go through Undo; undo and atlas changes refresh it.
        VisualElement LeafMix()
        {
            var foldout = Foldout("Leaf Mix", false);
            if (targets.Length > 1)
            {
                foldout.Add(new Label("Select one tree to edit its leaf mix."));
                return foldout;
            }
            _leafMix = new LeafMixPanel();
            _leafMix.Changing += () => Undo.RecordObject(Tree, "Change Leaf Mix");
            _leafMix.Changed += () =>
            {
                EditorUtility.SetDirty(Tree);
                Tree.MarkDirty();
                serializedObject.Update();
            };
            _leafMix.Bind(Tree.definition.leaves, Tree.leafAtlas);
            foldout.Add(_leafMix);
            foldout.TrackPropertyValue(serializedObject.FindProperty(nameof(TreeWizardTree.leafAtlas)),
                _ => _leafMix.Bind(Tree.definition.leaves, Tree.leafAtlas));
            foldout.TrackPropertyValue(_definition.FindPropertyRelative(nameof(TreeDefinition.leaves)).FindPropertyRelative(nameof(LeafSettings.cellWeights)),
                _ => _leafMix.Bind(Tree.definition.leaves, Tree.leafAtlas)); // undo, or a preset applied
            return foldout;
        }

        VisualElement PresetRow()
        {
            var presets = AssetDatabase.FindAssets("t:" + nameof(TreePreset))
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<TreePreset>)
                .Where(p => p != null)
                .OrderBy(p => p.name)
                .ToList();

            var row = Row();
            var dropdown = Grow(new DropdownField("Preset", presets.Select(p => p.name).ToList(), presets.Count > 0 ? 0 : -1));
            row.Add(dropdown);
            row.Add(new Button(() =>
            {
                if (dropdown.index < 0)
                    return;
                foreach (var t in targets.Cast<TreeWizardTree>())
                {
                    Undo.RecordObject(t, "Apply Tree Preset");
                    t.ApplyPreset(presets[dropdown.index]);
                }
                serializedObject.Update();
                RebuildLevels();
            }) { text = "Apply", style = { flexShrink = 0 } });
            row.SetEnabled(presets.Count > 0);
            return row;
        }

        VisualElement SeedRow()
        {
            var seed = _definition.FindPropertyRelative(nameof(TreeDefinition.seed));
            var row = Row();
            row.Add(Grow(new PropertyField(seed)));
            row.Add(new Button(() =>
            {
                seed.intValue = Random.Range(1, 1_000_000);
                serializedObject.ApplyModifiedProperties();
            }) { text = "Randomize", tooltip = "Pick a new random seed", style = { flexShrink = 0 } });
            return row;
        }

        VisualElement BranchLevels()
        {
            var foldout = Foldout("Branches", true);
            _levels = new VisualElement();
            foldout.Add(_levels);

            var buttons = Row();
            buttons.Add(Grow(new Button(() => ResizeLevels(+1)) { text = "Add Level", name = "add-level" }));
            buttons.Add(Grow(new Button(() => ResizeLevels(-1)) { text = "Remove Level", name = "remove-level" }));
            foldout.Add(buttons);

            RebuildLevels();
            return foldout;
        }

        void ResizeLevels(int delta)
        {
            var levels = _definition.FindPropertyRelative(nameof(TreeDefinition.branchLevels));
            var size = Mathf.Clamp(levels.arraySize + delta, 0, TreeDefinition.MaxBranchLevels);
            if (size == levels.arraySize)
                return;

            levels.arraySize = size;
            if (delta > 0 && size > 1)
            {
                // Copying the previous level gives an odd tree; start from sensible twig-level values.
                var added = levels.GetArrayElementAtIndex(size - 1);
                added.FindPropertyRelative(nameof(BranchLevelSettings.count)).intValue = 4;
                added.FindPropertyRelative(nameof(BranchLevelSettings.length)).floatValue = 0.35f;
                added.FindPropertyRelative(nameof(BranchLevelSettings.segments)).intValue = 3;
            }
            serializedObject.ApplyModifiedProperties();
            RebuildLevels();
        }

        void RebuildLevels()
        {
            if (_levels == null)
                return;
            serializedObject.Update();
            _levels.Clear();
            var levels = _definition.FindPropertyRelative(nameof(TreeDefinition.branchLevels));
            for (var i = 0; i < levels.arraySize; i++)
            {
                var section = Section($"Level {i + 1}", levels.GetArrayElementAtIndex(i), i == 0);
                section.style.marginLeft = 4;
                _levels.Add(section);
            }
            _levels.Bind(serializedObject);

            var root = _levels.parent;
            root?.Q<Button>("add-level")?.SetEnabled(levels.arraySize < TreeDefinition.MaxBranchLevels);
            root?.Q<Button>("remove-level")?.SetEnabled(levels.arraySize > 0);
        }

        void UpdateStats()
        {
            if (_stats == null || target == null)
                return;
            var mesh = Tree.Mesh;
            if (mesh == null)
            {
                _stats.text = "Not built yet";
                return;
            }
            long bark = mesh.subMeshCount > MeshData.BarkSubmesh ? mesh.GetIndexCount(MeshData.BarkSubmesh) / 3 : 0;
            long leaf = mesh.subMeshCount > MeshData.LeafSubmesh ? mesh.GetIndexCount(MeshData.LeafSubmesh) / 3 : 0;
            _stats.text = $"{bark + leaf:N0} triangles ({bark:N0} bark, {leaf:N0} leaf), {mesh.vertexCount:N0} vertices";
        }

        void SaveAsPreset()
        {
            var path = EditorUtility.SaveFilePanelInProject("Save Tree Preset", Tree.name, "asset", "Save these settings as a reusable preset.");
            if (string.IsNullOrEmpty(path))
                return;
            EditorGUIUtility.PingObject(TreeBaker.SaveAsPreset(Tree, path));
        }

        void BakeToPrefab()
        {
            var path = EditorUtility.SaveFilePanelInProject("Bake Tree to Prefab", Tree.name, "prefab",
                "Save the tree as a static prefab. Baking over an existing prefab updates it in place.");
            if (string.IsNullOrEmpty(path))
                return;
            ImpostorSettings impostor = null;
            if (EditorPrefs.GetBool(BakeImpostorKey, false))
                impostor = ImpostorBaker.ReadSettings(AssetDatabase.LoadAssetAtPath<GameObject>(path)) ?? new ImpostorSettings();
            EditorGUIUtility.PingObject(TreeBaker.BakeToPrefab(Tree, path, impostor));
        }

        void ExportFbx()
        {
            var path = EditorUtility.SaveFilePanel("Export Tree as FBX", EditorPrefs.GetString(LastExportFolderKey, ""), Tree.name, "fbx");
            if (string.IsNullOrEmpty(path))
                return;
            EditorPrefs.SetString(LastExportFolderKey, Path.GetDirectoryName(path));

            TreeFbxExporter.Export(Tree, path);
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (Path.GetFullPath(path).StartsWith(projectRoot))
                AssetDatabase.Refresh();
            else
                EditorUtility.RevealInFinder(path);
        }

        const string LastExportFolderKey = "TreeWizard.LastExportFolder";
        const string BakeImpostorKey = "TreeWizard.BakeImpostor";

        Foldout Section(string title, SerializedProperty property, bool expanded)
        {
            var foldout = Foldout(title, expanded);
            foreach (var child in Children(property))
                foldout.Add(new PropertyField(child));
            return foldout;
        }

        Foldout Foldout(string title, bool expanded) =>
            new Foldout { text = title, value = expanded, viewDataKey = $"TreeWizardTree.{title}" };

        static IEnumerable<SerializedProperty> Children(SerializedProperty property)
        {
            var it = property.Copy();
            var end = property.GetEndProperty();
            if (!it.NextVisible(true))
                yield break;
            while (!SerializedProperty.EqualContents(it, end))
            {
                yield return it.Copy();
                if (!it.NextVisible(false))
                    yield break;
            }
        }

        static VisualElement Row() => new VisualElement { style = { flexDirection = FlexDirection.Row } };

        /// <summary>Takes the row's spare width, and gives it up first when the inspector is narrow.</summary>
        static T Grow<T>(T element) where T : VisualElement
        {
            element.style.flexGrow = 1;
            element.style.flexShrink = 1;
            element.style.minWidth = 0;
            return element;
        }
    }
}
