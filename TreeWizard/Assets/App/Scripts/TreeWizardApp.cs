using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TreeWizard.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace TreeWizard.App
{
    /// <summary>
    /// The standalone designer: a settings sidebar driving a live tree, presets, save/open of
    /// designs and FBX export.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class TreeWizardApp : MonoBehaviour
    {
        [SerializeField] TreeWizardTree tree;
        [SerializeField] OrbitCamera orbit;
        [SerializeField] List<TreePreset> presets = new List<TreePreset>();

        TreeSettingsPanel _settings;
        DropdownField _preset;
        Label _stats;
        Label _status;
        FileDialog _dialog;
        string _folder;
        string _designName = "Tree";
        float _statusUntil;
        int _shownTriangles = -1;

        void OnEnable()
        {
            _folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TreeWizard");
            var root = GetComponent<UIDocument>().rootVisualElement;

            _settings = new TreeSettingsPanel();
            _settings.Changed += tree.MarkDirty;
            root.Q("settings").Add(_settings);

            _preset = root.Q<DropdownField>("preset");
            _preset.choices = presets.Select(p => p.name).ToList();
            _preset.RegisterValueChangedCallback(e => ApplyPreset(_preset.index));

            root.Q<Button>("openButton").clicked += Open;
            root.Q<Button>("saveButton").clicked += Save;
            root.Q<Button>("exportButton").clicked += Export;
            _stats = root.Q<Label>("stats");
            _status = root.Q<Label>("status");

            _dialog = new FileDialog();
            root.Add(_dialog);

            if (presets.Count > 0)
                _preset.index = 0; // applies it through the callback
            else
                _settings.Definition = tree.definition;
        }

        void Start() => FrameTree();

        void Update()
        {
            var mesh = tree.Mesh;
            var triangles = mesh != null ? (int)((mesh.GetIndexCount(0) + mesh.GetIndexCount(1)) / 3) : 0;
            if (triangles != _shownTriangles)
            {
                _shownTriangles = triangles;
                _stats.text = $"{triangles:N0} triangles · {(mesh != null ? mesh.vertexCount : 0):N0} vertices";
            }
            if (_statusUntil > 0f && Time.unscaledTime > _statusUntil)
            {
                _status.RemoveFromClassList("status--visible");
                _statusUntil = 0f;
            }
            if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame && !IsTyping())
                FrameTree();
        }

        void ApplyPreset(int index)
        {
            if (index < 0 || index >= presets.Count)
                return;
            tree.ApplyPreset(presets[index]);
            _settings.Definition = tree.definition;
            _designName = presets[index].name;
            tree.Rebuild();
            FrameTree();
        }

        void FrameTree()
        {
            if (orbit != null && tree.TryGetComponent<MeshRenderer>(out var renderer) && tree.Mesh != null)
                orbit.Frame(renderer.bounds);
        }

        void Save() =>
            _dialog.Show(FileDialog.Mode.Save, "Save Design", _folder, _designName + TreeDesignFile.Extension, TreeDesignFile.Extension, path =>
            {
                TreeDesignFile.Save(path, _preset.value, tree.definition);
                Remember(path);
                ShowStatus($"Saved {Path.GetFileName(path)}");
            });

        void Open() =>
            _dialog.Show(FileDialog.Mode.Open, "Open Design", _folder, "", TreeDesignFile.Extension, path =>
            {
                var design = TreeDesignFile.Load(path);
                var index = presets.FindIndex(p => p.name == design.preset);
                if (index >= 0)
                    _preset.SetValueWithoutNotify(presets[index].name);
                var preset = index >= 0 ? presets[index] : presets.FirstOrDefault();
                if (preset != null)
                    tree.ApplyPreset(preset); // materials and leaf atlas
                tree.definition = design.definition;
                tree.MarkDirty();
                _settings.Definition = tree.definition;
                Remember(path);
                ShowStatus($"Opened {Path.GetFileName(path)}");
            });

        void Export() =>
            _dialog.Show(FileDialog.Mode.Save, "Export FBX", _folder, _designName + ".fbx", ".fbx", path =>
            {
                TreeFbxExporter.Export(tree, path);
                _folder = Path.GetDirectoryName(path);
                ShowStatus($"Exported {Path.GetFileName(path)} to {_folder}");
            });

        void Remember(string path)
        {
            _folder = Path.GetDirectoryName(path);
            _designName = Path.GetFileNameWithoutExtension(path);
        }

        void ShowStatus(string message)
        {
            _status.text = message;
            _status.AddToClassList("status--visible");
            _statusUntil = Time.unscaledTime + 5f;
        }

        /// <summary>True while a text or number field has focus, so typing doesn't trigger shortcuts.</summary>
        bool IsTyping()
        {
            for (var e = GetComponent<UIDocument>().rootVisualElement.focusController?.focusedElement as VisualElement; e != null; e = e.parent)
            {
                if (e.ClassListContains(TextInputBaseField<string>.ussClassName)) // shared by text and number fields
                    return true;
            }
            return false;
        }
    }
}
