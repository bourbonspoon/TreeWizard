using System.Collections.Generic;

namespace TreeWizard.UI
{
    /// <summary>Back and forward through visited folders, like a web browser.</summary>
    public sealed class NavigationHistory
    {
        readonly List<string> _folders = new List<string>();
        int _index = -1;

        public string Current => _index >= 0 ? _folders[_index] : null;
        public bool CanGoBack => _index > 0;
        public bool CanGoForward => _index < _folders.Count - 1;

        /// <summary>Records a new folder, dropping anything ahead of the current one.</summary>
        public void Visit(string folder)
        {
            if (folder == Current)
                return;
            _folders.RemoveRange(_index + 1, _folders.Count - _index - 1);
            _folders.Add(folder);
            _index++;
        }

        public string Back()
        {
            if (CanGoBack)
                _index--;
            return Current;
        }

        public string Forward()
        {
            if (CanGoForward)
                _index++;
            return Current;
        }

        public void Clear()
        {
            _folders.Clear();
            _index = -1;
        }
    }
}
