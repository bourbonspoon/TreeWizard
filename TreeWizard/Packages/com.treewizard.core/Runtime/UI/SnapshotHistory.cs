using System.Collections.Generic;

namespace TreeWizard.UI
{
    /// <summary>
    /// Undo and redo over whole-document snapshots, such as a design saved as JSON. Changes that arrive in quick
    /// succession, like every step of a slider drag, can be merged into one step.
    /// </summary>
    public sealed class SnapshotHistory
    {
        readonly List<string> _undo = new List<string>();
        readonly List<string> _redo = new List<string>();
        readonly int _limit;
        readonly float _mergeSeconds;
        float _lastChange = float.NegativeInfinity;
        bool _mergeable;

        /// <param name="limit">Most undo steps kept; the oldest are dropped.</param>
        /// <param name="mergeSeconds">How close together mergeable changes must be to count as one step.</param>
        public SnapshotHistory(int limit = 100, float mergeSeconds = 0.75f)
        {
            _limit = limit;
            _mergeSeconds = mergeSeconds;
        }

        /// <summary>The document's latest recorded state.</summary>
        public string Current { get; private set; }

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        /// <summary>Forgets all steps and starts over from <paramref name="state"/>, e.g. after opening a document.</summary>
        public void Reset(string state)
        {
            _undo.Clear();
            _redo.Clear();
            Current = state;
            _mergeable = false;
        }

        /// <summary>
        /// Records the document's new state as an undo step, and clears redo. A <paramref name="mergeable"/> change
        /// instead joins the step before it when that was mergeable too and less than the merge time ago.
        /// A state equal to <see cref="Current"/> is ignored.
        /// </summary>
        /// <param name="time">Seconds, from any steady clock.</param>
        public void Record(string state, float time, bool mergeable = false)
        {
            if (state == Current)
                return;
            var merge = mergeable && _mergeable && _undo.Count > 0 && time - _lastChange < _mergeSeconds;
            if (!merge)
            {
                _undo.Add(Current);
                if (_undo.Count > _limit)
                    _undo.RemoveAt(0);
            }
            _redo.Clear();
            Current = state;
            _lastChange = time;
            _mergeable = mergeable;
        }

        /// <summary>Steps back. Returns the state to restore, or null if there's nothing to undo.</summary>
        public string Undo() => Step(_undo, _redo);

        /// <summary>Steps forward again. Returns the state to restore, or null if there's nothing to redo.</summary>
        public string Redo() => Step(_redo, _undo);

        string Step(List<string> from, List<string> to)
        {
            if (from.Count == 0)
                return null;
            to.Add(Current);
            Current = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            _mergeable = false; // the next change starts a new step
            return Current;
        }
    }
}
