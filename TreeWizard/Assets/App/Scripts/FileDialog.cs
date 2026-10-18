using System;
using System.IO;
using System.Linq;
using UnityEngine.UIElements;

namespace TreeWizard.App
{
    /// <summary>
    /// Minimal open/save dialog drawn with UI Toolkit, since built players have no native file dialog.
    /// Lists folders and files with the wanted extension; click a folder to enter it.
    /// </summary>
    public class FileDialog : VisualElement
    {
        public enum Mode { Open, Save }

        const string UssClassName = "file-dialog";

        readonly Label _title;
        readonly TextField _folder;
        readonly ScrollView _list;
        readonly TextField _fileName;
        readonly Label _error;
        readonly Button _confirm;
        Mode _mode;
        string _extension;
        Action<string> _onConfirm;

        public FileDialog()
        {
            AddToClassList(UssClassName);
            AddToClassList(UssClassName + "--hidden");

            var window = new VisualElement();
            window.AddToClassList(UssClassName + "__window");
            Add(window);

            _title = new Label();
            _title.AddToClassList(UssClassName + "__title");
            window.Add(_title);

            var folderRow = new VisualElement();
            folderRow.AddToClassList(UssClassName + "__row");
            var up = new Button(() => Navigate(Directory.GetParent(_folder.value)?.FullName)) { text = "Up" };
            folderRow.Add(up);
            _folder = new TextField { isDelayed = true };
            _folder.AddToClassList(UssClassName + "__folder");
            _folder.RegisterValueChangedCallback(e => Navigate(e.newValue));
            folderRow.Add(_folder);
            window.Add(folderRow);

            _list = new ScrollView();
            _list.AddToClassList(UssClassName + "__list");
            window.Add(_list);

            _fileName = new TextField("File name");
            _fileName.AddToClassList(UssClassName + "__file-name");
            window.Add(_fileName);

            _error = new Label();
            _error.AddToClassList(UssClassName + "__error");
            window.Add(_error);

            var buttons = new VisualElement();
            buttons.AddToClassList(UssClassName + "__buttons");
            buttons.Add(new Button(Hide) { text = "Cancel" });
            _confirm = new Button(Confirm);
            _confirm.AddToClassList(UssClassName + "__confirm");
            buttons.Add(_confirm);
            window.Add(buttons);
        }

        public void Show(Mode mode, string title, string folder, string fileName, string extension, Action<string> onConfirm)
        {
            _mode = mode;
            _extension = extension;
            _onConfirm = onConfirm;
            _title.text = title;
            _confirm.text = mode == Mode.Save ? "Save" : "Open";
            _fileName.SetValueWithoutNotify(fileName ?? "");
            _error.text = "";
            Directory.CreateDirectory(folder);
            Navigate(folder);
            RemoveFromClassList(UssClassName + "--hidden");
            BringToFront();
        }

        public void Hide() => AddToClassList(UssClassName + "--hidden");

        void Navigate(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                _error.text = "Folder not found.";
                return;
            }
            _error.text = "";
            _folder.SetValueWithoutNotify(Path.GetFullPath(folder));
            _list.Clear();
            try
            {
                foreach (var dir in Directory.GetDirectories(folder).Where(d => !Path.GetFileName(d).StartsWith(".")).OrderBy(d => d))
                    _list.Add(Entry(Path.GetFileName(dir) + "/", "folder", () => Navigate(dir)));
                foreach (var file in Directory.GetFiles(folder, "*" + _extension).OrderBy(f => f))
                {
                    var name = Path.GetFileName(file);
                    _list.Add(Entry(name, "file", () =>
                    {
                        _fileName.value = name;
                        if (_mode == Mode.Open)
                            Confirm();
                    }));
                }
            }
            catch (UnauthorizedAccessException)
            {
                _error.text = "No permission to read this folder.";
            }
        }

        Button Entry(string text, string kind, Action onClick)
        {
            var entry = new Button(onClick) { text = text };
            entry.AddToClassList(UssClassName + "__entry");
            entry.AddToClassList($"{UssClassName}__entry--{kind}");
            return entry;
        }

        void Confirm()
        {
            var name = _fileName.value.Trim();
            if (name.Length == 0)
            {
                _error.text = "Enter a file name.";
                return;
            }
            if (!name.EndsWith(_extension, StringComparison.OrdinalIgnoreCase))
                name += _extension;
            var path = Path.Combine(_folder.value, name);
            if (_mode == Mode.Open && !File.Exists(path))
            {
                _error.text = "File not found.";
                return;
            }
            try
            {
                _onConfirm?.Invoke(path);
                Hide();
            }
            catch (Exception e)
            {
                _error.text = e.Message;
            }
        }
    }
}
