using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModUtils
{
    /// <summary>
    /// One toggle per non-zero value of a [Flags] enum, like the Flags drawer: a value is checked when all
    /// of its bits are set, checking it sets them and unchecking it clears them.
    /// </summary>
    internal sealed class ConfigurationGuiFlagsEditor : ConfigurationGuiEditor
    {
        private const int Columns = 2;
        private const float Spacing = 4f;

        private readonly List<long> _bits = new List<long>();
        private List<Toggle> _toggles;

        internal ConfigurationGuiFlagsEditor(ConfigEntryBase entry) : base(entry)
        {
        }

        internal override float Build(RectTransform slot, ConfigurationGuiTemplates templates)
        {
            var type = Entry.SettingType;
            var acceptableValues = Entry.Description?.AcceptableValues;
            var labels = new List<string>();
            foreach (var value in Enum.GetValues(type))
            {
                if (acceptableValues != null && !acceptableValues.IsValid(value)) continue;

                var bits = Convert.ToInt64(value);
                if (bits == 0) continue;

                _bits.Add(bits);
                labels.Add(GetEnumLabel(type, value));
            }

            var itemHeight = ConfigurationGuiTemplates.ControlHeight(templates.Toggle);
            _toggles = ConfigurationGuiTemplates.CreateToggleGrid(slot, templates.Toggle, templates.Label, labels,
                Columns, itemHeight, Spacing);
            for (var i = 0; i < _toggles.Count; i++)
            {
                var bits = _bits[i];
                _toggles[i].onValueChanged.AddListener(isOn => OnToggled(bits, isOn));
            }

            Refresh();
            LineHeight = itemHeight;
            return Mathf.Max(itemHeight,
                ConfigurationGuiTemplates.ToggleGridHeight(_toggles.Count, Columns, itemHeight, Spacing));
        }

        internal override void Refresh()
        {
            var current = Convert.ToInt64(Entry.BoxedValue);
            for (var i = 0; i < _toggles.Count; i++)
                _toggles[i].SetIsOnWithoutNotify((current & _bits[i]) == _bits[i]);
        }

        internal override void SetInteractable(bool interactable)
        {
            foreach (var toggle in _toggles)
                toggle.interactable = interactable;
        }

        private void OnToggled(long bits, bool isOn)
        {
            var current = Convert.ToInt64(Entry.BoxedValue);
            var value = isOn ? current | bits : current & ~bits;
            Commit(Enum.ToObject(Entry.SettingType, value));
        }
    }

    /// <summary>
    /// Native form of a StringList drawn by <see cref="ConfigurationCustomDrawer.MultiSelect{T}"/>: a
    /// summary button that expands into one toggle per element the drawer offers. Like that drawer, a
    /// change copies the whole current list, so selected values the drawer does not offer are kept.
    /// </summary>
    internal sealed class ConfigurationGuiMultiSelectEditor : ConfigurationGuiEditor
    {
        private const int Columns = 2;
        private const float Spacing = 4f;

        private readonly Func<IEnumerable<string>> _allElementSupplier;
        private readonly List<string> _elements = new List<string>();
        private readonly Dictionary<string, string> _labels = new Dictionary<string, string>();
        private readonly Func<string, string> _labelGenerator;
        private float _collapsedHeight = -1f;
        private ConstructorInfo _constructor;
        private bool _expanded;
        private float _headerHeight;
        private bool _interactable = true;
        private RectTransform _list;
        private float _listHeight;
        private RectTransform _slot;
        private Button _summary;
        private ConfigurationGuiTemplates _templates;
        private List<Toggle> _toggles;

        internal ConfigurationGuiMultiSelectEditor(ConfigEntryBase entry,
            Func<IEnumerable<string>> allElementSupplier, Func<string, string> labelGenerator) : base(entry)
        {
            _allElementSupplier = allElementSupplier;
            _labelGenerator = labelGenerator;
        }

        internal override float Build(RectTransform slot, ConfigurationGuiTemplates templates)
        {
            _slot = slot;
            _templates = templates;
            _constructor = Entry.SettingType.GetConstructor(new[] { typeof(IEnumerable<string>) });

            // The drawer's functions are called again for every build, so registry and language changes show.
            var acceptableValues = Entry.Description?.AcceptableValues;
            foreach (var element in _allElementSupplier.Invoke())
            {
                if (acceptableValues != null && !acceptableValues.IsValid(element)) continue;

                _elements.Add(element);
                _labels[element] = _labelGenerator(element) ?? element;
            }

            var layout = slot.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = Spacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            _headerHeight = ConfigurationGuiTemplates.ControlHeight(templates.Button);
            _summary = ConfigurationGuiTemplates.Clone(templates.Button, slot, "Summary");
            ConfigurationGuiTemplates.AddLayout(_summary.gameObject, minHeight: _headerHeight,
                preferredHeight: _headerHeight);
            var summaryLabel = ConfigurationGuiTemplates.SetFixedButtonLabel(_summary, templates.Label,
                alignment: TextAlignmentOptions.MidlineLeft);
            if (summaryLabel != null)
                summaryLabel.richText = false;

            _summary.onClick.AddListener(ToggleExpanded);

            _list = ConfigurationGuiTemplates.CreateRect("Elements", slot);
            _list.gameObject.SetActive(false);

            Refresh();
            LineHeight = _headerHeight;
            return _headerHeight;
        }

        internal override void Refresh()
        {
            var selected = new HashSet<string>((IEnumerable<string>)Entry.BoxedValue);
            if (_toggles != null)
                for (var i = 0; i < _toggles.Count; i++)
                    _toggles[i].SetIsOnWithoutNotify(selected.Contains(_elements[i]));

            var names = _elements.Where(selected.Contains).Select(x => _labels[x])
                                 .Concat(selected.Where(x => !_labels.ContainsKey(x)));
            var summary = (_expanded ? "- " : "+ ") + selected.Count;
            if (selected.Count > 0)
                summary += ": " + string.Join(", ", names);
            ConfigurationGuiTemplates.SetLabel(_summary, summary);
        }

        internal override void SetInteractable(bool interactable)
        {
            // The summary stays usable so that a read-only selection can still be viewed.
            _interactable = interactable;
            if (_toggles != null)
                foreach (var toggle in _toggles)
                    toggle.interactable = interactable;
        }

        private void ToggleExpanded()
        {
            _expanded = !_expanded;
            if (_expanded && _toggles == null)
            {
                var itemHeight = ConfigurationGuiTemplates.ControlHeight(_templates.Toggle);
                _toggles = ConfigurationGuiTemplates.CreateToggleGrid(_list, _templates.Toggle, _templates.Label,
                    _elements.Select(x => _labels[x]).ToList(), Columns, itemHeight, Spacing);
                for (var i = 0; i < _toggles.Count; i++)
                {
                    var element = _elements[i];
                    _toggles[i].interactable = _interactable;
                    _toggles[i].onValueChanged.AddListener(isOn => OnToggled(element, isOn));
                }

                _listHeight = ConfigurationGuiTemplates.ToggleGridHeight(_toggles.Count, Columns, itemHeight,
                    Spacing);
            }

            var showList = _expanded && _toggles.Count > 0;
            _list.gameObject.SetActive(showList);

            // The row sizes the slot from its layout element, so the slot grows and shrinks through it.
            var slotLayout = _slot.GetComponent<LayoutElement>();
            if (slotLayout != null)
            {
                if (_collapsedHeight < 0f)
                    _collapsedHeight = slotLayout.preferredHeight;

                var height = Mathf.Max(_collapsedHeight, showList ? _headerHeight + Spacing + _listHeight : 0f);
                slotLayout.minHeight = height;
                slotLayout.preferredHeight = height;
            }

            Refresh();
        }

        private void OnToggled(string element, bool isOn)
        {
            // Same construction as the MultiSelect drawer: start from a copy of the whole current list.
            var elements = (ICollection<string>)_constructor.Invoke(new[] { Entry.BoxedValue });
            if (isOn)
                elements.Add(element);
            else
                elements.Remove(element);

            Commit(elements);
        }
    }
}
