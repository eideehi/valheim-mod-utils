using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Valheim.SettingsGui;
using Object = UnityEngine.Object;

namespace ModUtils
{
    /// <summary>
    /// Vanilla Settings widgets that the Mods page copies, and helpers that build UI from them.
    /// </summary>
    internal sealed class ConfigurationGuiTemplates
    {
        // Controls inside a mod's panel are at most this high, lower than the vanilla widgets, so more rows
        // fit in the list.
        internal const float DefaultControlHeight = 30f;
        private const float MinControlHeight = 24f;
        private const float DefaultVanillaControlHeight = 36f;
        private const float MaxVanillaControlHeight = 56f;
        private const float ButtonTextMargin = 8f;
        private const float MinButtonTextScale = 0.8f;
        private const float ToggleLabelGap = 8f;

        private ConfigurationGuiTemplates()
        {
        }

        internal Toggle Toggle { get; private set; }

        internal Slider Slider { get; private set; }

        internal TMP_Dropdown Dropdown { get; private set; }

        internal Button Button { get; private set; }

        internal Button BackButton { get; private set; }

        internal TMP_Text Label { get; private set; }

        internal Scrollbar Scrollbar { get; private set; }

        internal TMP_InputField InputField { get; private set; }

        /// <summary>
        /// The frame image of the vanilla Settings tooltips, or null when the screen has none.
        /// </summary>
        internal Image TooltipFrame { get; private set; }

        internal static ConfigurationGuiTemplates Find(Settings settings)
        {
            var gameplay = settings.GetComponentInChildren<GameplaySettings>(true);
            var audio = settings.GetComponentInChildren<Valheim.SettingsGui.AudioSettings>(true);
            var graphics = settings.GetComponentInChildren<Valheim.SettingsGui.GraphicsSettings>(true);
            var radial = settings.GetComponentInChildren<RadialSettings>(true);

            var templates = new ConfigurationGuiTemplates
            {
                Toggle = GetField<Toggle>(gameplay, "m_toggleRun") ??
                         GetField<Toggle>(audio, "m_continousMusic") ??
                         FindVanilla<Toggle>(settings),
                Slider = GetField<Slider>(audio, "m_volumeSlider") ??
                         GetField<Slider>(gameplay, "m_autoBackups") ??
                         FindVanilla<Slider>(settings),
                Dropdown = GetField<TMP_Dropdown>(radial, "m_radialSize") ??
                           GetField<TMP_Dropdown>(graphics, "m_renderScaleDropdown") ??
                           FindVanilla<TMP_Dropdown>(settings),
                Button = FindButton(settings),
                BackButton = GetField<Button>(settings, "m_backButton"),
                Label = FindLabel(settings),
                InputField = FindInputField()
            };

            var scrollRect = GetField<ScrollRectEnsureVisible>(graphics, "m_settingsListScrollRectEnsureVisible")
                ?.GetComponent<ScrollRect>();
            if (scrollRect == null || scrollRect.verticalScrollbar == null)
            {
                scrollRect = null;
                foreach (var candidate in settings.GetComponentsInChildren<ScrollRect>(true))
                {
                    if (candidate.verticalScrollbar == null || IsInModsPage(candidate.transform)) continue;
                    scrollRect = candidate;
                    break;
                }
            }

            templates.Scrollbar = scrollRect != null ? scrollRect.verticalScrollbar : null;

            // Looked up as a Settings tooltip looks up its own frame: by name under its tooltip object.
            var tooltip = GetField<GameObject>(FindVanilla<SettingsTooltip>(settings), "m_tooltip");
            var frame = tooltip != null ? Utils.FindChild(tooltip.transform, "Background") : null;
            templates.TooltipFrame = frame != null ? frame.GetComponent<Image>() : null;

            return templates;
        }

        internal static Button FindButton(Settings settings)
        {
            var gameplay = settings.GetComponentInChildren<GameplaySettings>(true);
            return GetField<Button>(gameplay, "m_resetTutorial") ??
                   GetField<Button>(gameplay, "m_radialSettingsButton") ??
                   GetField<Button>(settings, "m_backButton");
        }

        internal static TMP_Text FindLabel(Settings settings)
        {
            var gameplay = settings.GetComponentInChildren<GameplaySettings>(true);
            var toggle = GetField<Toggle>(gameplay, "m_toggleRun");
            var label = toggle != null ? toggle.GetComponentInChildren<TMP_Text>(true) : null;
            return label != null ? label : FindVanilla<TMP_Text>(settings);
        }

        /// <summary>
        /// The text field inside the Feedback prefab. Settings has none, and only this sub-object is ever
        /// copied: an instance of the whole prefab registers itself as the open Feedback dialog.
        /// </summary>
        private static TMP_InputField FindInputField()
        {
            GameObject prefab = null;
            var startup = FejdStartup.instance;
            if (startup != null)
                prefab = startup.m_feedbackPrefab;

            if (prefab == null)
            {
                var menu = Menu.instance;
                if (menu != null)
                    prefab = menu.m_feedbackPrefab;
            }

            var inputField = prefab != null ? prefab.GetComponentInChildren<TMP_InputField>(true) : null;
            return inputField != null ? inputField : null;
        }

        /// <summary>
        /// Copies a vanilla widget under <paramref name="parent"/> and detaches it from vanilla behavior.
        /// </summary>
        internal static T Clone<T>(T source, Transform parent, string name) where T : Component
        {
            var clone = Object.Instantiate(source.gameObject, parent, false);
            clone.name = name;
            Sanitize(clone);
            clone.SetActive(true);
            return clone.GetComponent<T>();
        }

        /// <summary>
        /// Gives every copied widget fresh events, because copied persistent listeners still call the
        /// vanilla components, and removes copied behaviors that act on vanilla state: gamepad hotkeys,
        /// vanilla tooltips, settings tabs and tab handlers.
        /// </summary>
        internal static void Sanitize(GameObject root)
        {
            var rootTransform = root.transform;
            foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
            {
                switch (selectable)
                {
                    case Button button:
                        button.onClick = new Button.ButtonClickedEvent();
                        break;
                    case Toggle toggle:
                        toggle.onValueChanged = new Toggle.ToggleEvent();
                        if (toggle.group != null && !toggle.group.transform.IsChildOf(rootTransform))
                            toggle.group = null;
                        break;
                    case Slider slider:
                        slider.onValueChanged = new Slider.SliderEvent();
                        break;
                    case Scrollbar scrollbar:
                        scrollbar.onValueChanged = new Scrollbar.ScrollEvent();
                        break;
                    case TMP_Dropdown dropdown:
                        dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
                        break;
                    case TMP_InputField inputField:
                        ReplaceUnityEvents(inputField);
                        break;
                }

                var navigation = selectable.navigation;
                if (navigation.mode != Navigation.Mode.Explicit) continue;

                navigation.mode = Navigation.Mode.Automatic;
                selectable.navigation = navigation;
            }

            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;

                if (behaviour is UIGamePad gamePad)
                {
                    if (gamePad.m_hint != null && gamePad.m_hint.transform.IsChildOf(rootTransform))
                        gamePad.m_hint.SetActive(false);
                    Object.DestroyImmediate(gamePad);
                }
                else if (behaviour is SettingsTooltip || behaviour is ISettingsTab || behaviour is TabHandler)
                {
                    Object.DestroyImmediate(behaviour);
                }
            }
        }

        /// <summary>
        /// Replaces every UnityEvent field of the component, including ones declared by game subclasses,
        /// with a fresh instance.
        /// </summary>
        private static void ReplaceUnityEvents(Component component)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.DeclaredOnly;
            for (var type = component.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
            foreach (var field in type.GetFields(flags))
            {
                var fieldType = field.FieldType;
                if (!typeof(UnityEventBase).IsAssignableFrom(fieldType) || fieldType.IsAbstract ||
                    fieldType.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                field.SetValue(component, Activator.CreateInstance(fieldType));
            }
        }

        /// <summary>
        /// Lays out one copied toggle per label in rows of <paramref name="columns"/> inside
        /// <paramref name="container"/> and returns the toggles in label order.
        /// </summary>
        internal static List<Toggle> CreateToggleGrid(RectTransform container, Toggle template, TMP_Text style,
            IList<string> labels, int columns, float itemHeight, float spacing)
        {
            var layout = container.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var toggles = new List<Toggle>(labels.Count);
            for (var start = 0; start < labels.Count; start += columns)
            {
                var line = CreateRect("Line", container);
                var lineLayout = line.gameObject.AddComponent<HorizontalLayoutGroup>();
                lineLayout.spacing = spacing;
                lineLayout.childControlWidth = true;
                lineLayout.childControlHeight = true;
                lineLayout.childForceExpandWidth = true;
                lineLayout.childForceExpandHeight = false;
                AddLayout(line.gameObject, minHeight: itemHeight, preferredHeight: itemHeight);

                for (var column = 0; column < columns; column++)
                {
                    var index = start + column;
                    if (index >= labels.Count)
                    {
                        // Keeps the cells of a short last line as wide as the others.
                        AddLayout(CreateRect("Filler", line).gameObject, preferredWidth: 0f,
                            preferredHeight: itemHeight, flexibleWidth: 1f);
                        continue;
                    }

                    var toggle = Clone(template, line, "Item");
                    LayOutToggleItem(toggle, template, style, labels[index]);
                    AddLayout(toggle.gameObject, preferredWidth: 0f, preferredHeight: itemHeight,
                        flexibleWidth: 1f);
                    toggles.Add(toggle);
                }
            }

            return toggles;
        }

        /// <summary>
        /// Puts the box of a copied toggle at the cell's left edge and its label inside the cell, right of
        /// the box, and returns that label (null when the toggle has none). The vanilla toggle draws its
        /// label outside its own rect, left of the box, where it would cover the row name.
        /// </summary>
        internal static TMP_Text LayOutToggleItem(Toggle toggle, Toggle template, TMP_Text style, string label)
        {
            var boxWidth = 0f;
            var box = toggle.targetGraphic != null ? toggle.targetGraphic.rectTransform : null;
            if (box != null)
            {
                // The template sits on the vanilla screen, so its sizes are the laid-out ones.
                var boxSize = template.targetGraphic.rectTransform.rect.size;
                var check = toggle.graphic != null ? toggle.graphic.rectTransform : null;
                var checkSize = template.graphic != null ? template.graphic.rectTransform.rect.size : Vector2.zero;

                if (box.parent != toggle.transform)
                    box.SetParent(toggle.transform, false);
                box.anchorMin = new Vector2(0f, 0.5f);
                box.anchorMax = new Vector2(0f, 0.5f);
                box.pivot = new Vector2(0f, 0.5f);
                box.sizeDelta = boxSize;
                box.anchoredPosition = Vector2.zero;
                boxWidth = boxSize.x;

                // A check mark outside the box would stay where the vanilla layout put it.
                if (check != null && !check.IsChildOf(box))
                {
                    check.SetParent(box, false);
                    check.anchorMin = new Vector2(0.5f, 0.5f);
                    check.anchorMax = new Vector2(0.5f, 0.5f);
                    check.pivot = new Vector2(0.5f, 0.5f);
                    check.sizeDelta = checkSize;
                    check.anchoredPosition = Vector2.zero;
                }
            }

            foreach (var text in toggle.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == toggle.graphic) continue;

                var rect = text.rectTransform;
                if (rect.parent != toggle.transform)
                    rect.SetParent(toggle.transform, false);
                Stretch(rect, boxWidth + ToggleLabelGap, 0f, 0f, 0f);
                text.margin = Vector4.zero;
                text.enableAutoSizing = false;
                text.fontSize = style.fontSize;
                text.richText = false;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.text = label;
                return text;
            }

            return null;
        }

        /// <summary>
        /// Gives a copied button a label of fixed size in the row label's style, spread over the button.
        /// The vanilla label is sized for the vanilla button and shrinks on a shorter one. A positive
        /// <paramref name="fitWidth"/> lets the current text shrink a little to fit a button that wide;
        /// text that still does not fit is cut off, because the button font has no ellipsis character.
        /// </summary>
        internal static TMP_Text SetFixedButtonLabel(Button button, TMP_Text style, float fitWidth = 0f,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var text = button.GetComponentInChildren<TMP_Text>(true);
            if (text == null) return null;

            if (text.transform.parent != button.transform)
                text.transform.SetParent(button.transform, false);
            Stretch(text.rectTransform, ButtonTextMargin, ButtonTextMargin, 0f, 0f);
            text.margin = Vector4.zero;
            text.enableAutoSizing = false;
            text.fontSize = style.fontSize;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Truncate;
            text.alignment = alignment;

            var available = fitWidth - 2f * ButtonTextMargin;
            if (available > 0f)
            {
                var width = text.GetPreferredValues(text.text).x;
                if (width > available)
                    text.fontSize = style.fontSize * Mathf.Max(available / width, MinButtonTextScale);
            }

            return text;
        }

        internal static float ToggleGridHeight(int count, int columns, float itemHeight, float spacing)
        {
            var lines = (count + columns - 1) / columns;
            return lines == 0 ? 0f : lines * itemHeight + (lines - 1) * spacing;
        }

        internal static RectTransform CreateRect(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.layer = parent.gameObject.layer;
            var rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        internal static TextMeshProUGUI CreateText(Transform parent, string name, TMP_Text style,
            float sizeScale = 1f)
        {
            // A text added to an active object looks for TextMeshPro's default font at once, which the game
            // does not ship, so the font is set before the object becomes active.
            var gameObject = CreateRect(name, parent).gameObject;
            gameObject.SetActive(false);
            var text = gameObject.AddComponent<TextMeshProUGUI>();
            if (style != null)
            {
                text.font = style.font;
                text.fontSharedMaterial = style.fontSharedMaterial;
                text.fontSize = style.fontSize * sizeScale;
                text.color = style.color;
            }

            text.richText = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;
            gameObject.SetActive(true);
            return text;
        }


        internal static void SetLabel(Component component, string label)
        {
            var text = component.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
                text.text = label;
        }

        internal static void Stretch(RectTransform rect, float left, float right, float top, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>
        /// Spans the parent's width and keeps a fixed height, centered vertically.
        /// </summary>
        internal static void FillWidth(RectTransform rect, float height)
        {
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = Vector2.zero;
        }

        internal static LayoutElement AddLayout(GameObject gameObject, float minWidth = -1f,
            float minHeight = -1f, float preferredWidth = -1f, float preferredHeight = -1f,
            float flexibleWidth = -1f, float flexibleHeight = -1f)
        {
            var layout = gameObject.GetComponent<LayoutElement>();
            if (layout == null)
                layout = gameObject.AddComponent<LayoutElement>();
            layout.minWidth = minWidth;
            layout.minHeight = minHeight;
            layout.preferredWidth = preferredWidth;
            layout.preferredHeight = preferredHeight;
            layout.flexibleWidth = flexibleWidth;
            layout.flexibleHeight = flexibleHeight;
            return layout;
        }

        /// <summary>
        /// Height of a control inside a mod's panel: the vanilla widget's height from
        /// <see cref="VanillaControlHeight"/>, at most <see cref="DefaultControlHeight"/>.
        /// </summary>
        internal static float ControlHeight(Component source)
        {
            return Mathf.Min(VanillaControlHeight(source), DefaultControlHeight);
        }

        /// <summary>
        /// Height of a vanilla widget, limited to a range that fits one settings row. The Mods list buttons
        /// and the panel footer keep this vanilla size.
        /// </summary>
        internal static float VanillaControlHeight(Component source)
        {
            var rect = source != null ? source.transform as RectTransform : null;
            var height = rect != null ? rect.rect.height : 0f;
            return height >= 1f
                ? Mathf.Clamp(height, MinControlHeight, MaxVanillaControlHeight)
                : DefaultVanillaControlHeight;
        }

        private static T GetField<T>(object instance, string fieldName) where T : Object
        {
            if (instance == null) return null;

            try
            {
                var field = AccessTools.Field(instance.GetType(), fieldName);
                var value = field?.GetValue(instance) as T;
                return value != null ? value : null;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(
                    $"[ModUtils] Failed to read {instance.GetType().Name}.{fieldName}: {e.Message}");
                return null;
            }
        }

        private static T FindVanilla<T>(Settings settings) where T : Component
        {
            foreach (var component in settings.GetComponentsInChildren<T>(true))
                if (!IsInModsPage(component.transform))
                    return component;

            return null;
        }

        private static bool IsInModsPage(Transform transform)
        {
            for (var current = transform; current != null; current = current.parent)
                if (string.Equals(current.name, ConfigurationGui.PageName, StringComparison.Ordinal))
                    return true;

            return false;
        }
    }
}
