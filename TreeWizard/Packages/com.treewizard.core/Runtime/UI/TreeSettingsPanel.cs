using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeWizard.UI
{
    /// <summary>
    /// Runtime UI Toolkit editor for a <see cref="TreeDefinition"/>. Controls are generated from the
    /// same [Range], [Min] and [Tooltip] attributes the Unity inspector uses, so a new setting shows
    /// up in both without extra UI work.
    /// </summary>
    public class TreeSettingsPanel : VisualElement
    {
        public const string UssClassName = "tw-settings";
        public const string SectionUssClassName = UssClassName + "__section";
        public const string FieldUssClassName = UssClassName + "__field";
        public const string RowUssClassName = UssClassName + "__row";
        public const string ButtonUssClassName = UssClassName + "__button";
        /// <summary>On a field or button that should take the spare width of its row.</summary>
        public const string GrowUssClassName = UssClassName + "--grow";

        TreeDefinition _definition;
        readonly List<Action> _refreshers = new List<Action>();
        readonly List<Action> _levelRefreshers = new List<Action>();
        VisualElement _levels;
        Button _addLevel;
        Button _removeLevel;

        /// <summary>Raised after any setting changes.</summary>
        public event Action Changed;

        /// <summary>The definition being edited. Setting it rebuilds the panel.</summary>
        public TreeDefinition Definition
        {
            get => _definition;
            set
            {
                _definition = value;
                Rebuild();
            }
        }

        public TreeSettingsPanel()
        {
            AddToClassList(UssClassName);
        }

        /// <summary>Re-reads every control from the definition, after it was changed from outside.</summary>
        public void Refresh()
        {
            foreach (var refresh in _refreshers.Concat(_levelRefreshers))
                refresh();
        }

        void Notify() => Changed?.Invoke();

        void Rebuild()
        {
            Clear();
            _refreshers.Clear();
            _levelRefreshers.Clear();
            if (_definition == null)
                return;

            var seedRow = new VisualElement();
            seedRow.AddToClassList(RowUssClassName);
            var seed = CreateField(_definition, typeof(TreeDefinition).GetField(nameof(TreeDefinition.seed)), _refreshers);
            seed.AddToClassList(GrowUssClassName);
            seedRow.Add(seed);
            seedRow.Add(MakeButton("Randomize", () =>
            {
                _definition.seed = UnityEngine.Random.Range(1, 1_000_000);
                Refresh();
                Notify();
            }));
            Add(seedRow);

            Add(Section("Trunk", _definition.trunk, true, _refreshers));
            Add(BranchesSection());
            Add(Section("Leaves", _definition.leaves, true, _refreshers));
            Add(Section("Mesh", _definition.mesh, false, _refreshers));
        }

        VisualElement BranchesSection()
        {
            var foldout = MakeFoldout("Branches", true);
            _levels = new VisualElement();
            foldout.Add(_levels);

            var buttons = new VisualElement();
            buttons.AddToClassList(RowUssClassName);
            _addLevel = MakeButton("Add Level", () => ResizeLevels(+1));
            _removeLevel = MakeButton("Remove Level", () => ResizeLevels(-1));
            _addLevel.AddToClassList(GrowUssClassName);
            _removeLevel.AddToClassList(GrowUssClassName);
            buttons.Add(_addLevel);
            buttons.Add(_removeLevel);
            foldout.Add(buttons);

            RebuildLevels();
            return foldout;
        }

        void ResizeLevels(int delta)
        {
            var levels = (_definition.branchLevels ?? Array.Empty<BranchLevelSettings>()).ToList();
            if (delta > 0 && levels.Count < TreeDefinition.MaxBranchLevels)
                levels.Add(BranchLevelSettings.OuterLevel());
            else if (delta < 0 && levels.Count > 0)
                levels.RemoveAt(levels.Count - 1);
            else
                return;

            _definition.branchLevels = levels.ToArray();
            RebuildLevels();
            Notify();
        }

        void RebuildLevels()
        {
            _levels.Clear();
            _levelRefreshers.Clear();
            var levels = _definition.branchLevels ?? Array.Empty<BranchLevelSettings>();
            for (var i = 0; i < levels.Length; i++)
                _levels.Add(Section($"Level {i + 1}", levels[i], i == 0, _levelRefreshers));
            _addLevel.SetEnabled(levels.Length < TreeDefinition.MaxBranchLevels);
            _removeLevel.SetEnabled(levels.Length > 0);
        }

        Foldout Section(string title, object settings, bool expanded, List<Action> refreshers)
        {
            var foldout = MakeFoldout(title, expanded);
            foreach (var field in settings.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var control = CreateField(settings, field, refreshers);
                if (control != null)
                    foldout.Add(control);
            }
            return foldout;
        }

        /// <summary>A control for one field, picked from its type and attributes. Null for unsupported types.</summary>
        VisualElement CreateField(object target, FieldInfo field, List<Action> refreshers)
        {
            var label = Nicify(field.Name);
            var range = field.GetCustomAttribute<RangeAttribute>();
            var min = field.GetCustomAttribute<MinAttribute>();
            VisualElement control;

            if (field.FieldType == typeof(float))
            {
                if (range != null)
                    control = Bind(new Slider(label, range.min, range.max) { showInputField = true }, target, field, refreshers, v => Mathf.Clamp(v, range.min, range.max));
                else
                    control = Bind(new FloatField(label), target, field, refreshers, v => min != null ? Mathf.Max(min.min, v) : v);
            }
            else if (field.FieldType == typeof(int))
            {
                if (range != null)
                    control = Bind(new SliderInt(label, (int)range.min, (int)range.max) { showInputField = true }, target, field, refreshers, v => Mathf.Clamp(v, (int)range.min, (int)range.max));
                else
                    control = Bind(new IntegerField(label), target, field, refreshers, v => min != null ? Mathf.Max((int)min.min, v) : v);
            }
            else if (field.FieldType == typeof(bool))
            {
                control = Bind(new Toggle(label), target, field, refreshers, v => v);
            }
            else if (field.FieldType.IsEnum)
            {
                control = Bind(new EnumField(label, (Enum)field.GetValue(target)), target, field, refreshers, v => v);
            }
            else
            {
                return null;
            }

            control.tooltip = field.GetCustomAttribute<TooltipAttribute>()?.tooltip;
            control.AddToClassList(FieldUssClassName);
            return control;
        }

        BaseField<T> Bind<T>(BaseField<T> control, object target, FieldInfo field, List<Action> refreshers, Func<T, T> sanitize)
        {
            control.SetValueWithoutNotify((T)field.GetValue(target));
            control.RegisterValueChangedCallback(e =>
            {
                var value = sanitize(e.newValue);
                field.SetValue(target, value);
                if (!EqualityComparer<T>.Default.Equals(value, e.newValue))
                    control.SetValueWithoutNotify(value);
                Notify();
            });
            refreshers.Add(() => control.SetValueWithoutNotify((T)field.GetValue(target)));
            return control;
        }

        static Foldout MakeFoldout(string title, bool expanded)
        {
            var foldout = new Foldout { text = title, value = expanded };
            foldout.AddToClassList(SectionUssClassName);
            return foldout;
        }

        static Button MakeButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList(ButtonUssClassName);
            return button;
        }

        /// <summary>"lengthAtEnd" to "Length At End", like the inspector's labels.</summary>
        static string Nicify(string name)
        {
            var sb = new StringBuilder(name.Length + 4);
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (i == 0)
                    sb.Append(char.ToUpperInvariant(c));
                else
                {
                    if (char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                        sb.Append(' ');
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
