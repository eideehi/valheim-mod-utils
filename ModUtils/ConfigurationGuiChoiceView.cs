using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModUtils
{
    /// <summary>
    /// The choice window, opened by <see cref="ConfigurationGui.OpenChoiceWindow"/>, in the entry window's
    /// shell: one row per <see cref="ConfigurationGuiChoice"/> with a name and a dropdown, an optional filter
    /// field above the rows, a status line below them for a refused choice, and a Close button. A choice is
    /// passed to its row's callback when it is made, so nothing is pending, and Close, Escape and the
    /// gamepad back button close the window at once. While the window is open the game takes no player
    /// input (<see cref="ConfigurationGuiEntryWindowInput"/>). It closes when the world session ends, and when
    /// one of its callbacks throws.
    /// </summary>
    internal sealed class ConfigurationGuiChoiceView : ConfigurationGuiView
    {
        /// <summary>
        /// Name of the object under the game's menu that holds the window. The window itself is its child
        /// named by the plugin GUID.
        /// </summary>
        internal const string RootName = "ModUtils.ChoiceWindow";

        // A row's name is this and its index, so automated checks can find a row.
        private const string RowPrefix = "Choice:";

        private readonly List<ChoiceRow> _rows = new List<ChoiceRow>();
        private ConfigurationGuiInputField _filter;
        private Action<string> _filterChanged;
        private string _pluginName;
        private ConfigurationGuiWindowShell _shell;
        private TMP_Text _status;

        /// <summary>
        /// Opens the window unless a window of either kind is open, as described on
        /// <see cref="ConfigurationGui.OpenChoiceWindow"/>, and returns it, or null when it did not open. The
        /// first window patches the game with <paramref name="harmonyId"/>.
        /// </summary>
        internal static ConfigurationGuiChoiceWindow Open(ConfigurationGuiPlugin plugin, string title, string note,
            IList<ConfigurationGuiChoice> choices, Action<string> filterChanged, string harmonyId)
        {
            if (ConfigurationGuiWindowShell.IsOpen) return null;

            if (choices.Count == 0)
            {
                Warn(plugin, "no choices were given");
                return null;
            }

            if (choices.Contains(null))
            {
                Warn(plugin, "a choice is null");
                return null;
            }

            var refusal = ConfigurationGuiWindowShell.FindTemplates(out var menu, out var templates);
            if (refusal == null && templates.Dropdown == null)
                refusal = "the game's Settings screen has no dropdown to copy";
            if (refusal != null)
            {
                Warn(plugin, refusal);
                return null;
            }

            ConfigurationGuiEntryWindowInput.Apply(harmonyId);

            ConfigurationGuiWindowShell shell = null;
            try
            {
                shell = ConfigurationGuiWindowShell.Create(menu, templates, RootName, plugin.Guid);
                var view = shell.Window.gameObject.AddComponent<ConfigurationGuiChoiceView>();
                view._shell = shell;
                view.Build(plugin, templates, title, note, choices, filterChanged);
                // A callback that threw while the rows were filled has closed the window already.
                if (view.IsClosed) return null;

                shell.Show(view);
                return new ConfigurationGuiChoiceWindow(view);
            }
            catch (Exception e)
            {
                shell?.Destroy();
                UnityEngine.Debug.LogError($"[ModUtils] Failed to open the choice window of {plugin.Name}: {e}");
                return null;
            }
        }

        private static void Warn(ConfigurationGuiPlugin plugin, string reason)
        {
            UnityEngine.Debug.LogWarning($"[ModUtils] The choice window of {plugin.Name} is not opened: {reason}.");
        }

        /// <summary>
        /// True when the filter field or an open dropdown list handles the current Escape press itself, so
        /// Escape must not close the window. An open list closes on the press.
        /// </summary>
        internal override bool IsEscapeConsumed()
        {
            if (_filter != null && _filter.ConsumesEscape())
                return true;

            foreach (var row in _rows)
                if (ConfigurationGuiDropdownEditor.CloseListOnEscape(row.Dropdown))
                    return true;

            return false;
        }

        internal void CloseWindow()
        {
            Close();
        }

        /// <summary>
        /// Reads every row's options and selection again and shows them without passing a choice on. Stops
        /// at a callback that throws or closes the window.
        /// </summary>
        internal void ShowChoices()
        {
            foreach (var row in _rows)
                if (!ShowChoice(row))
                    return;
        }

        // Nothing is pending, so leaving never asks.
        protected override void Leave()
        {
            Close();
        }

        protected override void Close()
        {
            if (IsClosed) return;

            MarkClosed();
            _shell.Destroy();
        }

        // Leaving the world destroys the window with the game's menu; the shutdown that comes first has
        // closed it already.
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

            // The window belongs to the world session.
            if (ConfigurationGuiWindowShell.IsWorldGone)
            {
                Close();
                return;
            }

            _shell.FitSize(false);

            base.Update();
        }

        private void Build(ConfigurationGuiPlugin plugin, ConfigurationGuiTemplates templates, string title,
            string note, IList<ConfigurationGuiChoice> choices, Action<string> filterChanged)
        {
            _pluginName = plugin.Name;
            _filterChanged = filterChanged;
            Templates = templates;

            LayOutStack(ConfigurationGuiWindowShell.FramePadding);
            CreateHeader(title, note);
            // Without the game's text field to copy there is no filter, as the Mods tab then has no search.
            if (filterChanged != null && templates.InputField != null)
            {
                _filter = CreateSearchField(transform, "Filter");
                var fieldHeight = ConfigurationGuiTemplates.ControlHeight(templates.InputField);
                ConfigurationGuiTemplates.AddLayout(_filter.Input.gameObject, minWidth: EditorMinWidth,
                    minHeight: fieldHeight, preferredHeight: fieldHeight);
                _filter.Input.onValueChanged.AddListener(OnFilterChanged);
            }

            var content = CreateScrollView(transform, templates);
            _shell.SetList(content);
            var body = CreateRowsBody(content);

            // Right below the rows, so a refusal shows next to the choice that caused it; hidden while empty.
            _status = ConfigurationGuiTemplates.CreateText(transform, "Status", templates.Label, 0.9f);
            _status.textWrappingMode = TextWrappingModes.Normal;
            ShowStatus(null);

            // Without a description to show, the area would stay an empty block under the list.
            var hasDescription = choices.Any(x => !string.IsNullOrEmpty(x.Description));
            if (hasDescription)
                CreateDescriptionArea();
            var footer = CreateFooterLine(out var style, out var width, out var height);
            CreateFooterButton(style, footer, "Close", ConfigurationGui.CloseKey, width, height, Leave);
            if (hasDescription)
                CreateFullDescription();

            for (var i = 0; i < choices.Count; i++)
                CreateChoiceRow(choices[i], i, body);
            ShowChoices();
        }

        private void CreateChoiceRow(ConfigurationGuiChoice choice, int index, RectTransform body)
        {
            var rowRect = CreateRowObject(RowPrefix + index.ToString(CultureInfo.InvariantCulture), body,
                choice.Name, choice.Description, out var name);

            var slot = ConfigurationGuiTemplates.CreateRect("Editor", rowRect);
            var dropdown = ConfigurationGuiDropdownEditor.CreateDropdown(slot, Templates, out var height);
            // With a placeholder the dropdown can show no option, for a selection outside the options. The
            // placeholder itself shows nothing.
            var placeholder = ConfigurationGuiTemplates.CreateText(dropdown.transform, "Placeholder", Templates.Label);
            placeholder.text = "";
            dropdown.placeholder = placeholder;
            dropdown.ClearOptions();

            var lineHeight = Mathf.Max(height, RowMinHeight - 2 * RowPadding);
            LayOutRowParts(name, slot, lineHeight, lineHeight);

            var row = new ChoiceRow(choice, dropdown);
            _rows.Add(row);
            dropdown.onValueChanged.AddListener(chosen => OnChosen(row, chosen));
            rowRect.gameObject.SetActive(true);
        }

        private bool ShowChoice(ChoiceRow row)
        {
            var choice = row.Choice;
            if (!TryCall(() => ReadOptions(choice), "options", choice, out var texts) ||
                !TryCall(choice.Selected, "selected", choice, out var selected))
                return false;

            var dropdown = row.Dropdown;
            // The list is built again only when it changed.
            if (!texts.SequenceEqual(row.Options))
            {
                // An open list shows the old options, so a click on it would pick an index into the new ones.
                // Hiding only fades the list out, and its items take clicks until it is gone, so they stop
                // taking them first. The list sits beside the dropdown's template, which is inactive while the
                // list is open and keeps its items as they are for the next list.
                if (dropdown.IsExpanded)
                {
                    foreach (var item in dropdown.template.parent.GetComponentsInChildren<Toggle>())
                        item.interactable = false;
                    dropdown.Hide();
                }

                row.Options = texts;
                dropdown.ClearOptions();
                dropdown.AddOptions(texts);
            }

            dropdown.SetValueWithoutNotify(selected >= 0 && selected < texts.Count ? selected : -1);
            return true;
        }

        // Copied inside the call, so a list that throws while it is read counts as a callback that threw.
        private static List<string> ReadOptions(ConfigurationGuiChoice choice)
        {
            var options = choice.Options();
            return options != null ? options.Select(x => x ?? "").ToList() : new List<string>();
        }

        /// <summary>
        /// Passes the picked option of <paramref name="row"/> to its choose callback and shows the message it
        /// returns, then every row's options and selection again, so a refused choice shows the row's
        /// selection as before.
        /// </summary>
        private void OnChosen(ChoiceRow row, int index)
        {
            if (IsClosed) return;

            if (index >= 0 && index < row.Options.Count)
            {
                var choice = row.Choice;
                if (!TryCall(() => choice.Choose(index), "choose", choice, out var message))
                    return;

                ShowStatus(message);
            }

            ShowChoices();
        }

        private void OnFilterChanged(string text)
        {
            if (IsClosed) return;

            try
            {
                _filterChanged(text);
            }
            catch (Exception e)
            {
                Fail("filterChanged callback", e);
                return;
            }

            if (IsClosed) return;

            ShowStatus(null);
            ShowChoices();
        }

        private void ShowStatus(string text)
        {
            _status.text = text ?? "";
            _status.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        /// <summary>
        /// Calls a callback of <paramref name="choice"/>. False when it threw, which closes the window, or when
        /// the window was closed inside it.
        /// </summary>
        private bool TryCall<T>(Func<T> callback, string name, ConfigurationGuiChoice choice, out T result)
        {
            try
            {
                result = callback();
            }
            catch (Exception e)
            {
                result = default;
                Fail($"{name} callback of the choice \"{choice.Name}\"", e);
                return false;
            }

            return !IsClosed;
        }

        // The window closes, so no further callback runs and the error is logged once.
        private void Fail(string callback, Exception e)
        {
            UnityEngine.Debug.LogError(
                $"[ModUtils] The {callback} in the choice window of {_pluginName} threw, so the window closes: {e}");
            Close();
        }

        private sealed class ChoiceRow
        {
            internal ChoiceRow(ConfigurationGuiChoice choice, TMP_Dropdown dropdown)
            {
                Choice = choice;
                Dropdown = dropdown;
            }

            internal ConfigurationGuiChoice Choice { get; }

            internal TMP_Dropdown Dropdown { get; }

            /// <summary>
            /// The options the dropdown lists, which a chosen index points into.
            /// </summary>
            internal List<string> Options { get; set; } = new List<string>();
        }
    }
}
