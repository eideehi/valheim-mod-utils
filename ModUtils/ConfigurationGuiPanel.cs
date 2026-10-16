using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModUtils
{
    /// <summary>
    /// One plugin's configuration panel on the Mods page. It replaces the mod list while open, blocks the
    /// Settings navigation, and closes on Back, on Escape or the gamepad back button, or when another tab
    /// hides the page.
    /// </summary>
    internal sealed class ConfigurationGuiPanel : MonoBehaviour
    {
        private const float Padding = 8f;
        private const float Spacing = 6f;
        private const float RowMinHeight = 36f;
        private const int RowPadding = 2;
        private const float RowSpacing = 8f;
        private const float NameMinWidth = 120f;
        private const float EditorMinWidth = 200f;
        private const float ResetWidth = 120f;
        private const float ScrollMinHeight = 120f;
        private const float ScrollbarSpacing = 4f;
        private const float DescriptionLines = 3.6f;
        private const float BackMinWidth = 120f;
        private const float BackMaxWidth = 260f;

        private readonly List<ConfigurationGuiEditor> _editors = new List<ConfigurationGuiEditor>();
        private bool _closed;
        private TMP_Text _description;
        private GameObject _list;
        private GameObject _panels;

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
        /// True when an editor in this panel handles the current Escape press itself, so Escape must not
        /// close the panel.
        /// </summary>
        internal bool IsEscapeConsumed()
        {
            foreach (var editor in _editors)
                if (editor.ConsumesEscape())
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

        private void Update()
        {
            // The gamepad back button closes the panel like Escape, as it closes the game's own dialogs.
            if (_closed || !(ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB")) ||
                IsEscapeConsumed())
                return;

            Close();
        }

        private void OnDisable()
        {
            if (_closed) return;
            _closed = true;

            // Another tab hid the page. Hierarchy changes are not allowed while the page is being
            // deactivated, so the list comes back on the next frame.
            var settings = Settings.instance;
            if (settings != null && settings.isActiveAndEnabled)
                settings.StartCoroutine(RestoreListNextFrame(gameObject, _panels, _list));
        }

        // Releasing in the same frame as the Escape press would let Settings close on that press.
        private static IEnumerator ReleaseNavigationNextFrame(GameObject list)
        {
            yield return null;
            ReleaseNavigation(list);
        }

        private static IEnumerator RestoreListNextFrame(GameObject panel, GameObject panels, GameObject list)
        {
            yield return null;

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
            hint.text = ConfigurationGui.Translate(ConfigurationGui.ApplyHintKey);
            hint.color = new Color(hint.color.r, hint.color.g, hint.color.b, hint.color.a * 0.75f);

            var content = CreateScrollView(transform, templates);

            _description = ConfigurationGuiTemplates.CreateText(transform, "Description", label, 0.9f);
            _description.textWrappingMode = TextWrappingModes.Normal;
            _description.alignment = TextAlignmentOptions.TopLeft;
            var descriptionHeight = _description.fontSize * DescriptionLines;
            ConfigurationGuiTemplates.AddLayout(_description.gameObject, minHeight: descriptionHeight,
                preferredHeight: descriptionHeight);

            CreateFooter(templates);
            CreateSections(plugin.Config, content, templates);
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
            scroll.scrollSensitivity = templates.ScrollSensitivity;

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
            var height = ConfigurationGuiTemplates.ControlHeight(source);
            var width = Mathf.Clamp(((RectTransform)source.transform).rect.width, BackMinWidth, BackMaxWidth);

            var footer = ConfigurationGuiTemplates.CreateRect("Footer", transform);
            var layout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            ConfigurationGuiTemplates.AddLayout(footer.gameObject, minHeight: height, preferredHeight: height);

            var back = ConfigurationGuiTemplates.Clone(source, footer, "Back");
            ConfigurationGuiTemplates.SetLabel(back, ConfigurationGui.Translate(ConfigurationGui.BackKey));
            ConfigurationGuiTemplates.AddLayout(back.gameObject, preferredWidth: width, preferredHeight: height);
            back.onClick.AddListener(Close);
        }

        private void CreateSections(ConfigFile config, RectTransform content, ConfigurationGuiTemplates templates)
        {
            var rows = new List<Row>();
            foreach (var pair in config)
            {
                var row = Row.Create(pair.Value, rows.Count);
                if (row != null)
                    rows.Add(row);
            }

            // GroupBy keeps the order in which each category first appears.
            foreach (var section in rows.GroupBy(x => x.Category))
            {
                var body = CreateSection(section.Key, content, templates);
                foreach (var row in section.OrderByDescending(x => x.Order).ThenBy(x => x.Index))
                    CreateRow(row, body, templates);
            }
        }

        private static RectTransform CreateSection(string category, RectTransform content,
            ConfigurationGuiTemplates templates)
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
            layout.spacing = 2f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            header.onClick.AddListener(() =>
            {
                var expanded = !body.gameObject.activeSelf;
                body.gameObject.SetActive(expanded);
                if (headerLabel != null)
                    headerLabel.text = (expanded ? "- " : "+ ") + category;
            });

            return body;
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
            var editor = ConfigurationGuiEditor.Create(row.Entry, templates);
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
            _editors.Add(editor);

            if (editor.IsEditable && !row.ReadOnly)
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

        private sealed class Row
        {
            private Row(ConfigEntryBase entry, int index, string category, string name, string description,
                int order, bool readOnly)
            {
                Entry = entry;
                Index = index;
                Category = category;
                Name = name;
                Description = description;
                Order = order;
                ReadOnly = readOnly;
            }

            internal ConfigEntryBase Entry { get; }

            internal int Index { get; }

            internal string Category { get; }

            internal string Name { get; }

            internal string Description { get; }

            internal int Order { get; }

            internal bool ReadOnly { get; }

            internal static Row Create(ConfigEntryBase entry, int index)
            {
                var attributes = entry.Description?.Tags?.OfType<ConfigurationManagerAttributes>()
                                      .FirstOrDefault();
                if (attributes?.Browsable == false || attributes?.IsAdvanced == true) return null;

                var definition = entry.Definition;
                var category = string.IsNullOrEmpty(attributes?.Category) ? definition.Section : attributes.Category;
                var name = string.IsNullOrEmpty(attributes?.DispName) ? definition.Key : attributes.DispName;
                var description = string.IsNullOrEmpty(attributes?.Description)
                    ? entry.Description?.Description
                    : attributes.Description;

                return new Row(entry, index, category, name, description ?? "", attributes?.Order ?? 0,
                    attributes?.ReadOnly == true);
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
