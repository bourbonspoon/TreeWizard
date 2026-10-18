using System;
using System.Collections;
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
        [SerializeField] TreeWizardWind wind;
        [SerializeField] List<TreePreset> presets = new List<TreePreset>();

        const string RecentFoldersKey = "TreeWizard.RecentFolders";
        const string ZoomKey = "TreeWizard.Zoom";
        static readonly float[] ZoomSteps = { 0.75f, 0.9f, 1f, 1.1f, 1.25f, 1.5f, 1.75f, 2f };
        static FileFilter DesignFilter => new FileFilter("Designs", TreeDesignFile.Extension);
        static FileFilter FbxFilter => new FileFilter("FBX", ".fbx");

        TreeSettingsPanel _settings;
        DropdownField _preset;
        Label _stats;
        Label _status;
        FileBrowser _browser;
        string _folder;
        string _designName = "Tree";
        float _statusUntil;
        int _shownTriangles = -1;
        PanelSettings _panel;
        float _zoom = 1f;
        float _nextScaleCheck;

        void OnEnable()
        {
            _folder = Path.Combine(Places.Documents, "TreeWizard");
            var document = GetComponent<UIDocument>();
            _panel = document.panelSettings = Instantiate(document.panelSettings); // scaled at runtime, so the asset stays untouched
            _panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            _zoom = Mathf.Clamp(PlayerPrefs.GetFloat(ZoomKey, 1f), ZoomSteps[0], ZoomSteps[ZoomSteps.Length - 1]);
            UpdateScale();
            var root = document.rootVisualElement;

            _settings = new TreeSettingsPanel();
            _settings.Changed += tree.MarkDirty;
            root.Q("settings").Add(_settings);

            _preset = root.Q<DropdownField>("preset");
            _preset.choices = presets.Select(p => p.name).ToList();
            _preset.RegisterValueChangedCallback(e => ApplyPreset(_preset.index));

            root.Q<Button>("openButton").clicked += Open;
            root.Q<Button>("saveButton").clicked += Save;
            root.Q<Button>("exportButton").clicked += Export;
            var windSlider = root.Q<Slider>("wind");
            if (wind != null)
            {
                windSlider.SetValueWithoutNotify(wind.strength);
                windSlider.RegisterValueChangedCallback(e => wind.strength = e.newValue);
            }
            else
            {
                windSlider.SetEnabled(false);
            }
            _stats = root.Q<Label>("stats");
            _status = root.Q<Label>("status");

            _browser = new FileBrowser();
            _browser.SetRecentFolders(PlayerPrefs.GetString(RecentFoldersKey, "").Split('\n'));
            _browser.RecentFoldersChanged += () => PlayerPrefs.SetString(RecentFoldersKey, string.Join("\n", _browser.RecentFolders));
            root.Add(_browser);

            if (presets.Count > 0)
                _preset.index = 0; // applies it through the callback
            else
                _settings.Definition = tree.definition;
        }

        void Start()
        {
            FrameTree();
            var smokeTest = Arg("-smokeTest");
            if (smokeTest != null)
                StartCoroutine(SmokeTest(smokeTest));
        }

        /// <summary>
        /// Automated check for builds: TreeWizard -smokeTest &lt;folder&gt; screenshots each preset, saves a
        /// design and exports FBX into the folder, screenshots the file browser, then quits with exit code 0, or 1 on any error.
        /// </summary>
        IEnumerator SmokeTest(string folder)
        {
            Directory.CreateDirectory(folder);
            var failed = false;
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    failed = true;
            };

            Debug.Log($"[SmokeTest] UI scale {_panel.scale} (display {DisplayScale.System}, zoom {_zoom}), Screen.dpi {Screen.dpi}, window {Screen.width}x{Screen.height}");
            for (var i = 0; i < presets.Count; i++)
            {
                _preset.index = i;
                yield return new WaitForSeconds(2f);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"{presets[i].name}.png"));
                yield return new WaitForSeconds(0.5f); // the capture happens at the end of a frame
                try
                {
                    TreeDesignFile.Save(Path.Combine(folder, presets[i].name + TreeDesignFile.Extension), _preset.value, tree.definition);
                    TreeFbxExporter.Export(tree, Path.Combine(folder, presets[i].name + ".fbx"));
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            _browser.SaveFile("Save Design", folder, "Smoke" + TreeDesignFile.Extension, new[] { DesignFilter }, _ => { });
            yield return new WaitForSeconds(1f);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "FileBrowser.png"));
            yield return new WaitForSeconds(0.5f);
            _browser.Cancel();

            Debug.Log(failed ? "[SmokeTest] FAILED" : "[SmokeTest] passed");
            Application.Quit(failed ? 1 : 0);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

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
            if (Time.unscaledTime > _nextScaleCheck)
                UpdateScale(); // the window can move to a screen with a different scale
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.fKey.wasPressedThisFrame && !_browser.IsOpen && !IsTyping())
                FrameTree();
            if (keyboard.leftCommandKey.isPressed || keyboard.rightCommandKey.isPressed || keyboard.ctrlKey.isPressed)
            {
                if (keyboard.equalsKey.wasPressedThisFrame || keyboard.numpadPlusKey.wasPressedThisFrame)
                    Zoom(1);
                else if (keyboard.minusKey.wasPressedThisFrame || keyboard.numpadMinusKey.wasPressedThisFrame)
                    Zoom(-1);
                else if (keyboard.digit0Key.wasPressedThisFrame || keyboard.numpad0Key.wasPressedThisFrame)
                    Zoom(0);
            }
        }

        // Cmd/Ctrl + and - step through ZoomSteps, 0 goes back to 100%.
        void Zoom(int direction)
        {
            var index = Array.FindIndex(ZoomSteps, z => z >= _zoom - 0.001f);
            if (index < 0)
                index = ZoomSteps.Length - 1;
            _zoom = direction == 0 ? 1f : ZoomSteps[Mathf.Clamp(index + direction, 0, ZoomSteps.Length - 1)];
            PlayerPrefs.SetFloat(ZoomKey, _zoom);
            UpdateScale();
            ShowStatus($"UI size {_zoom:P0}");
        }

        void UpdateScale()
        {
            _nextScaleCheck = Time.unscaledTime + 0.5f;
            var scale = DisplayScale.System * _zoom;
            if (!Mathf.Approximately(_panel.scale, scale))
                _panel.scale = scale;
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
            _browser.SaveFile("Save Design", DefaultFolder(), _designName + TreeDesignFile.Extension, new[] { DesignFilter }, path =>
            {
                TreeDesignFile.Save(path, _preset.value, tree.definition);
                Remember(path);
                ShowStatus($"Saved {Path.GetFileName(path)}");
            });

        void Open() =>
            _browser.OpenFile("Open Design", DefaultFolder(), new[] { DesignFilter }, path =>
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
            _browser.SaveFile("Export FBX", DefaultFolder(), _designName + ".fbx", new[] { FbxFilter }, path =>
            {
                TreeFbxExporter.Export(tree, path);
                _folder = Path.GetDirectoryName(path);
                ShowStatus($"Exported {Path.GetFileName(path)} to {_folder}");
            });

        // Documents/TreeWizard is created on first use; after that, wherever the person last saved or opened.
        string DefaultFolder()
        {
            try
            {
                Directory.CreateDirectory(_folder);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // the browser falls back to the nearest folder that exists
            }
            return _folder;
        }

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
