using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModUtils
{
    /// <summary>
    /// A row's name and whole description in a copy of the vanilla Settings tooltip frame, shown over a
    /// mod's panel when the description area below the list cuts the description off. It covers the area
    /// and grows upward over the list, and it never takes the pointer, so the rows under it keep their
    /// hover and clicks.
    /// </summary>
    internal sealed class ConfigurationGuiFullDescription
    {
        private const int Padding = 12;
        private const float Spacing = 4f;
        private const float TextScale = 0.9f;

        private readonly RectTransform _root;
        private readonly TMP_Text _text;
        private readonly TMP_Text _topic;

        private ConfigurationGuiFullDescription(RectTransform root, TMP_Text topic, TMP_Text text)
        {
            _root = root;
            _topic = topic;
            _text = text;
        }

        /// <summary>
        /// Builds the hidden overlay as the last child of <paramref name="panel"/>, so it draws over the
        /// panel's other parts. Returns null when the Settings screen has no tooltip frame to copy.
        /// </summary>
        internal static ConfigurationGuiFullDescription Create(Transform panel, ConfigurationGuiTemplates templates)
        {
            if (templates.TooltipFrame == null) return null;

            var root = ConfigurationGuiTemplates.CreateRect("FullDescription", panel);
            // The panel stacks its other children; Show places this one over them.
            ConfigurationGuiTemplates.AddLayout(root.gameObject).ignoreLayout = true;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.zero;
            root.pivot = Vector2.zero;
            var layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(Padding, Padding, Padding, Padding);
            layout.spacing = Spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            // Grows with the text; the bottom pivot keeps the bottom edge in place.
            root.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            // Taking the pointer would make the row below lose its hover, which hides the overlay again. The
            // group also covers the objects TextMeshPro adds later for characters from fallback fonts.
            root.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;

            var frame = ConfigurationGuiTemplates.Clone(templates.TooltipFrame, root, "Frame");
            // Only the frame is wanted; anything inside it belongs to the game's tooltip.
            for (var i = frame.transform.childCount - 1; i >= 0; i--)
                Object.Destroy(frame.transform.GetChild(i).gameObject);
            frame.raycastTarget = false;
            ConfigurationGuiTemplates.AddLayout(frame.gameObject).ignoreLayout = true;
            ConfigurationGuiTemplates.Stretch(frame.rectTransform, 0f, 0f, 0f, 0f);

            var topic = ConfigurationGuiTemplates.CreateText(root, "Topic", templates.Label);
            topic.fontStyle = FontStyles.Bold;
            var text = ConfigurationGuiTemplates.CreateText(root, "Text", templates.Label, TextScale);
            // Both wrap and grow instead of cutting text off.
            foreach (var label in new[] { topic, text })
            {
                label.textWrappingMode = TextWrappingModes.Normal;
                label.overflowMode = TextOverflowModes.Overflow;
                label.alignment = TextAlignmentOptions.TopLeft;
            }

            root.gameObject.SetActive(false);
            return new ConfigurationGuiFullDescription(root, topic, text);
        }

        /// <summary>
        /// Shows <paramref name="topic"/> and <paramref name="text"/> as wide as <paramref name="area"/>,
        /// with the overlay's bottom-left corner on the area's. The area shares the panel as parent, so
        /// its width applies as it is.
        /// </summary>
        internal void Show(RectTransform area, string topic, string text)
        {
            _topic.text = topic;
            _text.text = text;

            var corners = new Vector3[4];
            area.GetWorldCorners(corners);
            _root.position = corners[0];
            _root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, area.rect.width);
            _root.gameObject.SetActive(true);
        }

        internal void Hide()
        {
            _root.gameObject.SetActive(false);
        }
    }
}
