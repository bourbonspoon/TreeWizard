using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeWizard.UI
{
    /// <summary>
    /// Open, save and folder dialog drawn with UI Toolkit, for built players, which have no native file dialog.
    /// It looks and behaves the same on Windows, macOS and Linux. Add one to a panel's root; it stays hidden until
    /// one of <see cref="OpenFile"/>, <see cref="OpenFiles"/>, <see cref="SaveFile"/> or <see cref="SelectFolder"/>
    /// is called, then covers the panel until the person confirms or cancels.
    /// </summary>
    /// <remarks>
    /// Colours come from USS variables that a host stylesheet can set on :root, such as
    /// --tw-file-browser-accent; see Resources/TreeWizardFileBrowser.uss.
    /// </remarks>
    [UxmlElement]
    public partial class FileBrowser : VisualElement
    {
        public enum Mode { OpenFile, OpenFiles, SaveFile, SelectFolder }

        public const string UssClassName = "tw-file-browser";
        const string HiddenClassName = UssClassName + "--hidden";
        const string HiddenPartClassName = UssClassName + "__hidden";
        const string StyleSheetResource = "TreeWizardFileBrowser";
        const int MaxRecentFolders = 8;
        const double TypeAheadSeconds = 1.0;

        static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".exr", ".hdr", ".bmp", ".tif", ".tiff", ".gif" };

        readonly Label _title;
        readonly Button _back;
        readonly Button _forward;
        readonly Button _up;
        readonly VisualElement _pathBox;
        readonly VisualElement _breadcrumbs;
        readonly TextField _pathField;
        readonly TextField _search;
        readonly ScrollView _places;
        readonly MultiColumnListView _list;
        readonly TextField _fileName;
        readonly DropdownField _filter;
        readonly Label _message;
        readonly VisualElement _buttons;
        readonly Button _newFolder;
        readonly Button _confirm;
        readonly VisualElement _prompt;
        readonly Label _promptText;
        readonly TextField _promptField;
        readonly Button _promptConfirm;

        readonly NavigationHistory _history = new NavigationHistory();
        readonly List<FileEntry> _entries = new List<FileEntry>(); // everything listed in the folder
        readonly List<FileEntry> _visible = new List<FileEntry>(); // what the search leaves showing
        readonly List<string> _recent = new List<string>();

        Mode _mode;
        FileFilter[] _filters = { FileFilter.All };
        Action<string[]> _onConfirm;
        Action _onCancel;
        Func<string, bool> _onPrompt;
        FileSortColumn _sortColumn = FileSortColumn.Name;
        bool _sortAscending = true;
        string _typeAhead = "";
        double _typeAheadTime;

        public bool IsOpen => !ClassListContains(HiddenClassName);

        /// <summary>The folder being shown.</summary>
        public string CurrentFolder => _history.Current;

        /// <summary>The folders and files being shown, in display order.</summary>
        public IReadOnlyList<FileEntry> Entries => _visible;

        /// <summary>Folders recently opened from or saved to, newest first.</summary>
        public IReadOnlyList<string> RecentFolders => _recent;

        /// <summary>Raised when <see cref="RecentFolders"/> changes, so the host can save the list.</summary>
        public event Action RecentFoldersChanged;

        public FileBrowser()
        {
            AddToClassList(UssClassName);
            AddToClassList(HiddenClassName);
            var styles = Resources.Load<StyleSheet>(StyleSheetResource);
            if (styles != null)
                styleSheets.Add(styles);

            var window = Part(this, "window");

            _title = new Label();
            _title.AddToClassList(ClassName("title"));
            window.Add(_title);

            var toolbar = Part(window, "toolbar");
            _back = IconButton(toolbar, "back", GoBack);
            _forward = IconButton(toolbar, "forward", GoForward);
            _up = IconButton(toolbar, "up", GoUp);

            _pathBox = Part(toolbar, "path");
            _breadcrumbs = Part(_pathBox, "breadcrumbs");
            _breadcrumbs.RegisterCallback<ClickEvent>(e =>
            {
                if (e.target == _breadcrumbs)
                    EditPath(); // clicking beside the crumbs types a path, as in Windows Explorer
            });
            _pathField = new TextField { isDelayed = true };
            _pathField.AddToClassList(ClassName("path-field"));
            _pathField.RegisterValueChangedCallback(e => GoTo(e.newValue));
            _pathField.RegisterCallback<FocusOutEvent>(_ => StopEditingPath());
            _pathBox.Add(_pathField);
            IconButton(_pathBox, "edit", EditPath);

            _search = new TextField();
            _search.AddToClassList(ClassName("search"));
            _search.textEdition.placeholder = "Search";
            _search.RegisterValueChangedCallback(_ => ApplySortAndSearch());
            toolbar.Add(_search);

            var body = Part(window, "body");
            _places = new ScrollView { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            _places.AddToClassList(ClassName("places"));
            body.Add(_places);

            _list = new MultiColumnListView
            {
                itemsSource = _visible,
                fixedItemHeight = 26,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                sortingMode = ColumnSortingMode.Custom,
            };
            _list.AddToClassList(ClassName("list"));
            _list.columns.Add(new Column { name = "name", title = "Name", stretchable = true, minWidth = 160, makeCell = MakeNameCell, bindCell = BindNameCell });
            _list.columns.Add(new Column { name = "modified", title = "Date Modified", width = 170, makeCell = MakeTextCell, bindCell = (cell, i) => ((Label)cell).text = _visible[i].Modified.ToString("g") });
            _list.columns.Add(new Column { name = "size", title = "Size", width = 90, makeCell = MakeTextCell, bindCell = (cell, i) => ((Label)cell).text = FolderListing.FormatSize(_visible[i].Size) });
            _list.sortColumnDescriptions.Add(new SortColumnDescription("name", SortDirection.Ascending));
            _list.columnSortingChanged += OnSortingChanged;
            _list.itemsChosen += Choose;
            _list.selectionChanged += OnSelectionChanged;
            body.Add(_list);

            var nameRow = Part(window, "name-row");
            _fileName = new TextField("Name");
            _fileName.AddToClassList(ClassName("file-name"));
            nameRow.Add(_fileName);
            _filter = new DropdownField();
            _filter.AddToClassList(ClassName("filter"));
            _filter.RegisterValueChangedCallback(_ => OnFilterChanged());
            nameRow.Add(_filter);

            _message = new Label();
            _message.AddToClassList(ClassName("message"));
            window.Add(_message);

            _prompt = Part(window, "prompt");
            _promptText = new Label();
            _promptText.AddToClassList(ClassName("prompt-text"));
            _prompt.Add(_promptText);
            _promptField = new TextField();
            _promptField.AddToClassList(ClassName("prompt-field"));
            _prompt.Add(_promptField);
            _prompt.Add(TextButton("Cancel", HidePrompt));
            _promptConfirm = TextButton("OK", ConfirmPrompt);
            _promptConfirm.AddToClassList(ClassName("confirm"));
            _prompt.Add(_promptConfirm);
            SetPartVisible(_prompt, false);

            _buttons = Part(window, "buttons");
            _newFolder = TextButton("New Folder", NewFolder);
            _buttons.Add(_newFolder);
            Part(_buttons, "spacer");
            _buttons.Add(TextButton("Cancel", Cancel));
            _confirm = TextButton("Open", Confirm);
            _confirm.AddToClassList(ClassName("confirm"));
            _buttons.Add(_confirm);

            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        }

        public void OpenFile(string title, string folder, FileFilter[] filters, Action<string> onOpen, Action onCancel = null) =>
            Show(Mode.OpenFile, title, folder, null, filters, paths => onOpen(paths[0]), onCancel);

        public void OpenFiles(string title, string folder, FileFilter[] filters, Action<string[]> onOpen, Action onCancel = null) =>
            Show(Mode.OpenFiles, title, folder, null, filters, onOpen, onCancel);

        /// <param name="fileName">Suggested name. The selected filter's extension is added if the name has none.</param>
        public void SaveFile(string title, string folder, string fileName, FileFilter[] filters, Action<string> onSave, Action onCancel = null) =>
            Show(Mode.SaveFile, title, folder, fileName, filters, paths => onSave(paths[0]), onCancel);

        public void SelectFolder(string title, string folder, Action<string> onSelect, Action onCancel = null) =>
            Show(Mode.SelectFolder, title, folder, null, null, paths => onSelect(paths[0]), onCancel);

        /// <summary>Replaces the recent folders list, for example with one saved from an earlier session.</summary>
        public void SetRecentFolders(IEnumerable<string> folders)
        {
            _recent.Clear();
            _recent.AddRange(folders.Where(f => !string.IsNullOrEmpty(f)).Take(MaxRecentFolders));
        }

        /// <summary>Shows <paramref name="folder"/>, or explains in the dialog why it can't.</summary>
        public void Navigate(string folder) => Navigate(folder, true);

        public void Cancel()
        {
            var onCancel = _onCancel;
            Close();
            onCancel?.Invoke();
        }

        void Show(Mode mode, string title, string folder, string fileName, FileFilter[] filters, Action<string[]> onConfirm, Action onCancel)
        {
            _mode = mode;
            _onConfirm = onConfirm;
            _onCancel = onCancel;
            _filters = filters != null && filters.Length > 0 ? filters : new[] { FileFilter.All };

            _title.text = title;
            _confirm.text = mode switch
            {
                Mode.SaveFile => "Save",
                Mode.SelectFolder => "Select Folder",
                _ => "Open",
            };
            _list.selectionType = mode == Mode.OpenFiles ? SelectionType.Multiple : SelectionType.Single;
            SetPartVisible(_fileName, mode == Mode.SaveFile);
            SetPartVisible(_filter, mode != Mode.SelectFolder);
            SetPartVisible(_newFolder, mode == Mode.SaveFile || mode == Mode.SelectFolder);
            _filter.choices = _filters.Select(f => f.ToString()).ToList();
            _filter.SetValueWithoutNotify(_filter.choices[0]);
            _fileName.SetValueWithoutNotify(fileName ?? "");
            _search.SetValueWithoutNotify("");
            HidePrompt();
            StopEditingPath();
            SetMessage(null);

            _history.Clear();
            RebuildPlaces();
            Navigate(ExistingFolder(folder));

            RemoveFromClassList(HiddenClassName);
            BringToFront();
            schedule.Execute(() =>
            {
                if (mode == Mode.SaveFile)
                    _fileName.Focus();
                else
                    _list.Focus();
            });
        }

        void Close()
        {
            AddToClassList(HiddenClassName);
            HidePrompt();
            _onConfirm = null;
            _onCancel = null;
        }

        // The requested folder, or the nearest parent that exists, or home.
        static string ExistingFolder(string folder)
        {
            try
            {
                for (var dir = string.IsNullOrEmpty(folder) ? null : new DirectoryInfo(folder); dir != null; dir = dir.Parent)
                {
                    if (dir.Exists)
                        return dir.FullName;
                }
            }
            catch (Exception e) when (e is ArgumentException || e is IOException || e is NotSupportedException)
            {
            }
            return Places.Home;
        }

        FileFilter CurrentFilter => _filters[Mathf.Clamp(_filter.index, 0, _filters.Length - 1)];

        void Navigate(string folder, bool record)
        {
            List<FileEntry> entries;
            try
            {
                folder = Path.GetFullPath(folder);
                entries = FolderListing.List(folder, CurrentFilter, _mode != Mode.SelectFolder);
            }
            catch (UnauthorizedAccessException)
            {
                SetMessage("You don't have permission to open that folder.");
                return;
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is NotSupportedException || e is System.Security.SecurityException)
            {
                SetMessage(e is DirectoryNotFoundException ? "That folder doesn't exist." : $"Can't open that folder: {e.Message}");
                return;
            }

            if (record)
                _history.Visit(folder);
            _entries.Clear();
            _entries.AddRange(entries);
            SetMessage(null);
            _search.SetValueWithoutNotify("");
            ApplySortAndSearch();
            RebuildBreadcrumbs();
            UpdatePlaceSelection();
            _back.SetEnabled(_history.CanGoBack);
            _forward.SetEnabled(_history.CanGoForward);
            _up.SetEnabled(Directory.GetParent(folder) != null);
        }

        void Refresh() => Navigate(CurrentFolder, false);

        void GoBack()
        {
            if (_history.CanGoBack)
                Navigate(_history.Back(), false);
        }

        void GoForward()
        {
            if (_history.CanGoForward)
                Navigate(_history.Forward(), false);
        }

        void GoUp()
        {
            var parent = CurrentFolder != null ? Directory.GetParent(CurrentFolder) : null;
            if (parent != null)
                Navigate(parent.FullName);
        }

        // A typed path: folders open, files are chosen as if picked from the list.
        void GoTo(string typed)
        {
            StopEditingPath();
            string path;
            try
            {
                path = FolderListing.Resolve(CurrentFolder, typed);
            }
            catch (ArgumentException)
            {
                SetMessage("That isn't a valid path.");
                return;
            }
            if (Directory.Exists(path) || !File.Exists(path) || _mode == Mode.SelectFolder)
            {
                Navigate(path);
                return;
            }
            Navigate(Path.GetDirectoryName(path));
            if (_mode == Mode.SaveFile)
                _fileName.value = Path.GetFileName(path);
            else
                Select(Path.GetFileName(path));
            Confirm();
        }

        void Select(string name)
        {
            var index = _visible.FindIndex(e => e.Name == name);
            if (index >= 0)
                _list.SetSelection(index);
        }

        void EditPath()
        {
            _pathField.SetValueWithoutNotify(CurrentFolder);
            _pathBox.AddToClassList(ClassName("path--editing"));
            _pathField.schedule.Execute(() =>
            {
                _pathField.Focus();
                _pathField.SelectAll();
            });
        }

        void StopEditingPath() => _pathBox.RemoveFromClassList(ClassName("path--editing"));

        void OnSortingChanged()
        {
            var sorted = _list.sortedColumns.FirstOrDefault();
            _sortColumn = sorted?.columnName switch
            {
                "modified" => FileSortColumn.Modified,
                "size" => FileSortColumn.Size,
                _ => FileSortColumn.Name,
            };
            _sortAscending = sorted == null || sorted.direction == SortDirection.Ascending;
            ApplySortAndSearch();
        }

        void ApplySortAndSearch()
        {
            FolderListing.Sort(_entries, _sortColumn, _sortAscending);
            var query = _search.value.Trim();
            _visible.Clear();
            _visible.AddRange(query.Length == 0 ? _entries : _entries.Where(e => e.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
            _list.ClearSelection();
            _list.RefreshItems();
        }

        void OnFilterChanged()
        {
            // Swap a typed extension for the new filter's, as native save dialogs do.
            var name = _fileName.value;
            var filter = CurrentFilter;
            if (_mode == Mode.SaveFile && !filter.MatchesAll && !filter.Matches(name))
            {
                var extension = Path.GetExtension(name).ToLowerInvariant();
                if (_filters.Any(f => f.Extensions.Contains(extension)))
                    _fileName.SetValueWithoutNotify(Path.ChangeExtension(name, filter.DefaultExtension));
            }
            Refresh();
        }

        List<FileEntry> SelectedEntries() => _list.selectedItems.Cast<FileEntry>().ToList();

        void OnSelectionChanged(IEnumerable<object> items)
        {
            SetMessage(null);
            if (_mode != Mode.SaveFile)
                return;
            foreach (FileEntry entry in items)
            {
                if (!entry.IsFolder)
                    _fileName.SetValueWithoutNotify(entry.Name);
            }
        }

        // Double-click or Enter on the list.
        void Choose(IEnumerable<object> items)
        {
            var chosen = items.Cast<FileEntry>().ToList();
            if (chosen.Count == 0)
                return;
            if (chosen.Count == 1 && chosen[0].IsFolder)
            {
                Navigate(chosen[0].Path);
                return;
            }
            if (_mode == Mode.SaveFile)
                _fileName.SetValueWithoutNotify(chosen[0].Name);
            Confirm();
        }

        internal void Confirm()
        {
            if (IsPromptVisible)
                return;
            var selected = SelectedEntries();
            switch (_mode)
            {
                case Mode.OpenFile:
                case Mode.OpenFiles:
                    if (selected.Count == 1 && selected[0].IsFolder)
                    {
                        Navigate(selected[0].Path);
                        return;
                    }
                    var files = selected.Where(e => !e.IsFolder).Select(e => e.Path).ToArray();
                    if (files.Length == 0)
                    {
                        SetMessage(_mode == Mode.OpenFiles ? "Select one or more files." : "Select a file.");
                        return;
                    }
                    Complete(files);
                    break;
                case Mode.SaveFile:
                    ConfirmSave(selected);
                    break;
                case Mode.SelectFolder:
                    Complete(new[] { selected.Count == 1 && selected[0].IsFolder ? selected[0].Path : CurrentFolder });
                    break;
            }
        }

        void ConfirmSave(List<FileEntry> selected)
        {
            var typed = _fileName.value.Trim();
            if (typed.Length == 0)
            {
                if (selected.Count == 1 && selected[0].IsFolder)
                    Navigate(selected[0].Path);
                else
                    SetMessage("Enter a name.");
                return;
            }

            string path;
            try
            {
                path = FolderListing.Resolve(CurrentFolder, typed);
            }
            catch (ArgumentException)
            {
                SetMessage(FolderListing.ValidateName(typed) ?? "That isn't a valid file name.");
                return;
            }
            if (Directory.Exists(path))
            {
                _fileName.SetValueWithoutNotify("");
                Navigate(path);
                return;
            }

            var folder = Path.GetDirectoryName(path);
            var name = Path.GetFileName(path);
            var error = FolderListing.ValidateName(name);
            if (error != null)
            {
                SetMessage(error);
                return;
            }
            if (!Directory.Exists(folder))
            {
                SetMessage("That folder doesn't exist.");
                return;
            }
            if (!CurrentFilter.Matches(name))
            {
                name += CurrentFilter.DefaultExtension;
                path = Path.Combine(folder, name);
            }
            if (File.Exists(path))
            {
                ShowPrompt($"\"{name}\" already exists. Replace it?", null, "Replace", _ =>
                {
                    Complete(new[] { path });
                    return true;
                });
                return;
            }
            Complete(new[] { path });
        }

        void Complete(string[] paths)
        {
            try
            {
                _onConfirm?.Invoke(paths);
            }
            catch (Exception e)
            {
                SetMessage(e.Message); // e.g. the host couldn't write the file; let them pick somewhere else
                return;
            }
            AddRecent(_mode == Mode.SelectFolder ? paths[0] : Path.GetDirectoryName(paths[0]));
            Close();
        }

        void AddRecent(string folder)
        {
            _recent.RemoveAll(f => Places.SamePath(f, folder));
            _recent.Insert(0, folder);
            if (_recent.Count > MaxRecentFolders)
                _recent.RemoveRange(MaxRecentFolders, _recent.Count - MaxRecentFolders);
            RecentFoldersChanged?.Invoke();
        }

        void NewFolder() =>
            ShowPrompt("New folder:", FolderListing.UniqueName(CurrentFolder, "New Folder"), "Create", typed =>
            {
                var name = typed.Trim();
                var path = Path.Combine(CurrentFolder, name);
                var error = FolderListing.ValidateName(name);
                if (error == null && (Directory.Exists(path) || File.Exists(path)))
                    error = $"\"{name}\" already exists.";
                if (error == null)
                {
                    try
                    {
                        Directory.CreateDirectory(path);
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    {
                        error = e.Message;
                    }
                }
                if (error != null)
                {
                    SetMessage(error);
                    return false;
                }
                Navigate(path); // like native dialogs, step into the new folder
                return true;
            });

        bool IsPromptVisible => !_prompt.ClassListContains(HiddenPartClassName);

        // Asks a question in place of the buttons; onConfirm returns false to keep the prompt open.
        void ShowPrompt(string text, string fieldValue, string confirmText, Func<string, bool> onConfirm)
        {
            _promptText.text = text;
            SetPartVisible(_promptField, fieldValue != null);
            _promptField.SetValueWithoutNotify(fieldValue ?? "");
            _promptConfirm.text = confirmText;
            _onPrompt = onConfirm;
            SetPartVisible(_prompt, true);
            SetPartVisible(_buttons, false);
            schedule.Execute(() =>
            {
                if (fieldValue != null)
                {
                    _promptField.Focus();
                    _promptField.SelectAll();
                }
                else
                {
                    _promptConfirm.Focus();
                }
            });
        }

        void ConfirmPrompt()
        {
            if (_onPrompt == null || _onPrompt(_promptField.value))
                HidePrompt();
        }

        void HidePrompt()
        {
            _onPrompt = null;
            SetPartVisible(_prompt, false);
            SetPartVisible(_buttons, true);
        }

        void SetMessage(string message)
        {
            _message.text = message ?? "";
            SetPartVisible(_message, !string.IsNullOrEmpty(message));
        }

        void RebuildPlaces()
        {
            _places.Clear();
            AddPlaces("Places", Places.Standard());
            AddPlaces("Locations", Places.Drives());
            AddPlaces("Recent", _recent.Where(Directory.Exists).Select(f => new Place(FolderName(f), f, PlaceKind.Recent)).ToList());
        }

        void AddPlaces(string heading, List<Place> places)
        {
            if (places.Count == 0)
                return;
            var label = new Label(heading);
            label.AddToClassList(ClassName("places-heading"));
            _places.Add(label);
            foreach (var place in places)
            {
                var button = new Button(() => Navigate(place.Path)) { userData = place.Path, tooltip = place.Path };
                button.AddToClassList(ClassName("place"));
                button.Add(Icon(place.Kind.ToString().ToLowerInvariant()));
                var name = new Label(place.Name);
                name.AddToClassList(ClassName("place-name"));
                button.Add(name);
                _places.Add(button);
            }
        }

        void UpdatePlaceSelection()
        {
            _places.Query<Button>(className: ClassName("place")).ForEach(button =>
                button.EnableInClassList(ClassName("place--selected"), Places.SamePath((string)button.userData, CurrentFolder)));
        }

        static string FolderName(string folder)
        {
            var name = Path.GetFileName(folder.TrimEnd('/', '\\'));
            return string.IsNullOrEmpty(name) ? folder : name;
        }

        void RebuildBreadcrumbs()
        {
            _breadcrumbs.Clear();
            var crumbs = FolderListing.Breadcrumbs(CurrentFolder);
            for (var i = 0; i < crumbs.Count; i++)
            {
                if (i > 0)
                    _breadcrumbs.Add(Icon("chevron"));
                var path = crumbs[i].path;
                var crumb = TextButton(crumbs[i].label, () => Navigate(path));
                crumb.AddToClassList(ClassName("crumb"));
                _breadcrumbs.Add(crumb);
            }
        }

        VisualElement MakeNameCell()
        {
            var cell = new VisualElement();
            cell.AddToClassList(ClassName("name-cell"));
            cell.Add(Icon("file"));
            cell.Add(MakeTextCell());
            return cell;
        }

        void BindNameCell(VisualElement cell, int index)
        {
            var entry = _visible[index];
            var image = !entry.IsFolder && ImageExtensions.Any(e => entry.Name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
            var icon = cell[0];
            icon.EnableInClassList(ClassName("icon--folder"), entry.IsFolder);
            icon.EnableInClassList(ClassName("icon--image"), image);
            icon.EnableInClassList(ClassName("icon--file"), !entry.IsFolder && !image);
            ((Label)cell[1]).text = entry.Name;
        }

        static VisualElement MakeTextCell()
        {
            var label = new Label();
            label.AddToClassList(ClassName("cell"));
            return label;
        }

        void OnKeyDown(KeyDownEvent e)
        {
            if (!IsOpen)
                return;
            var target = e.target as VisualElement;
            var typing = IsTextInput(target);

            switch (e.keyCode)
            {
                case KeyCode.Escape:
                    if (IsPromptVisible)
                        HidePrompt();
                    else if (_pathBox.ClassListContains(ClassName("path--editing")))
                        StopEditingPath();
                    else
                        Cancel();
                    e.StopPropagation();
                    return;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (IsPromptVisible && (target == _promptConfirm || _promptField.Contains(target)))
                    {
                        ConfirmPrompt();
                        e.StopPropagation();
                    }
                    else if (_fileName.Contains(target))
                    {
                        Confirm();
                        e.StopPropagation();
                    }
                    return;
                case KeyCode.Backspace when !typing:
                    GoUp();
                    e.StopPropagation();
                    return;
                case KeyCode.LeftArrow when !typing && (e.altKey || e.commandKey):
                case KeyCode.LeftBracket when e.commandKey:
                    GoBack();
                    e.StopPropagation();
                    return;
                case KeyCode.RightArrow when !typing && (e.altKey || e.commandKey):
                case KeyCode.RightBracket when e.commandKey:
                    GoForward();
                    e.StopPropagation();
                    return;
                case KeyCode.UpArrow when e.altKey || e.commandKey:
                    GoUp();
                    e.StopPropagation();
                    return;
                case KeyCode.L when e.actionKey:
                    EditPath();
                    e.StopPropagation();
                    return;
                case KeyCode.F when e.actionKey:
                    _search.Focus();
                    e.StopPropagation();
                    return;
            }

            // Typing a name while the list has focus jumps to it.
            if (!typing && _list.Contains(target) && !e.actionKey && !e.altKey && e.character != '\0' && !char.IsControl(e.character))
            {
                TypeAhead(e.character);
                e.StopPropagation();
            }
        }

        bool IsTextInput(VisualElement element)
        {
            for (var v = element; v != null && v != this; v = v.parent)
            {
                if (v is TextField)
                    return true;
            }
            return false;
        }

        void TypeAhead(char c)
        {
            var now = Time.realtimeSinceStartupAsDouble;
            _typeAhead = now - _typeAheadTime > TypeAheadSeconds ? c.ToString() : _typeAhead + c;
            _typeAheadTime = now;
            var index = _visible.FindIndex(entry => entry.Name.StartsWith(_typeAhead, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return;
            _list.SetSelection(index);
            _list.ScrollToItem(index);
        }

        static string ClassName(string part) => $"{UssClassName}__{part}";

        static VisualElement Part(VisualElement parent, string part)
        {
            var element = new VisualElement();
            element.AddToClassList(ClassName(part));
            parent.Add(element);
            return element;
        }

        static void SetPartVisible(VisualElement element, bool visible) => element.EnableInClassList(HiddenPartClassName, !visible);

        static VisualElement Icon(string name)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList(ClassName("icon"));
            icon.AddToClassList(ClassName("icon--" + name));
            return icon;
        }

        static Button IconButton(VisualElement parent, string icon, Action onClick)
        {
            var button = new Button(onClick);
            button.AddToClassList(ClassName("icon-button"));
            button.Add(Icon(icon));
            parent.Add(button);
            return button;
        }

        static Button TextButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList(ClassName("button"));
            return button;
        }
    }
}
