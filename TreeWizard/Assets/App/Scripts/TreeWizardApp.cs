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
        static FileFilter ImageFilter => new FileFilter("Images", TextureImport.Extensions);

        TreeSettingsPanel _settings;
        DropdownField _preset;
        Label _stats;
        Label _status;
        FileBrowser _browser;
        CustomTextures _textures;
        Label _barkValue, _barkNormalValue, _leavesValue;
        Button _barkNormalButton;
        string _folder;
        string _textureFolder;
        string _designName = "Tree";
        float _statusUntil;
        int _shownTriangles = -1;
        PanelSettings _panel;
        float _zoom = 1f;
        float _nextScaleCheck;
        readonly SnapshotHistory _history = new SnapshotHistory();
        string _savedState;      // the design as last saved or opened, to tell whether there are unsaved changes
        int _quiet;              // while above 0, changes are being restored or loaded rather than made: not recorded
        bool _allowQuit;
        ConfirmDialog _dialog;
        Label _designLabel;
        Button _undoButton, _redoButton;
        ScrollView _settingsScroll;

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
            _settings.Changed += () =>
            {
                tree.MarkDirty();
                RecordChange(true); // sliders send a change per step of a drag; a drag is one undo step
            };
            root.Q("settings").Add(_settings);
            _settingsScroll = root.Q<ScrollView>("settingsScroll");

            _designLabel = root.Q<Label>("designLabel");
            _undoButton = root.Q<Button>("undoButton");
            _redoButton = root.Q<Button>("redoButton");
            _undoButton.clicked += Undo;
            _redoButton.clicked += Redo;

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

            _textures = new CustomTextures(tree);
            _textures.Changed += () =>
            {
                ShowTextures();
                RecordChange(false);
            };
            _barkValue = root.Q<Label>("barkValue");
            _barkNormalValue = root.Q<Label>("barkNormalValue");
            _leavesValue = root.Q<Label>("leavesValue");
            _barkNormalButton = root.Q<Button>("barkNormalButton");
            root.Q<Button>("barkButton").clicked += ChooseBark;
            _barkNormalButton.clicked += ChooseBarkNormal;
            root.Q<Button>("leavesButton").clicked += ChooseLeaves;
            root.Q<Button>("presetTexturesButton").clicked += () => _textures.Clear();
            ShowTextures();

            _browser = new FileBrowser();
            _browser.SetRecentFolders(PlayerPrefs.GetString(RecentFoldersKey, "").Split('\n'));
            _browser.RecentFoldersChanged += () => PlayerPrefs.SetString(RecentFoldersKey, string.Join("\n", _browser.RecentFolders));
            root.Add(_browser);
            _dialog = new ConfirmDialog();
            root.Add(_dialog);

            if (presets.Count > 0)
                _preset.index = 0; // applies it through the callback
            else
                _settings.Definition = tree.definition;
            ResetHistory();
            Application.wantsToQuit += ConfirmQuit;
        }

        void OnDisable()
        {
            Application.wantsToQuit -= ConfirmQuit;
            _textures?.Dispose();
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
        /// design and exports FBX into the folder, does the same with generated custom textures, screenshots the file browser,
        /// then quits with exit code 0, or 1 on any error.
        /// </summary>
        IEnumerator SmokeTest(string folder)
        {
            folder = Path.GetFullPath(folder);
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
                    TreeDesignFile.Save(Path.Combine(folder, presets[i].name + TreeDesignFile.Extension), _preset.value, tree.definition, _textures.Files);
                    TreeFbxExporter.Export(tree, Path.Combine(folder, presets[i].name + ".fbx"));
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            var barkPath = Path.Combine(folder, "SmokeBark.png");
            var sheetPath = Path.Combine(folder, "SmokeLeaves.png");
            WriteSmokeImages(barkPath, sheetPath);
            yield return Wait(_textures.SetBark(barkPath));
            yield return Wait(_textures.SetLeaves(new[] { sheetPath }));
            ShowTextures();
            GetComponent<UIDocument>().rootVisualElement.Q<Foldout>("textures").value = true;
            yield return new WaitForSeconds(1f);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "CustomTextures.png"));
            yield return new WaitForSeconds(0.5f);
            try
            {
                var design = Path.Combine(folder, "Custom" + TreeDesignFile.Extension);
                TreeDesignFile.Save(design, _preset.value, tree.definition, _textures.Files);
                if (!File.ReadAllText(design).Contains("\"bark\": \"SmokeBark.png\""))
                    Debug.LogError("[SmokeTest] the bark path wasn't stored relative to the design");
                var loaded = TreeDesignFile.Load(design).textures;
                if (loaded.bark != barkPath || loaded.leafSheets.SingleOrDefault() != sheetPath)
                    Debug.LogError($"[SmokeTest] texture paths didn't survive saving: {loaded.bark}, {string.Join(", ", loaded.leafSheets)}");
                TreeFbxExporter.Export(tree, Path.Combine(folder, "Custom.fbx"));
                if (!File.Exists(Path.Combine(folder, "Custom_Bark_Normal.png")))
                    Debug.LogError("[SmokeTest] no bark normal map was exported");
                Debug.Log($"[SmokeTest] custom textures: {tree.leafAtlas.cells.Count} leaves, {tree.barkMaterial.mainTexture.width}px bark");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            _textures.Clear();

            // Undo and redo step through changes, and the design reports unsaved changes until saved.
            ResetHistory();
            var height = tree.definition.trunk.height;
            tree.definition.trunk.height = height + 2f;
            RecordChange(false);
            if (!IsModified || !_designLabel.text.EndsWith("\u2022"))
                Debug.LogError("[SmokeTest] a change didn't mark the design as modified");
            Undo();
            yield return null;
            if (!Mathf.Approximately(tree.definition.trunk.height, height) || IsModified)
                Debug.LogError($"[SmokeTest] undo gave height {tree.definition.trunk.height}, expected {height}");
            Redo();
            yield return null;
            if (!Mathf.Approximately(tree.definition.trunk.height, height + 2f))
                Debug.LogError($"[SmokeTest] redo gave height {tree.definition.trunk.height}, expected {height + 2f}");
            ConfirmDiscard("quitting", () => { });
            yield return new WaitForSeconds(0.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "Unsaved.png"));
            yield return new WaitForSeconds(0.5f);
            _dialog.Cancel();
            Undo();

            _browser.SaveFile("Save Design", folder, "Smoke" + TreeDesignFile.Extension, new[] { DesignFilter }, _ => { });
            yield return new WaitForSeconds(1f);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "FileBrowser.png"));
            yield return new WaitForSeconds(0.5f);
            _browser.Cancel();

            Debug.Log(failed ? "[SmokeTest] FAILED" : "[SmokeTest] passed");
            _allowQuit = true;
            Application.Quit(failed ? 1 : 0);
        }

        static IEnumerator Wait(System.Threading.Tasks.Task task)
        {
            while (!task.IsCompleted)
                yield return null;
            if (task.IsFaulted)
                Debug.LogException(task.Exception);
        }

        // A stripy bark tile, and a sheet of two green leaves and an orange one with their stems pointing down.
        static void WriteSmokeImages(string barkPath, string sheetPath)
        {
            var bark = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            var pixels = new Color32[256 * 256];
            for (var y = 0; y < 256; y++)
            for (var x = 0; x < 256; x++)
            {
                var v = (byte)(100 + 60 * Mathf.Sin(x * 0.2f + Mathf.Sin(y * 0.05f) * 3f));
                pixels[y * 256 + x] = new Color32(v, (byte)(v * 0.8f), (byte)(v * 0.6f), 255);
            }
            bark.SetPixels32(pixels);
            File.WriteAllBytes(barkPath, bark.EncodeToPNG());
            Destroy(bark);

            const int w = 384, h = 256;
            var sheet = new Texture2D(w, h, TextureFormat.RGBA32, false);
            pixels = new Color32[w * h];
            var colours = new[] { new Color32(60, 140, 40, 255), new Color32(80, 150, 50, 255), new Color32(200, 110, 30, 255) };
            for (var leaf = 0; leaf < 3; leaf++)
            {
                var cx = 64 + leaf * 128;
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    float dx = (x - cx) / 44f, dy = (y - 150) / 90f;
                    var stem = Mathf.Abs(x - cx) <= 2 && y >= 20 && y < 70;
                    if (dx * dx + dy * dy <= 1f || stem)
                        pixels[y * w + x] = colours[leaf];
                }
            }
            sheet.SetPixels32(pixels);
            File.WriteAllBytes(sheetPath, sheet.EncodeToPNG());
            Destroy(sheet);
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
            if (keyboard.fKey.wasPressedThisFrame && !_browser.IsOpen && !_dialog.IsOpen && !IsTyping())
                FrameTree();
            if (keyboard.leftCommandKey.isPressed || keyboard.rightCommandKey.isPressed || keyboard.ctrlKey.isPressed)
            {
                if (!_browser.IsOpen && !_dialog.IsOpen)
                {
                    if (keyboard.zKey.wasPressedThisFrame)
                    {
                        if (keyboard.shiftKey.isPressed)
                            Redo();
                        else
                            Undo();
                    }
                    else if (keyboard.yKey.wasPressedThisFrame)
                        Redo();
                    else if (keyboard.sKey.wasPressedThisFrame)
                        Save();
                    else if (keyboard.oKey.wasPressedThisFrame)
                        Open();
                }
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
            _textures.Apply(presets[index]); // keeps the person's own textures over the new preset, and rebuilds
            _settings.Definition = tree.definition;
            _designName = presets[index].name;
            FrameTree();
            RecordChange(false);
        }

        void FrameTree()
        {
            if (orbit != null && tree.TryGetComponent<MeshRenderer>(out var renderer) && tree.Mesh != null)
                orbit.Frame(renderer.bounds);
        }

        void Save() => Save(null);

        /// <param name="then">Runs after a successful save, e.g. the open or quit that asked for it.</param>
        void Save(Action then) =>
            _browser.SaveFile("Save Design", DefaultFolder(), _designName + TreeDesignFile.Extension, new[] { DesignFilter }, path =>
            {
                TreeDesignFile.Save(path, _preset.value, tree.definition, _textures.Files);
                Remember(path);
                _savedState = Snapshot();
                ShowDesignState();
                ShowStatus($"Saved {Path.GetFileName(path)}");
                then?.Invoke();
            });

        void Open() => ConfirmDiscard("opening another design", () =>
            _browser.OpenFile("Open Design", DefaultFolder(), new[] { DesignFilter }, path =>
            {
                var design = TreeDesignFile.Load(path);
                var index = presets.FindIndex(p => p.name == design.preset);
                if (index >= 0)
                    _preset.SetValueWithoutNotify(presets[index].name);
                Remember(path);
                LoadDesign(index >= 0 ? presets[index] : presets.FirstOrDefault(), design, $"Opened {Path.GetFileName(path)}");
            }));

        // Opening starts a fresh history once the design's own textures have loaded.
        async void LoadDesign(TreePreset preset, TreeDesignFile design, string opened)
        {
            _quiet++;
            try
            {
                ShowDesign(preset, design.definition);
                var missing = await MatchTextures(design.textures);
                ShowStatus(missing.Count == 0 ? opened : $"{opened}, but couldn't find {string.Join(", ", missing)}; using the preset's textures instead", missing.Count == 0 ? 5f : 10f);
            }
            catch (Exception e)
            {
                ShowStatus($"{opened}, but its textures couldn't be loaded: {e.Message}", 10f);
            }
            finally
            {
                _quiet--;
                ResetHistory();
            }
        }

        // The preset's materials under the given settings, keeping the custom textures already loaded.
        void ShowDesign(TreePreset preset, TreeDefinition definition)
        {
            if (preset != null)
                tree.ApplyPreset(preset);
            tree.definition = definition;
            _textures.Apply(preset);
            tree.MarkDirty();
            var scroll = _settingsScroll.scrollOffset;
            _settings.Definition = tree.definition;
            _settingsScroll.schedule.Execute(() => _settingsScroll.scrollOffset = scroll); // after the rebuilt panel's layout
        }

        /// <summary>
        /// Loads or clears custom textures until they match <paramref name="files"/>. Files that have gone missing fall back
        /// to the preset's; their names are returned.
        /// </summary>
        async System.Threading.Tasks.Task<List<string>> MatchTextures(TreeDesignFile.TextureFiles files)
        {
            var missing = new List<string>();
            var bark = files.bark ?? "";
            var normal = files.barkNormal ?? "";
            if (bark != _textures.BarkPath || normal != _textures.BarkNormalPath)
            {
                if (bark.Length > 0 && !File.Exists(bark))
                {
                    missing.Add(Path.GetFileName(bark));
                    bark = "";
                }
                if (normal.Length > 0 && !File.Exists(normal))
                {
                    missing.Add(Path.GetFileName(normal));
                    normal = "";
                }
                if (bark.Length > 0)
                    await _textures.SetBark(bark, normal);
                else if (_textures.HasBark)
                    _textures.ClearBark();
            }

            var sheets = files.leafSheets ?? Array.Empty<string>();
            if (!sheets.SequenceEqual(_textures.LeafSheetPaths))
            {
                var found = sheets.Where(File.Exists).ToList();
                missing.AddRange(sheets.Where(p => !File.Exists(p)).Select(Path.GetFileName));
                if (found.Count > 0)
                    await _textures.SetLeaves(found);
                else if (_textures.HasLeaves)
                    _textures.ClearLeaves();
            }
            return missing;
        }

        /// <summary>The whole design as one string: what undo steps between, and what tells whether it's been saved.</summary>
        string Snapshot() => JsonUtility.ToJson(new TreeDesignFile { preset = _preset.value, definition = tree.definition, textures = _textures.Files });

        bool IsModified => Snapshot() != _savedState;

        void RecordChange(bool mergeable)
        {
            if (_quiet > 0)
                return;
            _history.Record(Snapshot(), Time.unscaledTime, mergeable);
            ShowDesignState();
        }

        void ResetHistory()
        {
            _history.Reset(Snapshot());
            _savedState = _history.Current;
            ShowDesignState();
        }

        void ShowDesignState()
        {
            _designLabel.text = IsModified ? _designName + " \u2022" : _designName;
            _designLabel.tooltip = IsModified ? "Unsaved changes" : "";
            _undoButton.SetEnabled(_history.CanUndo);
            _redoButton.SetEnabled(_history.CanRedo);
        }

        void Undo() => Restore(BlurAnd(_history.Undo));

        void Redo() => Restore(BlurAnd(_history.Redo));

        // A text field being typed in commits its value when it loses focus, so that edit is recorded before stepping.
        string BlurAnd(Func<string> step)
        {
            (GetComponent<UIDocument>().rootVisualElement.focusController?.focusedElement as VisualElement)?.Blur();
            return step();
        }

        async void Restore(string state)
        {
            if (state == null)
                return;
            var design = JsonUtility.FromJson<TreeDesignFile>(state);
            var index = presets.FindIndex(p => p.name == design.preset);
            if (index >= 0)
                _preset.SetValueWithoutNotify(presets[index].name);
            _quiet++;
            try
            {
                ShowDesign(index >= 0 ? presets[index] : null, design.definition);
                ShowDesignState();
                var missing = await MatchTextures(design.textures);
                if (missing.Count > 0)
                    ShowStatus($"Couldn't find {string.Join(", ", missing)}", 8f);
            }
            catch (Exception e)
            {
                ShowStatus($"Couldn't restore the textures: {e.Message}", 8f);
            }
            finally
            {
                _quiet--;
                ShowDesignState();
            }
        }

        /// <summary>Asks whether to save first when there are unsaved changes, then carries on unless cancelled.</summary>
        void ConfirmDiscard(string doing, Action proceed)
        {
            if (!IsModified)
            {
                proceed();
                return;
            }
            _dialog.Show("Unsaved Changes", $"Save changes to {_designName} before {doing}?",
                new ConfirmDialog.Choice("Don't Save", proceed),
                new ConfirmDialog.Choice("Cancel", null, cancel: true),
                new ConfirmDialog.Choice("Save…", () => Save(proceed), primary: true));
        }

        bool ConfirmQuit()
        {
            if (_allowQuit || !IsModified)
                return true;
            ConfirmDiscard("quitting", () =>
            {
                _allowQuit = true;
                Application.Quit();
            });
            return false;
        }

        void ChooseBark() =>
            _browser.OpenFile("Choose Bark Texture", TextureFolder(), new[] { ImageFilter, FileFilter.All }, path =>
            {
                _textureFolder = Path.GetDirectoryName(path);
                RunTextureTask(_textures.SetBark(path), "Making the bark normal map…", () => $"Bark: {Path.GetFileName(path)}");
            });

        void ChooseBarkNormal() =>
            _browser.OpenFile("Choose Bark Normal Map", TextureFolder(), new[] { ImageFilter, FileFilter.All }, path =>
            {
                _textureFolder = Path.GetDirectoryName(path);
                RunTextureTask(_textures.SetBark(_textures.BarkPath, path), "Loading the normal map…", () => $"Bark normal map: {Path.GetFileName(path)}");
            });

        void ChooseLeaves() =>
            _browser.OpenFiles("Choose Leaf Sheets", TextureFolder(), new[] { ImageFilter, FileFilter.All }, paths =>
            {
                _textureFolder = Path.GetDirectoryName(paths[0]);
                var task = _textures.SetLeaves(paths);
                RunTextureTask(task, "Finding the leaves…", () =>
                {
                    var (leaves, opaque) = task.Result;
                    var message = $"{leaves} leaves from {paths.Length} {(paths.Length == 1 ? "sheet" : "sheets")}";
                    return opaque.Length == 0 ? message : $"{message}. No transparent background on {string.Join(", ", opaque)}, so each became one leaf";
                });
            });

        async void RunTextureTask(System.Threading.Tasks.Task task, string working, Func<string> done)
        {
            ShowStatus(working, float.PositiveInfinity);
            try
            {
                await task;
                ShowStatus(done(), 8f);
            }
            catch (Exception e)
            {
                ShowStatus($"Couldn't use that image: {e.Message}", 10f);
            }
        }

        void ShowTextures()
        {
            Show(_barkValue, _textures.HasBark ? Path.GetFileName(_textures.BarkPath) : "Preset", _textures.BarkPath);
            Show(_barkNormalValue,
                !_textures.HasBark ? "Preset"
                : string.IsNullOrEmpty(_textures.BarkNormalPath) ? "Generated"
                : Path.GetFileName(_textures.BarkNormalPath), _textures.BarkNormalPath);
            _barkNormalButton.SetEnabled(_textures.HasBark);
            var sheets = _textures.LeafSheetPaths;
            Show(_leavesValue, !_textures.HasLeaves ? "Preset" : sheets.Count == 1 ? Path.GetFileName(sheets[0]) : $"{sheets.Count} sheets",
                string.Join("\n", sheets));

            static void Show(Label label, string text, string tooltip)
            {
                label.text = text;
                label.tooltip = tooltip;
            }
        }

        // The folder images last came from, else where designs are kept.
        string TextureFolder() => _textureFolder ?? DefaultFolder();

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

        void ShowStatus(string message, float seconds = 5f)
        {
            _status.text = message;
            _status.AddToClassList("status--visible");
            _statusUntil = Time.unscaledTime + seconds;
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
