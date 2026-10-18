using System;
using UnityEngine.UIElements;

namespace TreeWizard.App
{
    /// <summary>
    /// A small modal question over the app, such as whether to save changes first. Enter picks the primary choice,
    /// Escape the cancel choice.
    /// </summary>
    public sealed class ConfirmDialog : VisualElement
    {
        public readonly struct Choice
        {
            public readonly string Label;
            public readonly Action Action;
            public readonly bool Primary;
            public readonly bool Cancel;

            public Choice(string label, Action action, bool primary = false, bool cancel = false)
            {
                Label = label;
                Action = action;
                Primary = primary;
                Cancel = cancel;
            }
        }

        readonly Label _title = new Label();
        readonly Label _message = new Label();
        readonly VisualElement _buttons = new VisualElement();
        Choice[] _choices = Array.Empty<Choice>();

        public bool IsOpen => !ClassListContains("confirm--hidden");

        public ConfirmDialog()
        {
            AddToClassList("confirm");
            AddToClassList("confirm--hidden");
            var window = new VisualElement();
            window.AddToClassList("confirm__window");
            _title.AddToClassList("confirm__title");
            _message.AddToClassList("confirm__message");
            _buttons.AddToClassList("confirm__buttons");
            window.Add(_title);
            window.Add(_message);
            window.Add(_buttons);
            Add(window);
            focusable = true;
            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        }

        /// <summary>Shows the question. Choices appear left to right in the order given.</summary>
        public void Show(string title, string message, params Choice[] choices)
        {
            _title.text = title;
            _message.text = message;
            _choices = choices;
            _buttons.Clear();
            var spacer = new VisualElement();
            spacer.AddToClassList("confirm__spacer");
            _buttons.Add(spacer);
            foreach (var choice in choices)
            {
                var c = choice;
                var button = new Button(() => Pick(c)) { text = c.Label };
                button.AddToClassList("confirm__button");
                if (c.Primary)
                    button.AddToClassList("confirm__button--primary");
                _buttons.Add(button);
            }
            RemoveFromClassList("confirm--hidden");
            BringToFront();
            schedule.Execute(() => Focus());
        }

        /// <summary>Closes the dialog as if its cancel choice was picked.</summary>
        public void Cancel()
        {
            foreach (var choice in _choices)
            {
                if (choice.Cancel)
                {
                    Pick(choice);
                    return;
                }
            }
            AddToClassList("confirm--hidden");
        }

        void Pick(Choice choice)
        {
            AddToClassList("confirm--hidden");
            choice.Action?.Invoke();
        }

        void OnKeyDown(KeyDownEvent e)
        {
            if (!IsOpen)
                return;
            foreach (var choice in _choices)
            {
                if ((e.keyCode == UnityEngine.KeyCode.Escape && choice.Cancel) ||
                    ((e.keyCode == UnityEngine.KeyCode.Return || e.keyCode == UnityEngine.KeyCode.KeypadEnter) && choice.Primary))
                {
                    e.StopPropagation();
                    Pick(choice);
                    return;
                }
            }
        }
    }
}
