# TreeWizard.FileBrowser

An open, save and folder dialog for UI Toolkit, for built players, which have no native file dialog. It looks and behaves the same on Windows, macOS and Linux, and has no dependency on the rest of TreeWizard.

- Places sidebar: Home, Desktop, Documents, Downloads, Pictures, drives or volumes, and recent folders.
- Back, forward and up, clickable breadcrumbs, and a typed path (click beside the breadcrumbs, or Ctrl/Cmd+L). `~` means home.
- Sortable Name, Date Modified and Size columns, search, and type-ahead in the list.
- Open one file, open several, save (with a replace prompt and a New Folder button), or pick a folder.
- File type filters; when saving, the selected filter's extension is added to names without one.
- Keyboard: Enter opens or confirms, Esc cancels, Backspace goes up, Alt+Left/Right or Cmd+[ and ] go back and forward.

```csharp
using TreeWizard.UI;

var browser = new FileBrowser();
root.Add(browser); // a UIDocument's rootVisualElement; it stays hidden until used

var images = new FileFilter("Images", ".png", ".jpg");
browser.OpenFiles("Add Leaf Sheets", Places.Pictures, new[] { images, FileFilter.All }, paths => { /* ... */ });
browser.SaveFile("Export", Places.Documents, "Tree.fbx", new[] { new FileFilter("FBX", ".fbx") }, path => { /* ... */ });
```

If the callback throws, the dialog stays open and shows the exception's message, so the person can pick somewhere else.

`RecentFolders`, `SetRecentFolders` and `RecentFoldersChanged` let the host save recent folders between sessions.

Use `Places.Documents` rather than `Environment.SpecialFolder.MyDocuments`. On macOS and Linux, Mono returns the home folder for MyDocuments.

## Styling

The stylesheet is `Resources/TreeWizardFileBrowser.uss`, and every class starts with `tw-file-browser`. Set these variables on `:root` in your own stylesheet to re-theme it: `--tw-file-browser-window`, `-pane`, `-text`, `-text-dim`, `-hover`, `-selection`, `-accent`, `-error`.
