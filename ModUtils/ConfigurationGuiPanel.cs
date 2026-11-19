using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
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
    internal sealed class ConfigurationGuiPanel : ConfigurationGuiEntryView
    {
        // As tall as a single-line row with its spacing, so headers stand out from the controls.
        private const float SectionHeaderHeight = RowMinHeight + SectionRowSpacing;

        // Kept by this ModUtils copy for the game session, so every panel it opens starts with the user's
        // last choice.
        private static bool _showAdvanced;

        private readonly List<Section> _sections = new List<Section>();
        private readonly Queue<(Section Section, Row Row)> _unbuiltRows = new Queue<(Section, Row)>();
        private GameObject _list;
        private GameObject _panels;
        private Button _resetAll;
        private ConfigurationGuiInputField _search;
        private bool _searching;
        private string[] _searchTerms = Array.Empty<string>();

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
        internal override bool IsEscapeConsumed()
        {
            if (_search != null && _search.ConsumesEscape())
                return true;

            return base.IsEscapeConsumed();
        }

        /// <summary>
        /// Returns to the mod list.
        /// </summary>
        protected override void Close()
        {
            if (IsClosed) return;
            IsClosed = true;

            if (_panels != null) _panels.SetActive(false);
            if (_list != null) _list.SetActive(true);
            Destroy(gameObject);

            var settings = Settings.instance;
            if (settings != null && settings.isActiveAndEnabled)
                settings.StartCoroutine(ReleaseNavigationNextFrame(_list));
        }

        /// <summary>
        /// Stages the default value of every row that can be edited, including rows that the advanced
        /// toggle or the search hides. Read-only rows, also those of types without an editor, and entries
        /// without a row (hidden by Browsable false, or the one that lists the plugin on the Mods tab) keep
        /// their values. The button is available once every row is built.
        /// </summary>
        private void ResetAll()
        {
            foreach (var row in Rows)
                if (row.Editable)
                    Pending.Stage(row.Entry, row.Entry.DefaultValue);

            RefreshEditors();
        }

        /// <summary>
        /// Shows the rows whose name, category, description, section or key contain every space-separated
        /// term of the search text, ignoring case. Advanced rows show only with the advanced toggle on, or
        /// while searching, as ConfigurationManager searches them too. A section shows while it has a shown
        /// row, and every section shows expanded while searching. Rows built later follow the same rule.
        /// Display only; pending edits stay.
        /// </summary>
        private void ApplyFilter()
        {
            var text = _search != null ? _search.Input.text ?? "" : "";
            _searchTerms = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var searching = _searchTerms.Length > 0;
            // Every change of the results shows each matching section expanded again.
            if (searching)
                foreach (var section in _sections)
                    section.CollapsedInSearch = false;
            _searching = searching;

            foreach (var row in Rows)
                row.Object.SetActive(IsShown(row));

            foreach (var section in _sections)
                ShowSection(section);
        }

        protected override bool IsShown(Row row)
        {
            return (!row.Advanced || _showAdvanced || _searching) && row.Matches(_searchTerms);
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

        protected override void Update()
        {
            if (_unbuiltRows.Count > 0)
                BuildRows();

            base.Update();
        }

        private void OnDisable()
        {
            if (IsClosed) return;
            IsClosed = true;

            // Another tab hid the page; pending edits are dropped with the panel, and so is a discard popup
            // it was showing, which would otherwise stay open while Settings takes Escape again. Hierarchy
            // changes are not allowed while the page is being deactivated, so this happens on the next frame.
            var settings = Settings.instance;
            if (settings != null && settings.isActiveAndEnabled)
                settings.StartCoroutine(RestoreListNextFrame(gameObject, _panels, _list, IsPrompting));
            IsPrompting = false;
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
            Pending = new ConfigurationGuiPendingValues(plugin.Name);
            Templates = templates;
            var rows = CreateRows(plugin);

            LayOutStack((int)Padding);
            CreateHeader($"{plugin.Name} v{plugin.Version}", null);
            CreateStagedApplyHint();
            CreateFilter(templates, rows.Any(x => x.Advanced));
            var content = CreateScrollView(transform, templates);
            CreateDescriptionArea();
            CreateFooter();
            CreateFullDescription();
            CreateSections(rows, content, templates);

            ApplyFilter();
            UpdateApplyButton();
            Pending.Changed = UpdateApplyButton;
            BuildRows();
        }

        private void BuildRows()
        {
            var filled = new List<Section>();
            for (var i = 0; i < RowsPerFrame && _unbuiltRows.Count > 0; i++)
            {
                var (section, row) = _unbuiltRows.Dequeue();
                CreateRow(row, section.Body);
                section.Rows.Add(row);
                if (!filled.Contains(section))
                    filled.Add(section);
            }

            foreach (var section in filled)
                ShowSection(section);
            _resetAll.interactable = _unbuiltRows.Count == 0;
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
                _search = CreateSearchField(filter, "Search");
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

        private void CreateFooter()
        {
            var footer = CreateFooterLine(out var style, out var width, out var height);

            // Reset all sits at the left end, apart from Apply and Back at the right end.
            _resetAll = CreateFooterButton(style, footer, "ResetAll", ConfigurationGui.ResetAllKey, width, height,
                ResetAll);
            var gap = ConfigurationGuiTemplates.CreateRect("Gap", footer);
            ConfigurationGuiTemplates.AddLayout(gap.gameObject, flexibleWidth: 1f);
            CreateApplyButton(footer, style, width, height);
            CreateFooterButton(style, footer, "Back", ConfigurationGui.BackKey, width, height, Leave);
        }

        private static List<Row> CreateRows(ConfigurationGuiPlugin plugin)
        {
            var rows = new List<Row>();
            foreach (var pair in plugin.Config)
            {
                // The entry that lists the plugin on the Mods tab is changed in ConfigurationManager or in the
                // config file, not here.
                if (ReferenceEquals(pair.Value, plugin.ShowOnSettingsScreen)) continue;

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
                    _unbuiltRows.Enqueue((section, row));

                _sections.Add(section);
            }
        }

        private Section CreateSection(string category, RectTransform content, ConfigurationGuiTemplates templates)
        {
            var header = ConfigurationGuiTemplates.Clone(templates.Button, content, "Section");
            ConfigurationGuiTemplates.AddLayout(header.gameObject, minHeight: SectionHeaderHeight,
                preferredHeight: SectionHeaderHeight);

            // The title has the row names' size and left edge, in bold.
            var headerLabel = ConfigurationGuiTemplates.SetFixedButtonLabel(header, templates.Label,
                alignment: TextAlignmentOptions.MidlineLeft);
            if (headerLabel != null)
            {
                headerLabel.richText = false;
                headerLabel.fontStyle = FontStyles.Bold;
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
    }
}
