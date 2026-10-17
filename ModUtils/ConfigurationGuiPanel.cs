using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModUtils
{
    /// <summary>
    /// One plugin's configuration panel on the Mods page. It replaces the mod list while open and blocks
    /// the Settings navigation. A search field and an advanced toggle filter the rows. Edits stay pending
    /// in the panel until Apply writes them. The panel closes
    /// on Back, on Escape or the gamepad back button, asking first when edits are pending, and without
    /// asking when another tab hides the page; closing drops pending edits.
    /// </summary>
    internal sealed class ConfigurationGuiPanel : MonoBehaviour
    {
        private const float Padding = 8f;
        private const float Spacing = 6f;
        private const float RowMinHeight = ConfigurationGuiTemplates.DefaultControlHeight + 2 * RowPadding;
        private const int RowPadding = 2;
        private const float RowSpacing = 8f;
        private const float SectionRowSpacing = 2f;
        private const int WheelStepRows = 3;
        private const float NameMinWidth = 120f;
        private const float EditorMinWidth = 200f;
        private const float ResetWidth = 120f;
        private const float ScrollMinHeight = 120f;
        private const float ScrollbarSpacing = 4f;
        private const float DescriptionLines = 3.6f;
        private const float BackMinWidth = 120f;
        private const float BackMaxWidth = 260f;

        // Kept by this ModUtils copy for the game session, so every panel it opens starts with the user's
        // last choice.
        private static bool _showAdvanced;

        private readonly List<Row> _rows = new List<Row>();
        private readonly List<Section> _sections = new List<Section>();
        private Button _apply;
        private bool _closed;
        private TMP_Text _description;
        private GameObject _list;
        private GameObject _panels;
        private ConfigurationGuiPendingValues _pending;
        private bool _prompting;
        private ConfigurationGuiInputField _search;
        private bool _searching;

        internal static void Open(ConfigurationGuiPlugin plugin, RectTransform page)
        {
            GameObject panelObject = null;
            try
            {
                var settings = Settings.instance;
                var list = ConfigurationGui.FindChild(page, ConfigurationGui.ListName);
                var panels = ConfigurationGui.FindChild(page, ConfigurationGui.PanelsName);
                if (settings == null || list == null || panels == null) return;

                var templates = ConfigurationGuiTemplates.Find(settings);
                if (templates.Label == null || templates.Button == null)
                {
                    UnityEngine.Debug.LogWarning(
                        "[ModUtils] The settings screen has no label or button to copy. The mod panel cannot be shown.");
                    return;
                }

                var rect = ConfigurationGuiTemplates.CreateRect(plugin.Guid, panels);
                panelObject = rect.gameObject;
                ConfigurationGuiTemplates.Stretch(rect, 0f, 0f, 0f, 0f);

                var panel = panelObject.AddComponent<ConfigurationGuiPanel>();
                panel._list = list.gameObject;
                panel._panels = panels.gameObject;
                panel.Build(plugin, templates);

                list.gameObject.SetActive(false);
                panels.gameObject.SetActive(true);
                settings.BlockNavigation(true);
            }
            catch (Exception e)
            {
                if (panelObject != null)
                    Destroy(panelObject);
                UnityEngine.Debug.LogError($"[ModUtils] Failed to open the configuration panel of {plugin.Name}: {e}");
            }
        }

        /// <summary>
        /// True when the search field or an editor in this panel handles the current Escape press itself, so
        /// Escape must not close the panel.
        /// </summary>
        internal bool IsEscapeConsumed()
        {
            if (_search != null && _search.ConsumesEscape())
                return true;

            foreach (var row in _rows)
                if (row.Editor.ConsumesEscape())
                    return true;

            return false;
        }

        internal void ShowDescription(string description)
        {
            if (_description != null)
                _description.text = description ?? "";
        }

        private void Close()
        {
            if (_closed) return;
            _closed = true;

            if (_panels != null) _panels.SetActive(false);
            if (_list != null) _list.SetActive(true);
            Destroy(gameObject);

            var settings = Settings.instance;
            if (settings != null && settings.isActiveAndEnabled)
                settings.StartCoroutine(ReleaseNavigationNextFrame(_list));
        }

        /// <summary>
        /// Back, Escape and the gamepad back button: returns to the mod list at once without pending edits,
        /// else asks through the game's Yes/No popup whether to drop them.
        /// </summary>
        private void Leave()
        {
            // The popup is answered with the same keys, so a press while it is shown, or on the frame it
            // closed, belongs to the popup.
            if (_closed || UnifiedPopup.IsVisible() || UnifiedPopup.WasVisibleThisFrame()) return;

            if (!_pending.HasPending)
            {
                Close();
                return;
            }

            var header = ConfigurationGui.Translate(ConfigurationGui.DiscardHeaderKey);
            var question = ConfigurationGui.Translate(ConfigurationGui.DiscardQuestionKey);
            _prompting = true;
            UnifiedPopup.Push(new YesNoPopup(header, question, OnDiscardConfirmed, OnDiscardCancelled, false));
        }

        // Both answers always dismiss the popup, as the game's own answers do, and touch the panel only
        // while it is still open.
        private void OnDiscardConfirmed()
        {
            UnifiedPopup.Pop();
            _prompting = false;
            if (this == null || _closed) return;

            _pending.Discard();
            Close();
        }

        private void OnDiscardCancelled()
        {
            UnifiedPopup.Pop();
            _prompting = false;
        }

        private void Apply()
        {
            _pending.Apply();
            RefreshEditors();
        }

        /// <summary>
        /// Stages the default value of every row that can be edited, including rows that the advanced
        /// toggle or the search hides. Read-only rows, also those of types without an editor, and hidden
        /// (Browsable false) entries, which have no row, keep their values.
        /// </summary>
        private void ResetAll()
        {
            foreach (var row in _rows)
                if (row.Editable)
                    _pending.Stage(row.Entry, row.Entry.DefaultValue);

            RefreshEditors();
        }

        private void RefreshEditors()
        {
            foreach (var row in _rows)
                row.Editor.Refresh();
        }

        /// <summary>
        /// Shows the rows whose name, category, description, section or key contain every space-separated
        /// term of the search text, ignoring case. Advanced rows show only with the advanced toggle on, or
        /// while searching, as ConfigurationManager searches them too. A section shows while it has a shown
        /// row, and every section shows expanded while searching. Display only; pending edits stay.
        /// </summary>
        private void ApplyFilter()
        {
            var text = _search != null ? _search.Input.text ?? "" : "";
            var terms = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var searching = terms.Length > 0;
            // Every change of the results shows each matching section expanded again.
            if (searching)
                foreach (var section in _sections)
                    section.CollapsedInSearch = false;
            _searching = searching;

            foreach (var row in _rows)
                row.Object.SetActive((!row.Advanced || _showAdvanced || searching) && row.Matches(terms));

            foreach (var section in _sections)
                ShowSection(section);
        }

        /// <summary>
        /// A header click collapses or expands the section as shown. While searching it changes only the
        /// search results, so clearing the search brings back the state the section had before.
        /// </summary>
        private void ToggleSection(Section section)
        {
            if (_searching)
                section.CollapsedInSearch = !section.CollapsedInSearch;
            else
                section.Collapsed = !section.Collapsed;

            ShowSection(section);
        }

        private void ShowSection(Section section)
        {
            var shown = section.Rows.Any(x => x.Object.activeSelf);
            var expanded = !(_searching ? section.CollapsedInSearch : section.Collapsed);
            section.Header.gameObject.SetActive(shown);
            section.Body.gameObject.SetActive(shown && expanded);
            if (section.HeaderLabel != null)
                section.HeaderLabel.text = (expanded ? "- " : "+ ") + section.Category;
        }

        private void UpdateApplyButton()
        {
            if (_apply != null)
                _apply.interactable = _pending.HasPending;
        }

        private void Update()
        {
            // The gamepad back button closes the panel like Escape, as it closes the game's own dialogs.
            if (_closed || !(ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB")) ||
                IsEscapeConsumed())
                return;

            Leave();
        }

        private void OnDisable()
        {
            if (_closed) return;
            _closed = true;

            // Another tab hid the page; pending edits are dropped with the panel, and so is a discard popup
            // it was showing, which would otherwise stay open while Settings takes Escape again. Hierarchy
            // changes are not allowed while the page is being deactivated, so this happens on the next frame.
            var settings = Settings.instance;
            if (settings != null && settings.isActiveAndEnabled)
                settings.StartCoroutine(RestoreListNextFrame(gameObject, _panels, _list, _prompting));
            _prompting = false;
        }

        // Releasing in the same frame as the Escape press would let Settings close on that press.
        private static IEnumerator ReleaseNavigationNextFrame(GameObject list)
        {
            yield return null;
            ReleaseNavigation(list);
        }

        private static IEnumerator RestoreListNextFrame(GameObject panel, GameObject panels, GameObject list,
            bool prompting)
        {
            yield return null;

            if (prompting && UnifiedPopup.IsVisible()) UnifiedPopup.Pop();
            if (panel != null) Destroy(panel);
            if (panels != null) panels.SetActive(false);
            if (list != null) list.SetActive(true);
            ReleaseNavigation(list);
        }

        private static void ReleaseNavigation(GameObject list)
        {
            // A hidden list means a panel was opened again in the meantime and still needs the block.
            var settings = Settings.instance;
            if (settings != null && (list == null || list.activeSelf))
                settings.BlockNavigation(false);
        }

        private void Build(ConfigurationGuiPlugin plugin, ConfigurationGuiTemplates templates)
        {
            _pending = new ConfigurationGuiPendingValues(plugin.Name);
            var rows = CreateRows(plugin.Config);

            var layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset((int)Padding, (int)Padding, (int)Padding, (int)Padding);
            layout.spacing = Spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var label = templates.Label;

            var title = ConfigurationGuiTemplates.CreateText(transform, "Title", label, 1.25f);
            title.text = $"{plugin.Name} v{plugin.Version}";
            title.fontStyle = FontStyles.Bold;

            var hint = ConfigurationGuiTemplates.CreateText(transform, "Hint", label, 0.85f);
            hint.text = ConfigurationGui.Translate(ConfigurationGui.StagedApplyHintKey);
            hint.color = new Color(hint.color.r, hint.color.g, hint.color.b, hint.color.a * 0.75f);

            CreateFilter(templates, rows.Any(x => x.Advanced));
            var content = CreateScrollView(transform, templates);

            _description = ConfigurationGuiTemplates.CreateText(transform, "Description", label, 0.9f);
            _description.textWrappingMode = TextWrappingModes.Normal;
            _description.alignment = TextAlignmentOptions.TopLeft;
            var descriptionHeight = _description.fontSize * DescriptionLines;
            ConfigurationGuiTemplates.AddLayout(_description.gameObject, minHeight: descriptionHeight,
                preferredHeight: descriptionHeight);

            CreateFooter(templates);
            CreateSections(rows, content, templates);

            ApplyFilter();
            UpdateApplyButton();
            _pending.Changed = UpdateApplyButton;
        }

        /// <summary>
        /// The line above the rows: the search field and, when the mod has advanced entries to show, the
        /// advanced toggle at the right end.
        /// </summary>
        private void CreateFilter(ConfigurationGuiTemplates templates, bool hasAdvanced)
        {
            var showToggle = hasAdvanced && templates.Toggle != null;
            if (templates.InputField == null && !showToggle) return;

            var fieldHeight = ConfigurationGuiTemplates.ControlHeight(templates.InputField);
            var toggleHeight = ConfigurationGuiTemplates.ControlHeight(templates.Toggle);
            var height = Mathf.Max(templates.InputField != null ? fieldHeight : 0f, showToggle ? toggleHeight : 0f);

            var filter = ConfigurationGuiTemplates.CreateRect("Filter", transform);
            var layout = filter.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = RowSpacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            ConfigurationGuiTemplates.AddLayout(filter.gameObject, minHeight: height, preferredHeight: height);

            if (templates.InputField != null)
            {
                // Filters on every change, so the end of an edit needs no action.
                _search = ConfigurationGuiInputField.Create(templates, filter, "Search", _ => { });
                // Escape leaves the field and keeps the search.
                _search.Input.restoreOriginalTextOnEscape = false;
                if (_search.Input.placeholder is TMP_Text placeholder)
                    placeholder.text = ConfigurationGui.Translate(ConfigurationGui.SearchPlaceholderKey);
                ConfigurationGuiTemplates.AddLayout(_search.Input.gameObject, minWidth: EditorMinWidth,
                    preferredWidth: 0f, preferredHeight: fieldHeight, flexibleWidth: 1f);
                _search.Input.onValueChanged.AddListener(_ => ApplyFilter());
            }
            else
            {
                var gap = ConfigurationGuiTemplates.CreateRect("Gap", filter);
                ConfigurationGuiTemplates.AddLayout(gap.gameObject, flexibleWidth: 1f);
            }

            if (!showToggle) return;

            var toggle = ConfigurationGuiTemplates.Clone(templates.Toggle, filter, "ShowAdvanced");
            var label = ConfigurationGuiTemplates.LayOutToggleItem(toggle, templates.Toggle, templates.Label,
                ConfigurationGui.Translate(ConfigurationGui.ShowAdvancedKey));
            // The cell holds the box and the whole label beside it, so the label never needs truncating.
            // Measuring it with an ellipsis mode, before it is shown, makes TextMeshPro warn that the game's
            // font has no ellipsis.
            if (label != null)
                label.overflowMode = TextOverflowModes.Overflow;
            var width = label != null
                ? label.rectTransform.offsetMin.x + label.GetPreferredValues(label.text).x
                : toggleHeight;
            ConfigurationGuiTemplates.AddLayout(toggle.gameObject, minWidth: width, preferredWidth: width,
                preferredHeight: toggleHeight);
            toggle.SetIsOnWithoutNotify(_showAdvanced);
            toggle.onValueChanged.AddListener(isOn =>
            {
                _showAdvanced = isOn;
                ApplyFilter();
            });
        }

        private static RectTransform CreateScrollView(Transform parent, ConfigurationGuiTemplates templates)
        {
            var scrollRect = ConfigurationGuiTemplates.CreateRect("Scroll", parent);
            ConfigurationGuiTemplates.AddLayout(scrollRect.gameObject, minHeight: ScrollMinHeight, flexibleHeight: 1f);
            var scroll = scrollRect.gameObject.AddComponent<ScrollRect>();

            var viewport = ConfigurationGuiTemplates.CreateRect("Viewport", scrollRect);
            ConfigurationGuiTemplates.Stretch(viewport, 0f, 0f, 0f, 0f);
            // Transparent, but catches the pointer between rows so the wheel scrolls everywhere.
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = ConfigurationGuiTemplates.CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = Spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            // One wheel step moves the list by a few single-line rows. The UI input module's wheel delta
            // depends on its configured units per tick, so ask it for the tick count.
            scroll.scrollSensitivity = WheelStepRows * (RowMinHeight + SectionRowSpacing) *
                                       EventSystem.current.currentInputModule
                                           .ConvertPointerEventScrollDeltaToTicks(Vector2.up).y;

            if (templates.Scrollbar != null)
            {
                var width = ((RectTransform)templates.Scrollbar.transform).rect.width;
                var scrollbar = ConfigurationGuiTemplates.Clone(templates.Scrollbar, scrollRect, "Scrollbar");
                var scrollbarRect = (RectTransform)scrollbar.transform;
                scrollbarRect.anchorMin = new Vector2(1f, 0f);
                scrollbarRect.anchorMax = Vector2.one;
                scrollbarRect.pivot = Vector2.one;
                scrollbarRect.sizeDelta = new Vector2(width >= 1f ? Mathf.Min(width, 32f) : 12f, 0f);
                scrollbarRect.anchoredPosition = Vector2.zero;
                scrollbar.direction = Scrollbar.Direction.BottomToTop;
                var navigation = scrollbar.navigation;
                navigation.mode = Navigation.Mode.None;
                scrollbar.navigation = navigation;

                scroll.verticalScrollbar = scrollbar;
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
                scroll.verticalScrollbarSpacing = ScrollbarSpacing;
            }

            return content;
        }

        private void CreateFooter(ConfigurationGuiTemplates templates)
        {
            var source = templates.BackButton != null ? templates.BackButton : templates.Button;
            var height = ConfigurationGuiTemplates.VanillaControlHeight(source);
            var width = Mathf.Clamp(((RectTransform)source.transform).rect.width, BackMinWidth, BackMaxWidth);

            var footer = ConfigurationGuiTemplates.CreateRect("Footer", transform);
            var layout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = Spacing;
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            ConfigurationGuiTemplates.AddLayout(footer.gameObject, minHeight: height, preferredHeight: height);

            // Reset all sits at the left end, apart from Apply and Back at the right end.
            CreateFooterButton(source, footer, "ResetAll", ConfigurationGui.ResetAllKey, width, height, ResetAll);
            var gap = ConfigurationGuiTemplates.CreateRect("Gap", footer);
            ConfigurationGuiTemplates.AddLayout(gap.gameObject, flexibleWidth: 1f);
            _apply = CreateFooterButton(source, footer, "Apply", ConfigurationGui.ApplyKey, width, height, Apply);
            CreateFooterButton(source, footer, "Back", ConfigurationGui.BackKey, width, height, Leave);
        }

        private static Button CreateFooterButton(Button source, Transform footer, string name, string labelKey,
            float width, float height, UnityAction onClick)
        {
            var button = ConfigurationGuiTemplates.Clone(source, footer, name);
            ConfigurationGuiTemplates.SetLabel(button, ConfigurationGui.Translate(labelKey));
            ConfigurationGuiTemplates.AddLayout(button.gameObject, preferredWidth: width, preferredHeight: height);
            button.onClick.AddListener(onClick);
            return button;
        }

        private static List<Row> CreateRows(ConfigFile config)
        {
            var rows = new List<Row>();
            foreach (var pair in config)
            {
                var row = Row.Create(pair.Value, rows.Count);
                if (row != null)
                    rows.Add(row);
            }

            return rows;
        }

        private void CreateSections(List<Row> rows, RectTransform content, ConfigurationGuiTemplates templates)
        {
            // Sections come in the order in which each category first appears among the rows that are not
            // advanced, as when advanced rows were not built; categories with advanced rows only follow.
            var groups = rows.GroupBy(x => x.Category)
                             .OrderBy(x => x.Where(y => !y.Advanced).Select(y => y.Index)
                                            .DefaultIfEmpty(int.MaxValue).Min())
                             .ThenBy(x => x.Min(y => y.Index));
            foreach (var group in groups)
            {
                var section = CreateSection(group.Key, content, templates);
                foreach (var row in group.OrderByDescending(x => x.Order).ThenBy(x => x.Index))
                {
                    CreateRow(row, section.Body, templates);
                    section.Rows.Add(row);
                }

                _sections.Add(section);
            }
        }

        private Section CreateSection(string category, RectTransform content, ConfigurationGuiTemplates templates)
        {
            var header = ConfigurationGuiTemplates.Clone(templates.Button, content, "Section");
            var height = ConfigurationGuiTemplates.ControlHeight(templates.Button);
            ConfigurationGuiTemplates.AddLayout(header.gameObject, minHeight: height, preferredHeight: height);

            var headerLabel = header.GetComponentInChildren<TMP_Text>(true);
            if (headerLabel != null)
            {
                headerLabel.richText = false;
                headerLabel.alignment = TextAlignmentOptions.MidlineLeft;
                headerLabel.margin = new Vector4(12f, 0f, 12f, 0f);
                headerLabel.text = "- " + category;
            }

            var body = ConfigurationGuiTemplates.CreateRect("Rows", content);
            var layout = body.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = SectionRowSpacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var section = new Section(category, header, headerLabel, body);
            header.onClick.AddListener(() => ToggleSection(section));
            return section;
        }

        private void CreateRow(Row row, RectTransform body, ConfigurationGuiTemplates templates)
        {
            // The name identifies the entry, so automated checks can find a row by section and key.
            var definition = row.Entry.Definition;
            var rowRect = ConfigurationGuiTemplates.CreateRect($"Row:{definition.Section}/{definition.Key}", body);
            // Transparent, but makes the whole row report the pointer for the description area.
            rowRect.gameObject.AddComponent<Image>().color = Color.clear;
            var layout = rowRect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, RowPadding, RowPadding);
            layout.spacing = RowSpacing;
            // Editors can span several lines; the name and Reset line up with the first one.
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            ConfigurationGuiTemplates.AddLayout(rowRect.gameObject, minHeight: RowMinHeight);

            var hover = rowRect.gameObject.AddComponent<ConfigurationGuiRowHover>();
            hover.Panel = this;
            hover.Description = row.Description;

            var name = ConfigurationGuiTemplates.CreateText(rowRect, "Name", templates.Label);
            name.text = row.Name;

            var slot = ConfigurationGuiTemplates.CreateRect("Editor", rowRect);
            var editor = ConfigurationGuiEditor.Create(row.Entry, _pending, templates);
            var height = editor.Build(slot, templates);
            var lineHeight = Mathf.Max(editor.LineHeight > 0f ? editor.LineHeight : height,
                RowMinHeight - 2 * RowPadding);
            var slotHeight = Mathf.Max(height, lineHeight);
            // A preferred width of 0 keeps the name's text length and the editor's content out of the width
            // split, so the editor column starts at the same place in every row.
            ConfigurationGuiTemplates.AddLayout(name.gameObject, minWidth: NameMinWidth, minHeight: lineHeight,
                preferredWidth: 0f, flexibleWidth: 1f);
            ConfigurationGuiTemplates.AddLayout(slot.gameObject, minWidth: EditorMinWidth, minHeight: slotHeight,
                preferredWidth: 0f, preferredHeight: slotHeight, flexibleWidth: 1f);
            row.Object = rowRect.gameObject;
            row.Editor = editor;
            row.Editable = editor.IsEditable && !row.ReadOnly;
            _rows.Add(row);

            if (row.Editable)
            {
                var reset = ConfigurationGuiTemplates.Clone(templates.Button, rowRect, "Reset");
                ConfigurationGuiTemplates.SetLabel(reset, ConfigurationGui.Translate(ConfigurationGui.ResetKey));
                ConfigurationGuiTemplates.SetFixedButtonLabel(reset, templates.Label, ResetWidth);

                ConfigurationGuiTemplates.AddLayout(reset.gameObject, minWidth: ResetWidth,
                    preferredWidth: ResetWidth, preferredHeight: lineHeight);
                reset.onClick.AddListener(editor.ResetToDefault);
            }
            else
            {
                editor.SetInteractable(false);
                var note = ConfigurationGuiTemplates.CreateText(rowRect, "ReadOnly", templates.Label, 0.85f);
                note.text = ConfigurationGui.Translate(ConfigurationGui.ReadOnlyKey);
                note.alignment = TextAlignmentOptions.Center;
                ConfigurationGuiTemplates.AddLayout(note.gameObject, minWidth: ResetWidth, minHeight: lineHeight,
                    preferredWidth: ResetWidth);
            }
        }

        internal static ConfigurationManagerAttributes GetAttributes(ConfigEntryBase entry)
        {
            return entry.Description?.Tags?.OfType<ConfigurationManagerAttributes>().FirstOrDefault();
        }

        private sealed class Section
        {
            internal Section(string category, Button header, TMP_Text headerLabel, RectTransform body)
            {
                Category = category;
                Header = header;
                HeaderLabel = headerLabel;
                Body = body;
            }

            internal string Category { get; }

            internal Button Header { get; }

            internal TMP_Text HeaderLabel { get; }

            internal RectTransform Body { get; }

            internal List<Row> Rows { get; } = new List<Row>();

            /// <summary>
            /// The state the user left the section in, shown while the search text is empty.
            /// </summary>
            internal bool Collapsed { get; set; }

            /// <summary>
            /// The state shown while searching; every search starts expanded.
            /// </summary>
            internal bool CollapsedInSearch { get; set; }
        }

        private sealed class Row
        {
            private Row(ConfigEntryBase entry, int index, string category, string name, string description,
                int order, bool readOnly, bool advanced)
            {
                Entry = entry;
                Index = index;
                Category = category;
                Name = name;
                Description = description;
                Order = order;
                ReadOnly = readOnly;
                Advanced = advanced;
            }

            internal ConfigEntryBase Entry { get; }

            internal int Index { get; }

            internal string Category { get; }

            internal string Name { get; }

            internal string Description { get; }

            internal int Order { get; }

            internal bool ReadOnly { get; }

            internal bool Advanced { get; }

            internal GameObject Object { get; set; }

            internal ConfigurationGuiEditor Editor { get; set; }

            /// <summary>
            /// True when the row has an editor that can change the value and the entry is not read-only.
            /// </summary>
            internal bool Editable { get; set; }

            internal static Row Create(ConfigEntryBase entry, int index)
            {
                var attributes = GetAttributes(entry);
                if (attributes?.Browsable == false) return null;

                var definition = entry.Definition;
                var category = string.IsNullOrEmpty(attributes?.Category) ? definition.Section : attributes.Category;
                var name = string.IsNullOrEmpty(attributes?.DispName) ? definition.Key : attributes.DispName;
                var description = string.IsNullOrEmpty(attributes?.Description)
                    ? entry.Description?.Description
                    : attributes.Description;

                return new Row(entry, index, category, name, description ?? "", attributes?.Order ?? 0,
                    attributes?.ReadOnly == true, attributes?.IsAdvanced == true);
            }

            internal bool Matches(string[] terms)
            {
                var definition = Entry.Definition;
                return terms.All(x => Contains(Name, x) || Contains(Category, x) || Contains(Description, x) ||
                                      Contains(definition.Section, x) || Contains(definition.Key, x));
            }

            private static bool Contains(string text, string term)
            {
                return text != null && text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }
    }

    /// <summary>
    /// Shows a row's description in its panel while the pointer is over the row.
    /// </summary>
    internal sealed class ConfigurationGuiRowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal string Description;
        internal ConfigurationGuiPanel Panel;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Panel != null)
                Panel.ShowDescription(Description);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (Panel != null)
                Panel.ShowDescription("");
        }
    }
}
