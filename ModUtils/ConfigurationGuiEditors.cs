using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModUtils
{
    /// <summary>
    /// Value editor shown in one row of a mod panel.
    /// </summary>
    internal abstract class ConfigurationGuiEditor
    {
        protected ConfigurationGuiEditor(ConfigEntryBase entry)
        {
            Entry = entry;
        }

        protected ConfigEntryBase Entry { get; }

        /// <summary>
        /// The value the editor shows and builds on: the panel's pending value, else the entry's.
        /// </summary>
        protected object Value => Pending.Get(Entry);

        /// <summary>
        /// The open panel's pending values, which the editor stages its changes in.
        /// </summary>
        private protected ConfigurationGuiPendingValues Pending { get; private set; }

        /// <summary>
        /// False when the editor can only display the value.
        /// </summary>
        internal virtual bool IsEditable => true;

        /// <summary>
        /// Height of the editor's first line, which the row aligns its name with. Zero means the whole
        /// height returned by <see cref="Build"/>.
        /// </summary>
        internal float LineHeight { get; private protected set; }

        /// <summary>
        /// Picks the editor for an entry, which stages its changes in <paramref name="pending"/>: an editor
        /// registered by a mod first, then a built-in one. Types without an editor, and entries whose
        /// registered editor could not be created, get a read-only row.
        /// </summary>
        internal static ConfigurationGuiEditor Create(ConfigEntryBase entry, ConfigurationGuiPendingValues pending,
            ConfigurationGuiTemplates templates)
        {
            var editor = Select(entry, pending.PluginName, templates);
            editor.Pending = pending;
            return editor;
        }

        /// <summary>
        /// The editor shown for an entry whose own editor failed.
        /// </summary>
        internal static ConfigurationGuiEditor CreateReadOnly(ConfigEntryBase entry,
            ConfigurationGuiPendingValues pending)
        {
            return new ConfigurationGuiReadOnlyEditor(entry) { Pending = pending };
        }

        private static ConfigurationGuiEditor Select(ConfigEntryBase entry, string pluginName,
            ConfigurationGuiTemplates templates)
        {
            if (!ConfigurationGui.TryCreateCustomEditor(entry, pluginName, out var customEditor))
                return new ConfigurationGuiReadOnlyEditor(entry);
            if (customEditor != null)
                return new ConfigurationGuiCustomEditorHost(entry, customEditor);

            var type = entry.SettingType;
            var acceptableValues = entry.Description?.AcceptableValues;

            if (type == typeof(bool) && templates.Toggle != null)
                return new ConfigurationGuiToggleEditor(entry);

            if (type == typeof(int) && acceptableValues is AcceptableValueRange<int> intRange &&
                templates.Slider != null && templates.InputField != null)
                return new ConfigurationGuiSliderEditor(entry, intRange.MinValue, intRange.MaxValue, true);

            if (type == typeof(float) && acceptableValues is AcceptableValueRange<float> floatRange &&
                templates.Slider != null && templates.InputField != null)
                return new ConfigurationGuiSliderEditor(entry, floatRange.MinValue, floatRange.MaxValue, false);

            if ((type == typeof(int) || type == typeof(float) || type == typeof(string)) &&
                templates.InputField != null)
                return new ConfigurationGuiTextEditor(entry, false);

            if (type == typeof(KeyboardShortcut) && templates.Button != null)
                return new ConfigurationGuiKeyEditor(entry, true);

            if (type == typeof(KeyCode) && templates.Button != null)
                return new ConfigurationGuiKeyEditor(entry, false);

            if (type.IsEnum)
            {
                if (!type.IsDefined(typeof(FlagsAttribute), false))
                {
                    if (templates.Dropdown != null)
                        return new ConfigurationGuiDropdownEditor(entry);
                }
                else if (templates.Toggle != null)
                {
                    return new ConfigurationGuiFlagsEditor(entry);
                }
            }

            if (type == typeof(StringList))
            {
                var drawer = entry.Description?.Tags?.OfType<ConfigurationManagerAttributes>().FirstOrDefault()
                                  ?.CustomDrawer;
                if (!ConfigurationCustomDrawer.IsMultiSelect(drawer))
                {
                    if (templates.InputField != null)
                        return new ConfigurationGuiTextEditor(entry, true);
                }
                else if (ConfigurationCustomDrawer.TryGetMultiSelectSource<string>(drawer, out var allElementSupplier,
                             out var labelGenerator) && templates.Toggle != null && templates.Button != null &&
                         type.GetConstructor(new[] { typeof(IEnumerable<string>) }) != null)
                {
                    return new ConfigurationGuiMultiSelectEditor(entry, allElementSupplier, labelGenerator);
                }
            }

            return new ConfigurationGuiReadOnlyEditor(entry);
        }

        /// <summary>
        /// Label of an enum value: its DescriptionAttribute (translated for LocalizedDescriptionAttribute),
        /// else its name.
        /// </summary>
        protected static string GetEnumLabel(Type type, object value)
        {
            var name = Enum.GetName(type, value);
            var member = name == null ? null : type.GetMember(name).FirstOrDefault();
            var attribute = member?.GetCustomAttributes(typeof(DescriptionAttribute), false)
                                  .OfType<DescriptionAttribute>().FirstOrDefault();
            return attribute?.Description ?? value.ToString();
        }

        /// <summary>
        /// Creates the editor's widgets inside <paramref name="slot"/> and returns the height they need.
        /// </summary>
        internal abstract float Build(RectTransform slot, ConfigurationGuiTemplates templates);

        /// <summary>
        /// Shows <see cref="Value"/> without raising the widget's change event.
        /// </summary>
        internal abstract void Refresh();

        internal abstract void SetInteractable(bool interactable);

        /// <summary>
        /// True while the editor handles the current Escape press itself (for example a focused text
        /// field or a running key capture), so the panel must stay open.
        /// </summary>
        internal virtual bool ConsumesEscape()
        {
            return false;
        }

        internal void ResetToDefault()
        {
            Commit(Entry.DefaultValue);
        }

        /// <summary>
        /// Stages the value in the panel, which clamps it as the entry would, then shows the staged value.
        /// The entry is written when the panel's Apply is pressed.
        /// </summary>
        protected void Commit(object value)
        {
            try
            {
                Pending.Stage(Entry, value);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(
                    $"[ModUtils] Failed to stage [{Entry.Definition.Section}:{Entry.Definition.Key}]: {e}");
            }

            Refresh();
        }
    }

    internal sealed class ConfigurationGuiToggleEditor : ConfigurationGuiEditor
    {
        private Toggle _toggle;

        internal ConfigurationGuiToggleEditor(ConfigEntryBase entry) : base(entry)
        {
        }

        internal override float Build(RectTransform slot, ConfigurationGuiTemplates templates)
        {
            _toggle = ConfigurationGuiTemplates.Clone(templates.Toggle, slot, "Toggle");
            // The row shows its own name, so the copied vanilla label is emptied; the check mark stays.
            foreach (var text in _toggle.GetComponentsInChildren<TMP_Text>(true))
                if (text != _toggle.graphic)
                    text.text = "";

            var height = ConfigurationGuiTemplates.ControlHeight(templates.Toggle);
            ConfigurationGuiTemplates.FillWidth((RectTransform)_toggle.transform, height);

            Refresh();
            _toggle.onValueChanged.AddListener(value => Commit(value));
            return height;
        }

        internal override void Refresh()
        {
            _toggle.SetIsOnWithoutNotify((bool)Value);
        }

        internal override void SetInteractable(bool interactable)
        {
            _toggle.interactable = interactable;
        }
    }

    /// <summary>
    /// Slider for ranged int and float entries, with a number field beside it. A pointer drag only
    /// updates the field's text and is staged on release; every other slider change (a click on the
    /// track, arrow keys) is staged at once. A number typed into the field is staged on submit or when
    /// the field loses focus and is clamped to the range then, never while it is typed; while it is
    /// typed, the handle shows it, at the nearer end for a value outside the range. Text that is not a
    /// number leaves the handle in place and brings back the shown value when the edit ends. Escape in
    /// the field cancels the edit only.
    /// </summary>
    internal sealed class ConfigurationGuiSliderEditor : ConfigurationGuiEditor
    {
        private const float ValueWidth = 110f;
        private const float ValueSpacing = 8f;

        private readonly float _min;
        private readonly float _max;
        private readonly bool _wholeNumbers;
        private ConfigurationGuiInputField _field;
        private ConfigurationGuiPointerHold _hold;
        private string _shown = "";
        private Slider _slider;

        internal ConfigurationGuiSliderEditor(ConfigEntryBase entry, float min, float max, bool wholeNumbers) :
            base(entry)
        {
            _min = min;
            _max = max;
            _wholeNumbers = wholeNumbers;
        }

        internal override float Build(RectTransform slot, ConfigurationGuiTemplates templates)
        {
            var height = ConfigurationGuiTemplates.ControlHeight(templates.Slider);

            _slider = ConfigurationGuiTemplates.Clone(templates.Slider, slot, "Slider");
            foreach (var text in _slider.GetComponentsInChildren<TMP_Text>(true))
                text.text = "";

            var sliderRect = (RectTransform)_slider.transform;
            ConfigurationGuiTemplates.FillWidth(sliderRect, height);
            sliderRect.offsetMax = new Vector2(-(ValueWidth + ValueSpacing), sliderRect.offsetMax.y);

            // Left and right must move the value instead of moving the selection to the Reset button.
            var navigation = _slider.navigation;
            navigation.mode = Navigation.Mode.Vertical;
            _slider.navigation = navigation;

            _slider.wholeNumbers = _wholeNumbers;
            _slider.minValue = _min;
            _slider.maxValue = _max;

            _field = ConfigurationGuiInputField.Create(templates, slot, "Value", OnEditFinished);
            var valueRect = (RectTransform)_field.Input.transform;
            valueRect.anchorMin = new Vector2(1f, 0.5f);
            valueRect.anchorMax = new Vector2(1f, 0.5f);
            valueRect.pivot = new Vector2(1f, 0.5f);
            valueRect.sizeDelta = new Vector2(ValueWidth, height);
            valueRect.anchoredPosition = Vector2.zero;

            _hold = _slider.gameObject.AddComponent<ConfigurationGuiPointerHold>();
            _hold.Released = OnReleased;

            Refresh();
            _slider.onValueChanged.AddListener(OnValueChanged);
            _field.Input.onValueChanged.AddListener(OnTextChanged);
            return height;
        }

        internal override void Refresh()
        {
            var boxed = Value;
            if (_wholeNumbers)
            {
                var value = (int)boxed;
                _slider.SetValueWithoutNotify(value);
                _shown = value.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                // A typed float keeps its precision, so the field shows the value as stored.
                var value = (float)boxed;
                _slider.SetValueWithoutNotify(value);
                _shown = value.ToString(CultureInfo.InvariantCulture);
            }

            _field.Input.SetTextWithoutNotify(_shown);
        }

        internal override void SetInteractable(bool interactable)
        {
            _slider.interactable = interactable;
            _field.Input.interactable = interactable;
        }

        internal override bool ConsumesEscape()
        {
            return _field.ConsumesEscape();
        }

        private void OnValueChanged(float value)
        {
            // Shows the value that the release will stage.
            _field.Input.SetTextWithoutNotify(_wholeNumbers
                ? Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture)
                : FloorToHundredths(value).ToString(CultureInfo.InvariantCulture));

            if (_hold.IsHeld) return;
            Commit(ToEntryValue(value));
        }

        private void OnReleased()
        {
            if (_slider.interactable)
                Commit(ToEntryValue(_slider.value));
        }

        /// <summary>
        /// Moves the handle to the typed number without staging it or touching the text. Escape puts the
        /// original text back through the field's text setter, which raises this again, so the handle
        /// returns with it.
        /// </summary>
        private void OnTextChanged(string text)
        {
            if (ConfigurationGuiInputField.TryParseNumber(text, _wholeNumbers, out var number))
                _slider.SetValueWithoutNotify(Mathf.Clamp(_wholeNumbers ? (int)number : (float)number, _min, _max));
        }

        /// <summary>
        /// Stages the typed number once; the entry's range clamps it. Typing only moves the handle, so a
        /// value is never staged, clamped or restaged halfway.
        /// </summary>
        private void OnEditFinished(string text)
        {
            if (string.Equals(text, _shown, StringComparison.Ordinal))
            {
                Refresh();
                return;
            }

            if (ConfigurationGuiInputField.TryParseNumber(text, _wholeNumbers, out var number))
                Commit(number);
            else
                Refresh();
        }

        private object ToEntryValue(float value)
        {
            if (_wholeNumbers)
                return Mathf.RoundToInt(value);
            return FloorToHundredths(value);
        }

        private static float FloorToHundredths(float value)
        {
            // The small offset keeps values such as 0.29f, stored as 0.28999..., from dropping a step.
            return Mathf.Floor(value * 100f + 0.001f) / 100f;
        }
    }

    internal sealed class ConfigurationGuiDropdownEditor : ConfigurationGuiEditor
    {
        private readonly List<object> _values = new List<object>();
        private TMP_Dropdown _dropdown;

        internal ConfigurationGuiDropdownEditor(ConfigEntryBase entry) : base(entry)
        {
        }

        internal override float Build(RectTransform slot, ConfigurationGuiTemplates templates)
        {
            _dropdown = ConfigurationGuiTemplates.Clone(templates.Dropdown, slot, "Dropdown");
            HideVanillaTitle(_dropdown);
            var height = ConfigurationGuiTemplates.ControlHeight(templates.Dropdown);
            ConfigurationGuiTemplates.FillWidth((RectTransform)_dropdown.transform, height);

            // The vanilla caption shrinks with the lower control; it keeps the row label's size on one line
            // instead, in its place beside the arrow, and a caption too long for it is cut off.
            var caption = _dropdown.captionText;
            caption.enableAutoSizing = false;
            caption.fontSize = templates.Label.fontSize;
            caption.textWrappingMode = TextWrappingModes.NoWrap;
            caption.overflowMode = TextOverflowModes.Truncate;

            var type = Entry.SettingType;
            var acceptableValues = Entry.Description?.AcceptableValues;
            foreach (var value in Enum.GetValues(type))
                if (acceptableValues == null || acceptableValues.IsValid(value))
                    _values.Add(value);

            _dropdown.ClearOptions();
            _dropdown.AddOptions(_values.Select(x => GetEnumLabel(type, x)).ToList());

            Refresh();
            _dropdown.onValueChanged.AddListener(OnValueChanged);
            return height;
        }

        /// <summary>
        /// Hides texts that the copied dropdown carries besides its own, such as its vanilla row title,
        /// which would show next to the row name. The caption, the option texts and the texts holding
        /// them stay.
        /// </summary>
        private static void HideVanillaTitle(TMP_Dropdown dropdown)
        {
            var kept = new List<Transform>();
            if (dropdown.captionText != null) kept.Add(dropdown.captionText.transform);
            if (dropdown.itemText != null) kept.Add(dropdown.itemText.transform);

            foreach (var text in dropdown.GetComponentsInChildren<TMP_Text>(true))
            {
                var transform = text.transform;
                if (dropdown.template != null && transform.IsChildOf(dropdown.template)) continue;
                if (kept.Any(x => x.IsChildOf(transform))) continue;

                // Hiding the object would also hide images below it, such as the arrow.
                var holdsGraphics = transform.GetComponentsInChildren<Graphic>(true)
                                             .Any(x => x != text && !(x is TMP_SubMeshUI));
                if (holdsGraphics)
                    text.text = "";
                else
                    text.gameObject.SetActive(false);
            }
        }

        internal override void Refresh()
        {
            var current = Value;
            var index = _values.IndexOf(current);
            if (index < 0)
            {
                // Keeps the shown value truthful when the entry holds a value outside the offered options.
                _values.Add(current);
                _dropdown.AddOptions(new List<string> { GetEnumLabel(Entry.SettingType, current) });
                index = _values.Count - 1;
            }

            _dropdown.SetValueWithoutNotify(index);
        }

        internal override void SetInteractable(bool interactable)
        {
            _dropdown.interactable = interactable;
        }

        private void OnValueChanged(int index)
        {
            if (index >= 0 && index < _values.Count)
                Commit(_values[index]);
        }
    }

    /// <summary>
    /// Shows the serialized value of an entry that has no editor, and a swatch of a Color value.
    /// </summary>
    internal sealed class ConfigurationGuiReadOnlyEditor : ConfigurationGuiEditor
    {
        private const int MaxLength = 120;

        private ConfigurationGuiSwatch _swatch;
        private TMP_Text _text;

        internal ConfigurationGuiReadOnlyEditor(ConfigEntryBase entry) : base(entry)
        {
        }

        internal override bool IsEditable => false;

        internal override float Build(RectTransform slot, ConfigurationGuiTemplates templates)
        {
            _text = ConfigurationGuiTemplates.CreateText(slot, "Value", templates.Label);
            var textRect = (RectTransform)_text.transform;
            ConfigurationGuiTemplates.Stretch(textRect, 0f, 0f, 0f, 0f);
            if (Entry.SettingType == typeof(Color))
                _swatch = ConfigurationGuiSwatch.Create(slot, textRect, ConfigurationGuiTemplates.DefaultControlHeight);

            Refresh();
            return ConfigurationGuiTemplates.DefaultControlHeight;
        }

        internal override void Refresh()
        {
            string value;
            var current = Value;
            try
            {
                value = TomlTypeConverter.ConvertToString(current, Entry.SettingType);
            }
            catch (Exception)
            {
                value = current?.ToString();
            }

            value = (value ?? "").Replace('\r', ' ').Replace('\n', ' ');
            _text.text = value.Length > MaxLength ? value.Substring(0, MaxLength) + "..." : value;
            _swatch?.Show((Color)current);
        }

        internal override void SetInteractable(bool interactable)
        {
        }
    }

    /// <summary>
    /// Box at the right end of an editor slot that shows a colour: a dark frame around the colour.
    /// </summary>
    internal sealed class ConfigurationGuiSwatch
    {
        private const float Margin = 3f;
        private const float Spacing = 8f;
        private const float FrameWidth = 2f;

        private static readonly Color FrameColor = new Color(0.05f, 0.05f, 0.05f, 0.9f);
        private static readonly Regex ColorCodePattern = new Regex("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$");

        private readonly Image _fill;

        private ConfigurationGuiSwatch(Image fill)
        {
            _fill = fill;
        }

        /// <summary>
        /// True for a hex colour code with "#", the default value that marks a string entry as a colour.
        /// </summary>
        internal static bool IsColorCode(object value)
        {
            return value is string text && ColorCodePattern.IsMatch(text);
        }

        /// <summary>
        /// Reads a colour code the way Automatics reads its colour settings: surrounding spaces are
        /// ignored and a missing "#" is added.
        /// </summary>
        internal static bool TryParseColor(string value, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var input = value.Trim();
            if (!input.StartsWith("#", StringComparison.Ordinal))
                input = "#" + input;

            return ColorUtility.TryParseHtmlString(input, out color);
        }

        /// <summary>
        /// Adds a square swatch, a little smaller than <paramref name="height"/>, at the right end of
        /// <paramref name="slot"/> and narrows <paramref name="beside"/> to leave room for it.
        /// </summary>
        internal static ConfigurationGuiSwatch Create(RectTransform slot, RectTransform beside, float height)
        {
            var size = height - 2f * Margin;
            var frame = ConfigurationGuiTemplates.CreateRect("Swatch", slot);
            frame.anchorMin = new Vector2(1f, 0.5f);
            frame.anchorMax = new Vector2(1f, 0.5f);
            frame.pivot = new Vector2(1f, 0.5f);
            frame.sizeDelta = new Vector2(size, size);
            frame.anchoredPosition = Vector2.zero;
            var frameImage = frame.gameObject.AddComponent<Image>();
            frameImage.color = FrameColor;
            frameImage.raycastTarget = false;

            var fill = ConfigurationGuiTemplates.CreateRect("Fill", frame);
            ConfigurationGuiTemplates.Stretch(fill, FrameWidth, FrameWidth, FrameWidth, FrameWidth);
            var fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.color = Color.clear;
            fillImage.raycastTarget = false;

            beside.offsetMax = new Vector2(-(size + Spacing), beside.offsetMax.y);
            return new ConfigurationGuiSwatch(fillImage);
        }

        internal void Show(Color color)
        {
            _fill.color = color;
        }

        /// <summary>
        /// Shows the colour of a colour code, or an empty frame when the text is not one.
        /// </summary>
        internal void Show(string text)
        {
            _fill.color = TryParseColor(text, out var color) ? color : Color.clear;
        }
    }

    /// <summary>
    /// Tracks whether the left pointer button is held on a widget and reports its release.
    /// </summary>
    internal sealed class ConfigurationGuiPointerHold : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        internal Action Released;

        internal bool IsHeld { get; private set; }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                IsHeld = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!IsHeld || eventData.button != PointerEventData.InputButton.Left) return;

            IsHeld = false;
            Released?.Invoke();
        }

        private void OnDisable()
        {
            IsHeld = false;
        }
    }
}
