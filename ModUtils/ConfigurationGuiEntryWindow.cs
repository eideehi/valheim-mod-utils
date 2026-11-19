using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace ModUtils
{
    /// <summary>
    /// A window in the middle of the game screen, opened by <see cref="ConfigurationGui.OpenEntryWindow"/>,
    /// that lists chosen entries of one plugin in the given order. Each row is the row of the plugin's panel
    /// on the Mods tab; there are no sections, no search and no advanced filter. Edits stay pending until
    /// Apply writes them; Close and Escape leave, asking first when edits are pending. While the window is
    /// open the game takes no player input (<see cref="ConfigurationGuiEntryWindowInput"/>). It closes and
    /// drops pending edits when the world session ends.
    /// </summary>
    internal sealed class ConfigurationGuiEntryWindow : ConfigurationGuiEntryView
    {
        /// <summary>
        /// Name of the object under the game's menu that holds the window. The window itself is its child
        /// named by the plugin GUID.
        /// </summary>
        internal const string RootName = "ModUtils.EntryWindow";

        private const float MaxWidth = 960f;
        private const float ScreenMargin = 32f;
        // Keeps the rows clear of the frame's border.
        private const int FramePadding = 24;
        private static readonly Color FallbackFrameColor = new Color(0.08f, 0.08f, 0.08f, 0.95f);

        private static ConfigurationGuiEntryWindow _current;
        private static int _closedFrame = -1;

        private readonly Queue<Row> _unbuiltRows = new Queue<Row>();
        private Canvas _canvas;
        private RectTransform _content;
        private RectTransform _root;
        private RectTransform _rowsBody;
        private RectTransform _scroll;

        internal static bool IsOpen => _current != null && !_current.IsClosed;

        /// <summary>
        /// True while a window is open and on the frame one closed, so that the key press that closed it
        /// does not also reach the game.
        /// </summary>
        internal static bool BlocksInput => IsOpen || _closedFrame == Time.frameCount;

        /// <summary>
        /// Opens the window unless one is open, as described on <see cref="ConfigurationGui.OpenEntryWindow"/>.
        /// The first window patches the game with <paramref name="harmonyId"/>.
        /// </summary>
        internal static bool Open(ConfigurationGuiPlugin plugin, string title, string note,
            IList<ConfigEntryBase> entries, string harmonyId)
        {
            if (IsOpen) return false;

            if (entries.Count == 0)
            {
                Warn(plugin, "no entries were given");
                return false;
            }

            var rows = new List<Row>();
            foreach (var entry in entries)
            {
                if (entry == null || !ReferenceEquals(entry.ConfigFile, plugin.Config))
                {
                    var name = entry == null ? "null" : $"[{entry.Definition.Section}:{entry.Definition.Key}]";
                    Warn(plugin, $"the entry {name} is not one of its config entries");
                    return false;
                }

                // As on the Mods tab, the entry that lists the plugin there has no row.
                if (ReferenceEquals(entry, plugin.ShowOnSettingsScreen)) continue;

                var row = Row.Create(entry, rows.Count);
                if (row != null)
                    rows.Add(row);
            }

            if (rows.Count == 0)
            {
                Warn(plugin, "none of the entries is shown");
                return false;
            }

            var menu = Menu.instance;
            if (menu == null || Game.instance == null)
            {
                Warn(plugin, "no world is loaded");
                return false;
            }

            // The Settings screen may not have been opened in this session, so the widgets come from the prefab
            // that the game opens it from.
            var settings = menu.m_settingsPrefab != null
                ? menu.m_settingsPrefab.GetComponentInChildren<Settings>(true)
                : null;
            var templates = settings != null ? ConfigurationGuiTemplates.Find(settings) : null;
            if (templates == null || templates.Label == null || templates.Button == null)
            {
                Warn(plugin, "the game's Settings screen has no label or button to copy");
                return false;
            }

            ConfigurationGuiEntryWindowInput.Apply(harmonyId);

            GameObject rootObject = null;
            try
            {
                // Under the menu, like the game's Settings screen, so it draws over the HUD and below the game's
                // popups, and goes when the world's scene is unloaded.
                var root = ConfigurationGuiTemplates.CreateRect(RootName, menu.transform);
                rootObject = root.gameObject;
                // Built inactive, as the Mods tab builds its panels, so that copied widgets start only after
                // they are detached from the vanilla screen.
                rootObject.SetActive(false);
                ConfigurationGuiTemplates.Stretch(root, 0f, 0f, 0f, 0f);
                // Transparent, but takes every click around the window, so nothing behind it reacts.
                rootObject.AddComponent<Image>().color = Color.clear;

                var windowRect = ConfigurationGuiTemplates.CreateRect(plugin.Guid, root);
                windowRect.anchorMin = new Vector2(0.5f, 0.5f);
                windowRect.anchorMax = new Vector2(0.5f, 0.5f);
                windowRect.pivot = new Vector2(0.5f, 0.5f);
                windowRect.anchoredPosition = Vector2.zero;
                windowRect.sizeDelta = new Vector2(MaxWidth, ScrollMinHeight);

                // Behind the window's other parts and outside their layout, so its sprite size never sizes the
                // window.
                var frame = ConfigurationGuiTemplates.CreateRect("Frame", windowRect);
                ConfigurationGuiTemplates.AddLayout(frame.gameObject).ignoreLayout = true;
                ConfigurationGuiTemplates.Stretch(frame, 0f, 0f, 0f, 0f);
                ConfigurationGuiTemplates.AddFrame(frame.gameObject,
                    templates.PanelFrame != null ? templates.PanelFrame : templates.TooltipFrame,
                    FallbackFrameColor);

                var window = windowRect.gameObject.AddComponent<ConfigurationGuiEntryWindow>();
                window._root = root;
                window.Build(plugin, templates, title, note, rows);

                rootObject.SetActive(true);
                _current = window;
                window.FitSize(true);
                return true;
            }
            catch (Exception e)
            {
                if (rootObject != null)
                    Destroy(rootObject);
                UnityEngine.Debug.LogError($"[ModUtils] Failed to open the entry window of {plugin.Name}: {e}");
                return false;
            }
        }

        /// <summary>
        /// Closes the open window without asking, dropping its pending edits and its discard prompt.
        /// </summary>
        internal static void CloseCurrent()
        {
            if (IsOpen)
                _current.Close();
        }

        private static void Warn(ConfigurationGuiPlugin plugin, string reason)
        {
            UnityEngine.Debug.LogWarning($"[ModUtils] The entry window of {plugin.Name} is not opened: {reason}.");
        }

        // The window's buttons and editors stay clickable around a prompt without a cover.
        protected override bool CoversBackgroundWhilePrompting => true;

        /// <summary>
        /// Writes pending edits, but not once the window has closed or while it asks whether to discard them.
        /// </summary>
        protected override void Apply()
        {
            if (IsClosed || IsPrompting) return;

            base.Apply();
        }

        protected override void Close()
        {
            if (IsClosed) return;

            MarkClosed();
            DismissPrompt();
            Destroy(_root != null ? _root.gameObject : gameObject);
        }

        // Leaving the world destroys the window with the game's menu. The shutdown that comes first has
        // closed it already; the game's popups may be gone by now, so they are left alone here.
        private void OnDestroy()
        {
            if (!IsClosed)
                MarkClosed();
        }

        private void MarkClosed()
        {
            IsClosed = true;
            _closedFrame = Time.frameCount;
            if (_current == this)
                _current = null;
        }

        protected override void Update()
        {
            if (IsClosed) return;

            // The window belongs to the world session; leaving the world drops it with its pending edits.
            var game = Game.instance;
            if (game == null || game.IsShuttingDown())
            {
                Close();
                return;
            }

            if (_unbuiltRows.Count > 0)
                BuildRows();
            FitSize(false);

            base.Update();
        }

        private void Build(ConfigurationGuiPlugin plugin, ConfigurationGuiTemplates templates, string title,
            string note, List<Row> rows)
        {
            Pending = new ConfigurationGuiPendingValues(plugin.Name);
            Templates = templates;

            LayOutStack(FramePadding);
            CreateHeader(title, note);
            _content = CreateScrollView(transform, templates);
            _scroll = (RectTransform)_content.parent.parent;
            // The rows are spaced as in a section of the Mods tab.
            _rowsBody = ConfigurationGuiTemplates.CreateRect("Rows", _content);
            var layout = _rowsBody.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = SectionRowSpacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            CreateDescriptionArea();

            var footer = CreateFooterLine(out var style, out var width, out var height);
            CreateApplyButton(footer, style, width, height);
            CreateFooterButton(style, footer, "Close", ConfigurationGui.CloseKey, width, height, Leave);
            CreateFullDescription();

            foreach (var row in rows)
                _unbuiltRows.Enqueue(row);
            UpdateApplyButton();
            Pending.Changed = UpdateApplyButton;
            BuildRows();
        }

        private void BuildRows()
        {
            for (var i = 0; i < RowsPerFrame && _unbuiltRows.Count > 0; i++)
                CreateRow(_unbuiltRows.Dequeue(), _rowsBody);
        }

        /// <summary>
        /// Makes the window <see cref="MaxWidth"/> wide and as tall as its rows need, both at most the
        /// screen's size less <see cref="ScreenMargin"/> on each side; rows that do not fit then scroll.
        /// The heights come from the last layout pass, so <paramref name="rebuild"/> lays the window out
        /// first for a window that has not been laid out yet.
        /// </summary>
        private void FitSize(bool rebuild)
        {
            var window = (RectTransform)transform;
            // The screen in UI units is the root canvas, whatever size the menu's own object has.
            if (_canvas == null)
            {
                var canvas = GetComponentInParent<Canvas>();
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
            var list = Mathf.Max(LayoutUtility.GetPreferredHeight(_content), ScrollMinHeight);
            var height = Mathf.Max(0f, Mathf.Min(others + list, area.height - 2f * ScreenMargin));
            if (Mathf.Abs(window.rect.height - height) > 0.5f)
                window.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }
    }
}
