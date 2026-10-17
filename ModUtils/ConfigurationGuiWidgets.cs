using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ModUtils
{
    /// <summary>
    /// Creates widgets for a <see cref="ConfigurationGuiCustomEditor"/> in the style of the built-in
    /// editors, copied from the game's Settings screen. Each editor gets its own instance, which remembers
    /// the buttons and text fields it created for the editor's default <c>SetInteractable</c> and
    /// <c>ConsumesEscape</c>; widgets created after <c>SetInteractable(false)</c> start disabled too.
    /// </summary>
    public sealed class ConfigurationGuiWidgets
    {
        internal const float Spacing = 4f;
        private const float RowSpacing = 8f;

        private readonly List<ConfigurationGuiInputField> _fields = new List<ConfigurationGuiInputField>();
        private readonly List<Selectable> _selectables = new List<Selectable>();
        private readonly ConfigurationGuiTemplates _templates;
        private bool _interactable = true;

        internal ConfigurationGuiWidgets(ConfigurationGuiTemplates templates)
        {
            _templates = templates;
        }

        /// <summary>
        /// Height of one line of controls in the built-in editors. Buttons and text fields start with it.
        /// </summary>
        public float ControlHeight => ConfigurationGuiTemplates.ControlHeight(_templates.Button);

        /// <summary>
        /// Creates a single-line text in the style of the row names.
        /// </summary>
        public TMP_Text CreateLabel(Transform parent, string text)
        {
            var label = ConfigurationGuiTemplates.CreateText(parent, "Label", _templates.Label);
            label.text = text ?? "";
            return label;
        }

        /// <summary>
        /// Creates a copy of the game's button with a label in the style of the row names.
        /// </summary>
        public Button CreateButton(Transform parent, string label, UnityAction onClick)
        {
            var button = ConfigurationGuiTemplates.Clone(_templates.Button, parent, "Button");
            ConfigurationGuiTemplates.SetLabel(button, label ?? "");
            ConfigurationGuiTemplates.SetFixedButtonLabel(button, _templates.Label);
            ConfigurationGuiTemplates.AddLayout(button.gameObject, minHeight: ControlHeight,
                preferredHeight: ControlHeight);
            button.onClick.AddListener(onClick);

            button.interactable = _interactable;
            _selectables.Add(button);
            return button;
        }

        /// <summary>
        /// Creates a single-line text field like the built-in text editors' fields.
        /// <paramref name="editFinished"/> receives the text on Enter and when the field loses focus (pass
        /// <c>_ =&gt; { }</c> to read the text only when needed); Escape
        /// puts back the text it had when editing started. Returns null when the game offers no text field
        /// to copy.
        /// </summary>
        public ConfigurationGuiInputField CreateTextField(Transform parent, UnityAction<string> editFinished)
        {
            if (_templates.InputField == null) return null;

            var field = ConfigurationGuiInputField.Create(_templates, parent, "TextField", editFinished);
            var height = ConfigurationGuiTemplates.ControlHeight(_templates.InputField);
            ConfigurationGuiTemplates.AddLayout(field.Input.gameObject, minHeight: height, preferredHeight: height);

            field.Input.interactable = _interactable;
            _fields.Add(field);
            _selectables.Add(field.Input);
            return field;
        }

        /// <summary>
        /// Creates a container that lays out its children left to right, 8 units apart and centered
        /// vertically, at their preferred widths; give a child a flexible width with
        /// <see cref="SetLayout"/> to fill the line.
        /// </summary>
        public RectTransform CreateRow(Transform parent)
        {
            var row = ConfigurationGuiTemplates.CreateRect("Row", parent);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = RowSpacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return row;
        }

        /// <summary>
        /// Creates a container that lays out its children top to bottom at its full width, 4 units apart.
        /// </summary>
        public RectTransform CreateColumn(Transform parent)
        {
            var column = ConfigurationGuiTemplates.CreateRect("Column", parent);
            var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = Spacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return column;
        }

        /// <summary>
        /// Sets how a row or column sizes <paramref name="target"/>. A negative value leaves that size to
        /// the target's content; <paramref name="height"/> sets both the minimum and the preferred height.
        /// </summary>
        public void SetLayout(GameObject target, float minWidth = -1f, float preferredWidth = -1f,
            float flexibleWidth = -1f, float height = -1f)
        {
            ConfigurationGuiTemplates.AddLayout(target, minWidth, height, preferredWidth, height, flexibleWidth);
        }

        internal void SetInteractable(bool interactable)
        {
            _interactable = interactable;
            // A widget the editor destroyed, such as a removed line, reads as null.
            foreach (var selectable in _selectables)
                if (selectable != null)
                    selectable.interactable = interactable;
        }

        internal bool ConsumesEscape()
        {
            foreach (var field in _fields)
                if (field.Input != null && field.ConsumesEscape())
                    return true;

            return false;
        }
    }
}
