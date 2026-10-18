using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeWizard.UI
{
    /// <summary>
    /// A thumbnail and weight slider for each cell of a leaf atlas: how often the tree uses each leaf or twig.
    /// Edits <see cref="LeafSettings.cellWeights"/>, so the mix belongs to the tree and the atlas is left alone.
    /// Works in built players and in the inspector.
    /// </summary>
    public class LeafMixPanel : VisualElement
    {
        public const string UssClassName = "tw-leaf-mix";
        public const string ItemUssClassName = UssClassName + "__item";
        public const string HintUssClassName = UssClassName + "__hint";
        public const float MaxWeight = 3f;
        const float Thumbnail = 56f;

        LeafSettings _leaves;
        readonly VisualElement _grid;
        readonly Label _hint;
        readonly Button _reset;
        readonly List<Slider> _sliders = new List<Slider>();

        /// <summary>Raised just before a weight changes, e.g. to record undo.</summary>
        public event Action Changing;

        /// <summary>Raised after a weight changes.</summary>
        public event Action Changed;

        /// <summary>The atlas whose cells are shown.</summary>
        public LeafAtlas Atlas { get; private set; }

        public LeafMixPanel()
        {
            AddToClassList(UssClassName);
            _hint = new Label { style = { whiteSpace = WhiteSpace.Normal, marginBottom = 4 } };
            _hint.AddToClassList(HintUssClassName);
            Add(_hint);
            _grid = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            Add(_grid);
            _reset = new Button(ResetWeights) { text = "Reset Mix", style = { alignSelf = Align.FlexEnd } };
            Add(_reset);
        }

        /// <summary>Shows <paramref name="atlas"/>'s cells, editing the weights in <paramref name="leaves"/>.</summary>
        public void Bind(LeafSettings leaves, LeafAtlas atlas)
        {
            _leaves = leaves;
            Atlas = atlas;
            Rebuild();
        }

        /// <summary>Re-reads the sliders, after the weights were changed from outside (undo).</summary>
        public void Refresh()
        {
            for (var i = 0; i < _sliders.Count; i++)
                _sliders[i].SetValueWithoutNotify(WeightAt(i));
        }

        void Rebuild()
        {
            _grid.Clear();
            _sliders.Clear();
            var cells = Atlas != null && Atlas.texture != null ? Atlas.cells : null;
            var any = cells != null && cells.Count > 0;
            _hint.text = any
                ? "How often each leaf is used. 0 leaves it out."
                : "The leaf texture has no atlas, so every leaf card shows the whole texture.";
            _reset.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
            if (!any)
                return;

            for (var i = 0; i < cells.Count; i++)
            {
                var index = i;
                var item = new VisualElement { style = { width = Thumbnail + 12, alignItems = Align.Center, marginRight = 2, marginBottom = 4 } };
                item.AddToClassList(ItemUssClassName);
                item.Add(new Image
                {
                    image = Atlas.texture,
                    uv = cells[i].uvRect,
                    scaleMode = ScaleMode.ScaleToFit,
                    tooltip = cells[i].autumn ? $"Leaf {i + 1}, autumn" : $"Leaf {i + 1}",
                    style = { width = Thumbnail, height = Thumbnail },
                });
                var slider = new Slider(0f, MaxWeight) { style = { width = Thumbnail + 8, marginLeft = 0, marginRight = 0 } };
                slider.SetValueWithoutNotify(WeightAt(i));
                slider.RegisterValueChangedCallback(e => SetWeight(index, e.newValue));
                _sliders.Add(slider);
                item.Add(slider);
                _grid.Add(item);
            }
        }

        float WeightAt(int index)
        {
            var weights = _leaves?.cellWeights;
            return weights != null && index < weights.Length ? weights[index] : 1f;
        }

        void SetWeight(int index, float value)
        {
            if (_leaves == null)
                return;
            Changing?.Invoke();
            var weights = new float[Atlas.cells.Count];
            for (var i = 0; i < weights.Length; i++)
                weights[i] = i == index ? value : WeightAt(i);
            _leaves.cellWeights = weights;
            Changed?.Invoke();
        }

        void ResetWeights()
        {
            if (_leaves == null || _leaves.cellWeights == null || _leaves.cellWeights.Length == 0)
                return;
            Changing?.Invoke();
            _leaves.cellWeights = Array.Empty<float>();
            Refresh();
            Changed?.Invoke();
        }
    }
}
