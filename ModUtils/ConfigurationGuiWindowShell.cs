using System;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ModUtils
{
    /// <summary>
    /// What the entry window and the choice window share: a root under the game's menu that takes every
    /// click around the window, the window in the middle of the screen with its frame, its size, and the one
    /// window of this copy of ModUtils that is open at a time. While a window is open, and on the frame on
    /// which it closes, the game takes no player input (<see cref="ConfigurationGuiEntryWindowInput"/>).
    /// </summary>
    internal sealed class ConfigurationGuiWindowShell
    {
        /// <summary>
        /// Keeps the window's parts clear of the frame's border.
        /// </summary>
        internal const int FramePadding = 24;

        private const float MaxWidth = 960f;
        private const float ScreenMargin = 32f;
        private static readonly Color FallbackFrameColor = new Color(0.08f, 0.08f, 0.08f, 0.95f);

        private static ConfigurationGuiView _current;
        private static int _closedFrame = -1;

        private readonly RectTransform _root;
        private Canvas _canvas;
        private RectTransform _content;
        private RectTransform _scroll;

        private ConfigurationGuiWindowShell(RectTransform root, RectTransform window)
        {
            _root = root;
            Window = window;
        }

        /// <summary>
        /// The open window of either kind, or null.
        /// </summary>
        internal static ConfigurationGuiView Current => _current != null ? _current : null;

        internal static bool IsOpen => Current != null && !Current.IsClosed;

        /// <summary>
        /// True while a window is open and on the frame one closed, so that the key press that closed it
        /// does not also reach the game.
        /// </summary>
        internal static bool BlocksInput => IsOpen || _closedFrame == Time.frameCount;

        /// <summary>
        /// True when the world session that a window belongs to has ended or is ending.
        /// </summary>
        internal static bool IsWorldGone
        {
            get
            {
                var game = Game.instance;
                return game == null || game.IsShuttingDown();
            }
        }

        /// <summary>
        /// The object that holds the window's parts, which the window's view goes on.
        /// </summary>
        internal RectTransform Window { get; }

        /// <summary>
        /// Finds the game's menu in a world and the widgets a window copies. Returns why a window cannot
        /// open, or null when it can.
        /// </summary>
        internal static string FindTemplates(out Menu menu, out ConfigurationGuiTemplates templates)
        {
            templates = null;
            menu = Menu.instance;
            if (menu == null || Game.instance == null)
                return "no world is loaded";

            // The Settings screen may not have been opened in this session, so the widgets come from the prefab
            // that the game opens it from.
            var settings = menu.m_settingsPrefab != null
                ? menu.m_settingsPrefab.GetComponentInChildren<Settings>(true)
                : null;
            templates = settings != null ? ConfigurationGuiTemplates.Find(settings) : null;
            if (templates == null || templates.Label == null || templates.Button == null)
                return "the game's Settings screen has no label or button to copy";

            return null;
        }

        /// <summary>
        /// Builds the window's root, named <paramref name="rootName"/>, and in it the window named
        /// <paramref name="windowName"/> with its frame. Both stay hidden until <see cref="Show"/>.
        /// </summary>
        internal static ConfigurationGuiWindowShell Create(Menu menu, ConfigurationGuiTemplates templates,
            string rootName, string windowName)
        {
            // Under the menu, like the game's Settings screen, so it draws over the HUD and below the game's
            // popups, and goes when the world's scene is unloaded.
            var root = ConfigurationGuiTemplates.CreateRect(rootName, menu.transform);
            try
            {
                // Built inactive, as the Mods tab builds its panels, so that copied widgets start only after
                // they are detached from the vanilla screen.
                root.gameObject.SetActive(false);
                ConfigurationGuiTemplates.Stretch(root, 0f, 0f, 0f, 0f);
                // Transparent, but takes every click around the window, so nothing behind it reacts.
                root.gameObject.AddComponent<Image>().color = Color.clear;

                var window = ConfigurationGuiTemplates.CreateRect(windowName, root);
                window.anchorMin = new Vector2(0.5f, 0.5f);
                window.anchorMax = new Vector2(0.5f, 0.5f);
                window.pivot = new Vector2(0.5f, 0.5f);
                window.anchoredPosition = Vector2.zero;
                window.sizeDelta = new Vector2(MaxWidth, ConfigurationGuiView.ScrollMinHeight);

                // Behind the window's other parts and outside their layout, so its sprite size never sizes the
                // window.
                var frame = ConfigurationGuiTemplates.CreateRect("Frame", window);
                ConfigurationGuiTemplates.AddLayout(frame.gameObject).ignoreLayout = true;
                ConfigurationGuiTemplates.Stretch(frame, 0f, 0f, 0f, 0f);
                ConfigurationGuiTemplates.AddFrame(frame.gameObject,
                    templates.PanelFrame != null ? templates.PanelFrame : templates.TooltipFrame,
                    FallbackFrameColor);

                return new ConfigurationGuiWindowShell(root, window);
            }
            catch (Exception)
            {
                Object.Destroy(root.gameObject);
                throw;
            }
        }

        /// <summary>
        /// Tells which scroll view holds the window's rows; its content sizes the window.
        /// </summary>
        internal void SetList(RectTransform content)
        {
            _content = content;
            _scroll = (RectTransform)content.parent.parent;
        }

        /// <summary>
        /// Shows the built window as the open one.
        /// </summary>
        internal void Show(ConfigurationGuiView view)
        {
            _root.gameObject.SetActive(true);
            _current = view;
            FitSize(true);
        }

        /// <summary>
        /// Destroys the window with its root, also when it was never shown.
        /// </summary>
        internal void Destroy()
        {
            if (_root != null)
                Object.Destroy(_root.gameObject);
            else if (Window != null)
                Object.Destroy(Window.gameObject);
        }

        /// <summary>
        /// Records that <paramref name="view"/> closed on this frame, so that the game still takes no input
        /// on it.
        /// </summary>
        internal static void MarkClosed(ConfigurationGuiView view)
        {
            _closedFrame = Time.frameCount;
            if (_current == view)
                _current = null;
        }

        /// <summary>
        /// Makes the window <see cref="MaxWidth"/> wide and as tall as its rows need, both at most the
        /// screen's size less <see cref="ScreenMargin"/> on each side; rows that do not fit then scroll.
        /// The heights come from the last layout pass, so <paramref name="rebuild"/> lays the window out
        /// first for a window that has not been laid out yet.
        /// </summary>
        internal void FitSize(bool rebuild)
        {
            var window = Window;
            // The screen in UI units is the root canvas, whatever size the menu's own object has.
            if (_canvas == null)
            {
                var canvas = window.GetComponentInParent<Canvas>();
                _canvas = canvas != null ? canvas.rootCanvas : null;
            }

            var area = _canvas != null ? ((RectTransform)_canvas.transform).rect : _root.rect;
            var width = Mathf.Max(0f, Mathf.Min(MaxWidth, area.width - 2f * ScreenMargin));
            if (Mathf.Abs(window.rect.width - width) > 0.5f)
                window.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);

            if (rebuild)
                LayoutRebuilder.ForceRebuildLayoutImmediate(window);

            // The window's other parts take what they need, and the scroll area the whole list, at least its
            // minimum height.
            var others = LayoutUtility.GetPreferredHeight(window) - LayoutUtility.GetPreferredHeight(_scroll);
            var list = Mathf.Max(LayoutUtility.GetPreferredHeight(_content), ConfigurationGuiView.ScrollMinHeight);
            var height = Mathf.Max(0f, Mathf.Min(others + list, area.height - 2f * ScreenMargin));
            if (Mathf.Abs(window.rect.height - height) > 0.5f)
                window.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }
    }
}
