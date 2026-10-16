using System;
using System.Globalization;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace ModUtils
{
    /// <summary>
    /// Single-line text field for string, int and float entries without a range, and for lists edited
    /// in their serialized form. The text is written on submit or when the field loses focus; text
    /// that cannot be converted brings back the current value. Escape cancels the edit only. A string
    /// entry whose default value is a colour code also shows a swatch that follows the text while it is
    /// typed.
    /// </summary>
    internal sealed class ConfigurationGuiTextEditor : ConfigurationGuiEditor
    {
        private readonly bool _serialized;
        private ConfigurationGuiInputField _field;
        private string _shown = "";
        private ConfigurationGuiSwatch _swatch;

        internal ConfigurationGuiTextEditor(ConfigEntryBase entry, bool serialized) : base(entry)
        {
            _serialized = serialized;
        }

        internal override float Build(RectTransform slot, ConfigurationGuiTemplates templates)
        {
            var height = ConfigurationGuiTemplates.ControlHeight(templates.InputField);

            _field = ConfigurationGuiInputField.Create(templates, slot, "Input", OnEditFinished);
            var fieldRect = (RectTransform)_field.Input.transform;
            ConfigurationGuiTemplates.FillWidth(fieldRect, height);
            if (!_serialized && ConfigurationGuiSwatch.IsColorCode(Entry.DefaultValue))
            {
                _swatch = ConfigurationGuiSwatch.Create(slot, fieldRect, height);
                // Display only; the text is still written when the edit ends. Escape puts the original
                // text back through the field's text setter, which raises this event too.
                _field.Input.onValueChanged.AddListener(_swatch.Show);
            }

            Refresh();
            return height;
        }

        internal override void Refresh()
        {
            _shown = Format();
            _field.Input.SetTextWithoutNotify(_shown);
            _swatch?.Show(_shown);
        }

        internal override void SetInteractable(bool interactable)
        {
            _field.Input.interactable = interactable;
        }

        internal override bool ConsumesEscape()
        {
            return _field.ConsumesEscape();
        }

        private string Format()
        {
            if (_serialized)
                return Entry.GetSerializedValue();

            var value = Entry.BoxedValue;
            switch (value)
            {
                case int number:
                    return number.ToString(CultureInfo.InvariantCulture);
                case float number:
                    return number.ToString(CultureInfo.InvariantCulture);
                default:
                    return value as string ?? "";
            }
        }

        private void OnEditFinished(string text)
        {
            // Unchanged text is not written again, so leaving the field never rewrites a value that its
            // text shows rounded.
            if (string.Equals(text, _shown, StringComparison.Ordinal))
            {
                Refresh();
                return;
            }

            if (_serialized)
            {
                // BepInEx logs and ignores text that does not convert; Refresh then shows the kept value.
                Entry.SetSerializedValue(text);
                Refresh();
                return;
            }

            var type = Entry.SettingType;
            if (type == typeof(int) || type == typeof(float))
            {
                if (ConfigurationGuiInputField.TryParseNumber(text, type == typeof(int), out var number))
                    Commit(number);
                else
                    Refresh();
            }
            else
            {
                Commit(text);
            }
        }
    }

    /// <summary>
    /// Single-line copy of the Feedback text field and the parts that the editors typing into one share.
    /// </summary>
    internal sealed class ConfigurationGuiInputField
    {
        private int _endEditFrame = -1;

        private ConfigurationGuiInputField(TMP_InputField input)
        {
            Input = input;
        }

        internal TMP_InputField Input { get; }

        /// <summary>
        /// Copies the Feedback field under <paramref name="parent"/>, leaving its layout to the caller.
        /// <paramref name="editFinished"/> receives the text on submit and when the field loses focus.
        /// </summary>
        internal static ConfigurationGuiInputField Create(ConfigurationGuiTemplates templates, Transform parent,
            string name, UnityAction<string> editFinished)
        {
            var input = ConfigurationGuiTemplates.Clone(templates.InputField, parent, name);
            input.contentType = TMP_InputField.ContentType.Standard;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = 0;
            input.richText = false;
            input.readOnly = false;
            input.restoreOriginalTextOnEscape = true;
            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.text = "";
                SetFixedFontSize(placeholder, templates.Label.fontSize);
            }

            // The Feedback field is a large multi-line box, so its text takes the row label's size and is
            // centered vertically on the single line.
            if (input.textComponent != null)
            {
                SetFixedFontSize(input.textComponent, templates.Label.fontSize);
                input.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
            }

            var field = new ConfigurationGuiInputField(input);
            input.onSubmit.AddListener(editFinished);
            input.onDeselect.AddListener(editFinished);
            input.onEndEdit.AddListener(_ => field._endEditFrame = Time.frameCount);
            return field;
        }

        /// <summary>
        /// Reads an int or a float typed in the invariant culture.
        /// </summary>
        internal static bool TryParseNumber(string text, bool wholeNumber, out object value)
        {
            if (wholeNumber)
            {
                var parsed = int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var number);
                value = number;
                return parsed;
            }
            else
            {
                var parsed = float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var number);
                value = number;
                return parsed;
            }
        }

        /// <summary>
        /// True while the field is focused. The field also leaves editing on the Escape press itself, which
        /// may happen before the panel looks at that press in the same frame.
        /// </summary>
        internal bool ConsumesEscape()
        {
            return Input.isFocused || _endEditFrame == Time.frameCount;
        }

        private static void SetFixedFontSize(TMP_Text text, float size)
        {
            text.enableAutoSizing = false;
            text.fontSize = size;
        }
    }
}
