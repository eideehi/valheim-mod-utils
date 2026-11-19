using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModUtils
{
    /// <summary>
    /// What every ModUtils view shares: the title lines, a scroll view for its rows, rows that show their
    /// description in the area below the list while the pointer is over them, a footer line of buttons, and
    /// leaving through a button, Escape or the gamepad back button.
    /// </summary>
    internal abstract class ConfigurationGuiView : MonoBehaviour
    {
        protected const float Padding = 8f;
        protected const float Spacing = 6f;
        protected const int RowPadding = 2;
        protected const float RowMinHeight = ConfigurationGuiTemplates.DefaultControlHeight + 2 * RowPadding;
        protected const float RowSpacing = 8f;
        protected const float SectionRowSpacing = 2f;
        protected const float EditorMinWidth = 200f;
        internal const float ScrollMinHeight = 120f;
        // Building every row of a long mod in one frame stops the game for a moment, so a view opens with its
        // first rows and adds the rest a batch per frame.
        protected const int RowsPerFrame = 16;
        private const int WheelStepRows = 3;
        private const float NameMinWidth = 120f;
        private const float ScrollbarSpacing = 4f;
        private const float DescriptionLines = 3.6f;
        private const float UnfoldDelay = 1.5f;
        private const float FooterButtonMinWidth = 120f;
        private const float FooterButtonMaxWidth = 260f;

        private TMP_Text _description;
        private ConfigurationGuiFullDescription _fullDescription;
        private ConfigurationGuiRowHover _hoveredRow;
        private float _unfoldTime = float.PositiveInfinity;

        /// <summary>
        /// The vanilla widgets that rows and buttons copy. Set before any row is built.
        /// </summary>
        protected ConfigurationGuiTemplates Templates { get; set; }

        /// <summary>
        /// True once the view has closed; Escape and the answer of a prompt then do nothing.
        /// </summary>
        internal bool IsClosed { get; private protected set; }

        /// <summary>
        /// The row the description area shows, or null when no row is hovered.
        /// </summary>
        internal ConfigurationGuiRowHover HoveredRow => _hoveredRow;

        /// <summary>
        /// True when a part of this view handles the current Escape press itself, so Escape must not
        /// leave the view.
        /// </summary>
        internal virtual bool IsEscapeConsumed()
        {
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
        /// Closes the view at once.
        /// </summary>
        protected abstract void Close();

        /// <summary>
        /// The leave button, Escape and the gamepad back button.
        /// </summary>
        protected abstract void Leave();

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
        /// The lines above the rows: the title in bold, and <paramref name="note"/> when it is not empty.
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
        }

        /// <summary>
        /// A field that filters the view's rows on every change of its text, so the end of an edit needs no
        /// action. It shows the search placeholder while empty, and Escape leaves the field and keeps its
        /// text. Its layout is left to the caller.
        /// </summary>
        protected ConfigurationGuiInputField CreateSearchField(Transform parent, string name)
        {
            var field = ConfigurationGuiInputField.Create(Templates, parent, name, _ => { });
            field.Input.restoreOriginalTextOnEscape = false;
            if (field.Input.placeholder is TMP_Text placeholder)
                placeholder.text = ConfigurationGui.Translate(ConfigurationGui.SearchPlaceholderKey);
            return field;
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
        /// The object in a window's scroll content that holds its rows, spaced as in a section of the Mods
        /// tab.
        /// </summary>
        protected static RectTransform CreateRowsBody(RectTransform content)
        {
            var body = ConfigurationGuiTemplates.CreateRect("Rows", content);
            var layout = body.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = SectionRowSpacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return body;
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
        /// The overlay that unfolds a cut-off description; add it after the view's other parts, so it draws
        /// over them.
        /// </summary>
        protected void CreateFullDescription()
        {
            _fullDescription = ConfigurationGuiFullDescription.Create(transform, Templates);
        }

        /// <summary>
        /// Starts a row named <paramref name="objectName"/> in <paramref name="body"/> with its name label
        /// on the left. The whole row reports the pointer, so the description area shows
        /// <paramref name="description"/> while the pointer is over it. The row is inactive, so its widgets
        /// are copied and its texts created on an inactive object also while the view is shown; the caller
        /// adds its value with <see cref="LayOutRowParts"/> and then activates it.
        /// </summary>
        protected RectTransform CreateRowObject(string objectName, RectTransform body, string name,
            string description, out TMP_Text nameText)
        {
            var rowRect = ConfigurationGuiTemplates.CreateRect(objectName, body);
            rowRect.gameObject.SetActive(false);
            // Transparent, but makes the whole row report the pointer for the description area.
            rowRect.gameObject.AddComponent<Image>().color = Color.clear;
            var layout = rowRect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, RowPadding, RowPadding);
            layout.spacing = RowSpacing;
            // Editors can span several lines; the name and what follows the editor line up with its first one.
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            ConfigurationGuiTemplates.AddLayout(rowRect.gameObject, minHeight: RowMinHeight);

            var hover = rowRect.gameObject.AddComponent<ConfigurationGuiRowHover>();
            hover.View = this;
            hover.RowName = name;
            hover.Description = description;

            nameText = ConfigurationGuiTemplates.CreateText(rowRect, "Name", Templates.Label);
            nameText.text = name;
            return rowRect;
        }

        /// <summary>
        /// Splits a row's width between its name and the slot that holds its value. The name lines up with
        /// the value's first line, <paramref name="lineHeight"/> high, and the slot is
        /// <paramref name="slotHeight"/> high.
        /// </summary>
        protected static void LayOutRowParts(TMP_Text name, RectTransform slot, float lineHeight, float slotHeight)
        {
            // A preferred width of 0 keeps the name's text length and the editor's content out of the width
            // split, so the editor column starts at the same place in every row.
            ConfigurationGuiTemplates.AddLayout(name.gameObject, minWidth: NameMinWidth, minHeight: lineHeight,
                preferredWidth: 0f, flexibleWidth: 1f);
            ConfigurationGuiTemplates.AddLayout(slot.gameObject, minWidth: EditorMinWidth, minHeight: slotHeight,
                preferredWidth: 0f, preferredHeight: slotHeight, flexibleWidth: 1f);
        }
    }

    /// <summary>
    /// Shows a row's description in its view while the pointer is over the row.
    /// </summary>
    internal sealed class ConfigurationGuiRowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal string Description;
        internal string RowName;
        internal ConfigurationGuiView View;

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
