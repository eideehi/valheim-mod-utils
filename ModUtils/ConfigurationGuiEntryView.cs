using System;
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
    /// What a mod's panel on the Mods tab and the entry window share: the title lines, one row per entry with
    /// its editor and Reset button, the description area below the rows, edits that stay pending until
    /// Apply, and leaving through a button or Escape, which asks first whether to drop pending edits.
    /// </summary>
    internal abstract class ConfigurationGuiEntryView : MonoBehaviour
    {
        protected const float Padding = 8f;
        protected const float Spacing = 6f;
        protected const int RowPadding = 2;
        protected const float RowMinHeight = ConfigurationGuiTemplates.DefaultControlHeight + 2 * RowPadding;
        protected const float RowSpacing = 8f;
        protected const float SectionRowSpacing = 2f;
        protected const float EditorMinWidth = 200f;
        protected const float ScrollMinHeight = 120f;
        // Building every row of a long mod in one frame stops the game for a moment, so a view opens with its
        // first rows and adds the rest a batch per frame.
        protected const int RowsPerFrame = 16;
        private const int WheelStepRows = 3;
        private const float NameMinWidth = 120f;
        private const float ResetWidth = 120f;
        private const float ScrollbarSpacing = 4f;
        private const float DescriptionLines = 3.6f;
        private const float UnfoldDelay = 1.5f;
        private const float FooterButtonMinWidth = 120f;
        private const float FooterButtonMaxWidth = 260f;

        private Button _apply;
        private TMP_Text _description;
        private ConfigurationGuiFullDescription _fullDescription;
        private ConfigurationGuiRowHover _hoveredRow;
        private float _unfoldTime = float.PositiveInfinity;

        /// <summary>
        /// The rows built so far.
        /// </summary>
        protected List<Row> Rows { get; } = new List<Row>();

        /// <summary>
        /// The edits that stay in the view until Apply. Set before any row is built.
        /// </summary>
        private protected ConfigurationGuiPendingValues Pending { get; set; }

        /// <summary>
        /// The vanilla widgets that rows and buttons copy. Set before any row is built.
        /// </summary>
        protected ConfigurationGuiTemplates Templates { get; set; }

        /// <summary>
        /// True once the view has closed; Escape and the answer of a discard prompt then do nothing.
        /// </summary>
        protected bool IsClosed { get; set; }

        /// <summary>
        /// True while the discard prompt that this view showed is open.
        /// </summary>
        protected bool IsPrompting { get; set; }

        /// <summary>
        /// The row the description area shows, or null when no row is hovered.
        /// </summary>
        internal ConfigurationGuiRowHover HoveredRow => _hoveredRow;

        /// <summary>
        /// True when an editor in this view handles the current Escape press itself, so Escape must not
        /// leave the view.
        /// </summary>
        internal virtual bool IsEscapeConsumed()
        {
            foreach (var row in Rows)
                if (row.Editor.ConsumesEscape())
                    return true;

            return false;
        }

        /// <summary>
        /// Shows the description of <paramref name="row"/> in the area below the list, or clears the area
        /// when <paramref name="row"/> is null. A description that the area cuts off unfolds in full over
        /// the list once the pointer has stayed on the row for <see cref="UnfoldDelay"/> seconds; any
        /// hover change or mouse button press folds it again, and it does not unfold while a text field is
        /// being edited.
        /// </summary>
        internal void ShowDescription(ConfigurationGuiRowHover row)
        {
            var description = row != null ? row.Description : "";
            if (_description != null)
                _description.text = description ?? "";

            _hoveredRow = row;
            // Unscaled, because the pause menu stops game time.
            _unfoldTime = string.IsNullOrEmpty(description)
                ? float.PositiveInfinity
                : Time.unscaledTime + UnfoldDelay;
            _fullDescription?.Hide();
        }

        private void UnfoldDescription()
        {
            _unfoldTime = float.PositiveInfinity;
            // Typing into a focused field involves no press, so the overlay must not cover the list then.
            if (IsEditingText())
                return;

            // The text was set on an earlier frame; this makes sure the cut-off state is current.
            _description.ForceMeshUpdate();
            if (_description.isTextTruncated)
                _fullDescription?.Show(_description.rectTransform, _hoveredRow.RowName, _description.text);
        }

        private static bool IsEditingText()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var input = selected != null ? selected.GetComponent<TMP_InputField>() : null;
            return input != null && input.isFocused;
        }

        // Pressing a button on a row means the user is working with it, so its description stays folded
        // until the pointer enters a row anew.
        private void FoldDescription()
        {
            _unfoldTime = float.PositiveInfinity;
            _fullDescription?.Hide();
        }

        /// <summary>
        /// Closes the view at once; pending edits are dropped with it.
        /// </summary>
        protected abstract void Close();

        /// <summary>
        /// Whether the discard prompt covers the screen behind it, so that nothing behind it takes clicks.
        /// </summary>
        protected virtual bool CoversBackgroundWhilePrompting => false;

        /// <summary>
        /// The leave button, Escape and the gamepad back button: closes the view at once without pending
        /// edits, else asks through the game's Yes/No popup whether to drop them.
        /// </summary>
        protected void Leave()
        {
            // The popup is answered with the same keys, so a press while it is shown, or on the frame it
            // closed, belongs to the popup.
            if (IsClosed || UnifiedPopup.IsVisible() || UnifiedPopup.WasVisibleThisFrame()) return;

            if (!Pending.HasPending)
            {
                Close();
                return;
            }

            var header = ConfigurationGui.Translate(ConfigurationGui.DiscardHeaderKey);
            var question = ConfigurationGui.Translate(ConfigurationGui.DiscardQuestionKey);
            IsPrompting = true;
            UnifiedPopup.Push(new YesNoPopup(header, question, OnDiscardConfirmed, OnDiscardCancelled, false,
                CoversBackgroundWhilePrompting));
        }

        // Both answers always dismiss the popup, as the game's own answers do, and touch the view only
        // while it is still open.
        private void OnDiscardConfirmed()
        {
            UnifiedPopup.Pop();
            IsPrompting = false;
            if (this == null || IsClosed) return;

            Pending.Discard();
            Close();
        }

        private void OnDiscardCancelled()
        {
            UnifiedPopup.Pop();
            IsPrompting = false;
        }

        /// <summary>
        /// Takes down the discard prompt that this view showed, for a view that closes without an answer.
        /// </summary>
        protected void DismissPrompt()
        {
            if (!IsPrompting) return;

            IsPrompting = false;
            if (UnifiedPopup.IsVisible())
                UnifiedPopup.Pop();
        }

        protected virtual void Apply()
        {
            Pending.Apply();
            RefreshEditors();
        }

        protected void RefreshEditors()
        {
            foreach (var row in Rows)
                row.Editor.Refresh();
        }

        /// <summary>
        /// Whether a newly built row shows. Every row shows unless a subclass filters them.
        /// </summary>
        protected virtual bool IsShown(Row row)
        {
            return true;
        }

        protected void UpdateApplyButton()
        {
            if (_apply != null)
                _apply.interactable = Pending.HasPending;
        }

        protected virtual void Update()
        {
            if (Time.unscaledTime >= _unfoldTime)
                UnfoldDescription();

            if (_hoveredRow != null && (ZInput.GetKeyDown(KeyCode.Mouse0) || ZInput.GetKeyDown(KeyCode.Mouse1) ||
                                        ZInput.GetKeyDown(KeyCode.Mouse2)))
                FoldDescription();

            // The gamepad back button leaves like Escape, as it closes the game's own dialogs.
            if (IsClosed || !(ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB")) ||
                IsEscapeConsumed())
                return;

            Leave();
        }

        /// <summary>
        /// Stacks the view's parts from top to bottom at its full width, <paramref name="padding"/> inside
        /// its edges.
        /// </summary>
        protected void LayOutStack(int padding)
        {
            var layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.spacing = Spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        /// <summary>
        /// The lines above the rows: the title in bold, <paramref name="note"/> when it is not empty, and
        /// the hint that changes are saved on Apply.
        /// </summary>
        protected void CreateHeader(string title, string note)
        {
            var label = Templates.Label;

            var titleText = ConfigurationGuiTemplates.CreateText(transform, "Title", label, 1.25f);
            titleText.text = title;
            titleText.fontStyle = FontStyles.Bold;

            if (!string.IsNullOrEmpty(note))
            {
                var noteText = ConfigurationGuiTemplates.CreateText(transform, "Note", label, 0.9f);
                noteText.textWrappingMode = TextWrappingModes.Normal;
                noteText.text = note;
            }

            var hint = ConfigurationGuiTemplates.CreateText(transform, "Hint", label, 0.85f);
            hint.text = ConfigurationGui.Translate(ConfigurationGui.StagedApplyHintKey);
            hint.color = new Color(hint.color.r, hint.color.g, hint.color.b, hint.color.a * 0.75f);
        }

        /// <summary>
        /// The scroll area that takes the height left over by the view's other parts, and returns the
        /// content the rows go into, which grows with them.
        /// </summary>
        protected static RectTransform CreateScrollView(Transform parent, ConfigurationGuiTemplates templates)
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
                var scrollbarWidth = width >= 1f ? Mathf.Min(width, 32f) : 12f;
                scrollbarRect.sizeDelta = new Vector2(scrollbarWidth, 0f);
                scrollbarRect.anchoredPosition = Vector2.zero;
                scrollbar.direction = Scrollbar.Direction.BottomToTop;
                var navigation = scrollbar.navigation;
                navigation.mode = Navigation.Mode.None;
                scrollbar.navigation = navigation;

                scroll.verticalScrollbar = scrollbar;
                // Inset the viewport instead of letting the scroll rect expand it: expanding makes every layout
                // pass of the view lay out the whole list again.
                ConfigurationGuiTemplates.Stretch(viewport, 0f, scrollbarWidth + ScrollbarSpacing, 0f, 0f);
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            }

            return content;
        }

        /// <summary>
        /// The area below the list that shows the hovered row's description.
        /// </summary>
        protected void CreateDescriptionArea()
        {
            // Every row hover changes the text. Inside an area without a layout group, the change does not lay
            // out the whole view with its list again.
            var descriptionArea = ConfigurationGuiTemplates.CreateRect("DescriptionArea", transform);
            _description = ConfigurationGuiTemplates.CreateText(descriptionArea, "Description", Templates.Label, 0.9f);
            ConfigurationGuiTemplates.Stretch(_description.rectTransform, 0f, 0f, 0f, 0f);
            _description.textWrappingMode = TextWrappingModes.Normal;
            _description.alignment = TextAlignmentOptions.TopLeft;
            var descriptionHeight = _description.fontSize * DescriptionLines;
            ConfigurationGuiTemplates.AddLayout(descriptionArea.gameObject, minHeight: descriptionHeight,
                preferredHeight: descriptionHeight);
        }

        /// <summary>
        /// The line of buttons below the description area, which lines up its buttons at its right end. Its
        /// buttons copy <paramref name="style"/> at the given size.
        /// </summary>
        protected RectTransform CreateFooterLine(out Button style, out float buttonWidth, out float buttonHeight)
        {
            style = Templates.BackButton != null ? Templates.BackButton : Templates.Button;
            buttonHeight = ConfigurationGuiTemplates.VanillaControlHeight(style);
            buttonWidth = Mathf.Clamp(((RectTransform)style.transform).rect.width, FooterButtonMinWidth,
                FooterButtonMaxWidth);

            var footer = ConfigurationGuiTemplates.CreateRect("Footer", transform);
            var layout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = Spacing;
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            ConfigurationGuiTemplates.AddLayout(footer.gameObject, minHeight: buttonHeight,
                preferredHeight: buttonHeight);
            return footer;
        }

        protected static Button CreateFooterButton(Button style, Transform footer, string name, string labelKey,
            float width, float height, UnityAction onClick)
        {
            var button = ConfigurationGuiTemplates.Clone(style, footer, name);
            ConfigurationGuiTemplates.SetLabel(button, ConfigurationGui.Translate(labelKey));
            ConfigurationGuiTemplates.AddLayout(button.gameObject, preferredWidth: width, preferredHeight: height);
            button.onClick.AddListener(onClick);
            return button;
        }

        /// <summary>
        /// The Apply button, available only while edits are pending once <see cref="UpdateApplyButton"/>
        /// runs.
        /// </summary>
        protected void CreateApplyButton(Transform footer, Button style, float width, float height)
        {
            _apply = CreateFooterButton(style, footer, "Apply", ConfigurationGui.ApplyKey, width, height, Apply);
        }

        /// <summary>
        /// The overlay that unfolds a cut-off description; add it after the view's other parts, so it draws
        /// over them.
        /// </summary>
        protected void CreateFullDescription()
        {
            _fullDescription = ConfigurationGuiFullDescription.Create(transform, Templates);
        }

        protected void CreateRow(Row row, RectTransform body)
        {
            var templates = Templates;
            // The name identifies the entry, so automated checks can find a row by section and key.
            var definition = row.Entry.Definition;
            var rowRect = ConfigurationGuiTemplates.CreateRect($"Row:{definition.Section}/{definition.Key}", body);
            // Inactive until complete, so its widgets are copied and its texts created on an inactive object
            // also while the view is shown; the view then decides whether it shows.
            rowRect.gameObject.SetActive(false);
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
            hover.View = this;
            hover.RowName = row.Name;
            hover.Description = row.Description;

            var name = ConfigurationGuiTemplates.CreateText(rowRect, "Name", templates.Label);
            name.text = row.Name;

            var slot = ConfigurationGuiTemplates.CreateRect("Editor", rowRect);
            var editor = ConfigurationGuiEditor.Create(row.Entry, Pending, templates);
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
            Rows.Add(row);

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

            rowRect.gameObject.SetActive(IsShown(row));
        }

        internal static ConfigurationManagerAttributes GetAttributes(ConfigEntryBase entry)
        {
            return entry.Description?.Tags?.OfType<ConfigurationManagerAttributes>().FirstOrDefault();
        }

        internal sealed class Row
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

            /// <summary>
            /// The row of <paramref name="entry"/>, or null for an entry that is never shown
            /// (<c>Browsable = false</c>).
            /// </summary>
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
    /// Shows a row's description in its view while the pointer is over the row.
    /// </summary>
    internal sealed class ConfigurationGuiRowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal string Description;
        internal string RowName;
        internal ConfigurationGuiEntryView View;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (View != null)
                View.ShowDescription(this);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (View != null)
                View.ShowDescription(null);
        }

        // A row that becomes inactive gets no pointer exit, so it clears its own description.
        private void OnDisable()
        {
            if (View != null && View.HoveredRow == this)
                View.ShowDescription(null);
        }
    }
}
