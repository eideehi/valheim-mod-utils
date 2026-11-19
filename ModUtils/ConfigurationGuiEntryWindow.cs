using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

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

        private readonly Queue<Row> _unbuiltRows = new Queue<Row>();
        private RectTransform _rowsBody;
        private ConfigurationGuiWindowShell _shell;

        /// <summary>
        /// True while an entry window is open; a choice window does not count.
        /// </summary>
        internal static bool IsOpen => ConfigurationGuiWindowShell.IsOpen &&
                                       ConfigurationGuiWindowShell.Current is ConfigurationGuiEntryWindow;

        /// <summary>
        /// Opens the window unless a window of either kind is open, as described on
        /// <see cref="ConfigurationGui.OpenEntryWindow"/>. The first window patches the game with
        /// <paramref name="harmonyId"/>.
        /// </summary>
        internal static bool Open(ConfigurationGuiPlugin plugin, string title, string note,
            IList<ConfigEntryBase> entries, string harmonyId)
        {
            if (ConfigurationGuiWindowShell.IsOpen) return false;

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

            var refusal = ConfigurationGuiWindowShell.FindTemplates(out var menu, out var templates);
            if (refusal != null)
            {
                Warn(plugin, refusal);
                return false;
            }

            ConfigurationGuiEntryWindowInput.Apply(harmonyId);

            ConfigurationGuiWindowShell shell = null;
            try
            {
                shell = ConfigurationGuiWindowShell.Create(menu, templates, RootName, plugin.Guid);
                var window = shell.Window.gameObject.AddComponent<ConfigurationGuiEntryWindow>();
                window._shell = shell;
                window.Build(plugin, templates, title, note, rows);
                shell.Show(window);
                return true;
            }
            catch (Exception e)
            {
                shell?.Destroy();
                UnityEngine.Debug.LogError($"[ModUtils] Failed to open the entry window of {plugin.Name}: {e}");
                return false;
            }
        }

        /// <summary>
        /// Closes the open entry window without asking, dropping its pending edits and its discard prompt. A
        /// choice window stays open.
        /// </summary>
        internal static void CloseCurrent()
        {
            if (IsOpen)
                ((ConfigurationGuiEntryWindow)ConfigurationGuiWindowShell.Current).Close();
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
            _shell.Destroy();
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
            ConfigurationGuiWindowShell.MarkClosed(this);
        }

        protected override void Update()
        {
            if (IsClosed) return;

            // The window belongs to the world session; leaving the world drops it with its pending edits.
            if (ConfigurationGuiWindowShell.IsWorldGone)
            {
                Close();
                return;
            }

            if (_unbuiltRows.Count > 0)
                BuildRows();
            _shell.FitSize(false);

            base.Update();
        }

        private void Build(ConfigurationGuiPlugin plugin, ConfigurationGuiTemplates templates, string title,
            string note, List<Row> rows)
        {
            Pending = new ConfigurationGuiPendingValues(plugin.Name);
            Templates = templates;

            LayOutStack(ConfigurationGuiWindowShell.FramePadding);
            CreateHeader(title, note);
            CreateStagedApplyHint();
            var content = CreateScrollView(transform, templates);
            _shell.SetList(content);
            _rowsBody = CreateRowsBody(content);
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
    }
}
