using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valheim.SettingsGui;

namespace ModUtils.SmokeTest
{
    /// <summary>
    /// Drives the in-game config screen at the main menu: the Mods tab on the vanilla Settings screen,
    /// this plugin's entry in the mod list, and its configuration panel.
    /// </summary>
    public sealed partial class SmokePlugin
    {
        // Names every ModUtils copy uses for the shared Mods tab.
        private const string ModsPageName = "ModUtils.ModsTab.v1";
        private const string ModListName = "List";
        private const string PanelsName = "Panels";

        private const string RowNamePrefix = "Row:";
        private const string EditorSlotName = "Editor";
        private const string ResetButtonName = "Reset";
        private const string FooterName = "Footer";
        private const string MultiSelectSummaryName = "Summary";
        private const string ScreenshotDirectoryName = "ui";
        private const string IntSliderIdentity = "editors/int_slider";
        private const string FloatSliderIdentity = "editors/float_slider";
        private const string ColorCodeIdentity = "editors/color_code";
        private const string TextIdentity = "editors/text";
        private const string SwatchName = "Swatch";
        private const string SwatchFillName = "Fill";

        // Per channel, as the swatch stores the colour in floats.
        private const float ColorTolerance = 0.01f;

        // The config screen cuts a read-only value to this many characters.
        private const int ReadOnlyTextMaxLength = 120;

        private const float MainMenuWaitSeconds = 60f;
        private const int SettingsOpenFrames = 3;
        private const int SettingsCloseMaxFrames = 120;
        private const float ScreenshotWaitSeconds = 3f;
        private const float ScreenshotSettleSeconds = 0.25f;

        private static readonly object RoutineFinished = new object();

        private IEnumerator RunSettingsGuiChecks(List<CheckResult> results)
        {
            if (_guiSample != null)
            {
                // The main menu can still be loading or hidden behind the intro cinematic.
                var start = Time.realtimeSinceStartup;
                while (!IsMainMenuShown() && Time.realtimeSinceStartup - start < MainMenuWaitSeconds)
                    yield return new WaitForSecondsRealtime(0.5f);
            }

            var skipReason = GetSettingsGuiSkipReason();
            if (skipReason != null)
            {
                results.Add(Report(CheckResult.Skip(skipReason).WithName("settings_gui_checks")));
                yield break;
            }

            RunCleanupAction("restore the sample defaults", () => _guiSample.RestoreDefaults());

            var state = new SettingsGuiState(Info.Metadata.GUID);
            var checks = new List<KeyValuePair<string, Func<IEnumerator>>>
            {
                UiCheck("settings_gui_adds_one_mods_tab", () => CheckAddsOneModsTab(state)),
                UiCheck("settings_gui_reopen_keeps_one_mods_tab", () => CheckReopenKeepsOneModsTab(state)),
                UiCheck("settings_gui_mod_list_shows_plugin", () => CheckModListShowsPlugin(state)),
                UiCheck("settings_gui_panel_shows_visible_entries", () => CheckPanelShowsVisibleEntries(state)),
                UiCheck("settings_gui_read_only_rows_not_editable", () => CheckReadOnlyRowsNotEditable(state)),
                UiCheck("settings_gui_editors_write_entry_and_file", () => CheckEditorsWriteEntryAndFile(state)),
                UiCheck("settings_gui_slider_number_field", () => CheckSliderNumberField(state)),
                UiCheck("settings_gui_color_swatch", () => CheckColorSwatch(state)),
                UiCheck("settings_gui_reset_restores_defaults", () => CheckResetRestoresDefaults(state)),
                UiCheck("settings_gui_back_returns_to_mod_list", () => CheckBackReturnsToModList(state)),
                UiCheck("settings_gui_reopened_panel_shows_current_value",
                    () => CheckReopenedPanelShowsCurrentValue(state)),
                UiCheck("settings_gui_tab_switch_closes_panel", () => CheckTabSwitchClosesPanel(state)),
                UiCheck("settings_gui_ok_callback_runs_once", () => CheckOkCallbackRunsOnce(state)),
                UiCheck("settings_gui_ok_closes_settings", CheckOkClosesSettings)
            };

            foreach (var check in checks)
                yield return StartCoroutine(RunUiCheck(check.Key, check.Value, results));

            RunCleanupAction("restore the sample defaults", () => _guiSample.RestoreDefaults());
            if (Settings.instance != null)
            {
                RunCleanupAction("close the settings screen", () => Settings.instance.OnBack());
                for (var i = 0; i < SettingsCloseMaxFrames && Settings.instance != null; i++)
                    yield return null;
            }
        }

        private string GetSettingsGuiSkipReason()
        {
            if (_guiSample == null)
                return "the sample configuration could not be set up";
            if (FejdStartup.instance == null)
                return $"FejdStartup.instance did not exist within {MainMenuWaitSeconds:0} seconds; " +
                       "the checks run only at the main menu";
            if (Settings.instance != null)
                return "the settings screen is already open";
            if (!IsMainMenuShown())
                return $"the main menu was not shown within {MainMenuWaitSeconds:0} seconds";
            return null;
        }

        private static bool IsMainMenuShown()
        {
            var startup = FejdStartup.instance;
            return startup != null && startup.m_mainMenu != null && startup.m_mainMenu.activeInHierarchy;
        }

        private static KeyValuePair<string, Func<IEnumerator>> UiCheck(string name, Func<IEnumerator> routine)
        {
            return new KeyValuePair<string, Func<IEnumerator>>(name, routine);
        }

        /// <summary>
        /// Runs one check and records its result. An exception fails the check (a missing precondition
        /// skips it) without stopping the remaining checks. Iterators cannot yield inside a try block
        /// that has a catch clause, so the check's routine is advanced by hand.
        /// </summary>
        private IEnumerator RunUiCheck(string name, Func<IEnumerator> routine, List<CheckResult> results)
        {
            var stack = new Stack<IEnumerator>();
            CheckResult result = null;
            try
            {
                stack.Push(routine());
            }
            catch (Exception e)
            {
                result = ToUiCheckResult(name, e);
            }

            while (result == null)
            {
                object current;
                try
                {
                    current = AdvanceRoutine(stack);
                }
                catch (Exception e)
                {
                    result = ToUiCheckResult(name, e);
                    break;
                }

                if (current == RoutineFinished)
                {
                    result = CheckResult.Pass().WithName(name);
                    break;
                }

                yield return current;
            }

            while (stack.Count > 0)
                if (stack.Pop() is IDisposable disposable)
                    RunCleanupAction("finish " + name, disposable.Dispose);

            results.Add(Report(result));
        }

        /// <summary>
        /// Moves the routine to its next wait instruction, running nested enumerators in place, and
        /// returns <see cref="RoutineFinished"/> when the routine has completed.
        /// </summary>
        private static object AdvanceRoutine(Stack<IEnumerator> stack)
        {
            while (stack.Count > 0)
            {
                var routine = stack.Peek();
                if (!routine.MoveNext())
                {
                    stack.Pop();
                    continue;
                }

                if (routine.Current is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }

                return routine.Current;
            }

            return RoutineFinished;
        }

        private CheckResult ToUiCheckResult(string name, Exception e)
        {
            if (e is SkipCheckException)
                return CheckResult.Skip(e.Message).WithName(name);

            // Failed expectations carry their own message; anything else also needs its stack trace.
            if (!(e is InvalidOperationException))
                Logger.LogError($"[ModUtils Smoke] {name} threw: {e}");
            return CheckResult.Fail(name, e.GetType().Name + ": " + e.Message);
        }

        private void RunCleanupAction(string description, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Logger.LogError($"[ModUtils Smoke] Failed to {description}: {e}");
            }
        }

        private IEnumerator CheckAddsOneModsTab(SettingsGuiState state)
        {
            FejdStartup.instance.OnButtonSettings();
            yield return WaitFrames(SettingsOpenFrames);

            state.Refresh();
            Expect(state.Settings != null, "the settings screen did not open");
            Expect(state.TabHandler != null, "the settings screen has no TabHandler");
            var count = CountModsTabs(state.TabHandler);
            Expect(count == 1, $"expected one tab whose page is {ModsPageName}, found {count}");
            Expect(state.Page.GetComponent<ISettingsTab>() != null,
                "the Mods page has no component implementing ISettingsTab");
        }

        private IEnumerator CheckReopenKeepsOneModsTab(SettingsGuiState state)
        {
            var settings = Settings.instance;
            RequirePrecondition(settings != null, "the settings screen is not open");

            settings.OnBack();
            yield return WaitForSettingsClosed();
            Expect(Settings.instance == null, "Back did not close the settings screen");

            FejdStartup.instance.OnButtonSettings();
            yield return WaitFrames(SettingsOpenFrames);

            state.Refresh();
            Expect(state.Settings != null, "the settings screen did not open again");
            Expect(state.TabHandler != null, "the reopened settings screen has no TabHandler");
            var count = CountModsTabs(state.TabHandler);
            Expect(count == 1, $"expected one tab whose page is {ModsPageName} after reopening, found {count}");
        }

        private IEnumerator CheckModListShowsPlugin(SettingsGuiState state)
        {
            RequireModsPage(state);

            state.TabHandler.SetActiveTab(state.ModsTabIndex, forceSelect: true);
            yield return WaitFrames(2);

            Expect(state.Page.gameObject.activeInHierarchy, "the Mods page is not shown after selecting its tab");
            Expect(state.List.gameObject.activeInHierarchy, "the mod list is not shown on the Mods page");
            Expect(FindModButton(state) != null, $"the mod list shows no button named {state.Guid}");

            yield return CaptureScreenshot("settings-gui-list.png");
        }

        private IEnumerator CheckPanelShowsVisibleEntries(SettingsGuiState state)
        {
            RequireModsPage(state);
            var button = FindModButton(state);
            RequirePrecondition(button != null, "the mod list has no button for this plugin");

            button.onClick.Invoke();
            yield return WaitFrames(2);

            var panel = FindOpenPanel(state);
            Expect(panel != null, $"no active panel named {state.Guid} after clicking the plugin's button");
            Expect(!state.List.gameObject.activeSelf, "the mod list is still shown while the panel is open");

            var shown = FindRows(panel).Select(x => x.name.Substring(RowNamePrefix.Length)).ToList();
            var expected = _guiSample.VisibleEntries.Select(SettingsGuiSample.Identity).ToList();
            var missing = expected.Except(shown).ToList();
            var unexpected = shown.Except(expected).ToList();
            var duplicated = shown.GroupBy(x => x).Where(x => x.Count() > 1).Select(x => x.Key).ToList();
            Expect(missing.Count == 0 && unexpected.Count == 0 && duplicated.Count == 0,
                $"rows differ from the visible entries: missing=[{string.Join(", ", missing)}] " +
                $"unexpected=[{string.Join(", ", unexpected)}] duplicated=[{string.Join(", ", duplicated)}]");

            yield return CaptureScreenshot("settings-gui-panel.png");

            // The lower rows and the expanded multi-select list are below the first screenshot.
            var summary = FindRows(panel).Select(x => FindChild(x, EditorSlotName))
                                         .Select(x => x != null ? FindChild(x, MultiSelectSummaryName) : null)
                                         .Where(x => x != null && x.gameObject.activeInHierarchy)
                                         .Select(x => x.GetComponent<Button>())
                                         .FirstOrDefault(x => x != null);
            Expect(summary != null, $"the panel shows no multi-select row with a {MultiSelectSummaryName} button");
            var scroll = panel.GetComponentInChildren<ScrollRect>(false);
            Expect(scroll != null, "the panel has no ScrollRect to scroll to its bottom");

            summary.onClick.Invoke();
            yield return WaitFrames(2);
            scroll.verticalNormalizedPosition = 0f;
            yield return WaitFrames(2);
            yield return CaptureScreenshot("settings-gui-panel-bottom.png");

            // Later checks expect the panel as it opened.
            summary.onClick.Invoke();
            yield return WaitFrames(2);
            scroll.verticalNormalizedPosition = 1f;
            yield return WaitFrames(2);
        }

        private IEnumerator CheckReadOnlyRowsNotEditable(SettingsGuiState state)
        {
            RequireModsPage(state);
            var panel = FindOpenPanel(state);
            RequirePrecondition(panel != null, "the plugin's panel is not open");

            foreach (var entry in new ConfigEntryBase[] { _guiSample.ReadOnlyToggleEntry, _guiSample.ColorEntry })
            {
                var identity = SettingsGuiSample.Identity(entry);
                var row = FindRow(panel, entry);
                Expect(row != null, $"no shown row for {identity}");

                var editable = row.GetComponentsInChildren<Selectable>(true).Where(x => x.interactable)
                                  .Select(x => x.name).ToList();
                Expect(editable.Count == 0,
                    $"the row for {identity} has interactable widgets: [{string.Join(", ", editable)}]");
                Expect(row.GetComponentsInChildren<Button>(true).All(x => x.name != ResetButtonName),
                    $"the row for {identity} has a Reset button");
            }

            var readOnlyToggleEntry = _guiSample.ReadOnlyToggleEntry;
            var toggle = FindEditorWidget<Toggle>(panel, readOnlyToggleEntry);
            Expect(toggle != null, $"the row for {SettingsGuiSample.Identity(readOnlyToggleEntry)} shows no Toggle");
            Expect(toggle.isOn == readOnlyToggleEntry.Value,
                $"the read-only toggle shows {toggle.isOn} while the entry holds {readOnlyToggleEntry.Value}");

            var colorEntry = _guiSample.ColorEntry;
            var expectedText = ToReadOnlyText(colorEntry.GetSerializedValue());
            var texts = FindEditorWidgets<TMP_Text>(panel, colorEntry).Select(x => x.text).ToList();
            Expect(texts.Contains(expectedText),
                $"the row for {SettingsGuiSample.Identity(colorEntry)} shows [{string.Join(", ", texts)}], " +
                $"expected [{expectedText}]");

            yield break;
        }

        private IEnumerator CheckEditorsWriteEntryAndFile(SettingsGuiState state)
        {
            RequireModsPage(state);
            var panel = FindOpenPanel(state);
            RequirePrecondition(panel != null, "the plugin's panel is not open");

            var toggleEntry = _guiSample.ToggleEntry;
            var toggle = FindEditorWidget<Toggle>(panel, toggleEntry);
            Expect(toggle != null, $"the row for {SettingsGuiSample.Identity(toggleEntry)} has no Toggle");

            var previousToggle = toggleEntry.Value;
            Expect(toggle.isOn == previousToggle,
                $"the toggle shows {toggle.isOn} while the entry holds {previousToggle}");

            var toggled = !previousToggle;
            toggle.isOn = toggled;
            yield return null;

            Expect(toggleEntry.Value == toggled,
                $"switching the toggle from {previousToggle} to {toggled} left the entry at {toggleEntry.Value}");
            ExpectSavedValue(toggleEntry, toggled);

            var choiceEntry = _guiSample.ChoiceEntry;
            var dropdown = FindEditorWidget<TMP_Dropdown>(panel, choiceEntry);
            Expect(dropdown != null, $"the row for {SettingsGuiSample.Identity(choiceEntry)} has no TMP_Dropdown");
            var choices = Enum.GetValues(typeof(SampleChoice)).Cast<SampleChoice>().ToList();
            Expect(dropdown.options.Count == choices.Count,
                $"expected {choices.Count} dropdown options, found {dropdown.options.Count}");

            var previousChoice = choiceEntry.Value;
            var shownIndex = dropdown.value;
            Expect(shownIndex >= 0 && shownIndex < choices.Count && choices[shownIndex] == previousChoice,
                $"the dropdown shows option {shownIndex} while the entry holds {previousChoice}");

            var index = (choices.IndexOf(previousChoice) + 1) % choices.Count;
            dropdown.value = index;
            yield return null;

            Expect(choiceEntry.Value != previousChoice,
                $"selecting option {index} left the entry at {previousChoice}");
            Expect(choiceEntry.Value == choices[index],
                $"selecting option {index} set the entry to {choiceEntry.Value}, expected {choices[index]}");
            ExpectSavedValue(choiceEntry, choices[index]);
        }

        private IEnumerator CheckSliderNumberField(SettingsGuiState state)
        {
            RequireModsPage(state);
            var panel = FindOpenPanel(state);
            RequirePrecondition(panel != null, "the plugin's panel is not open");

            var intEntry = FindSampleEntry<int>(IntSliderIdentity);
            var intRange = intEntry.Description.AcceptableValues as AcceptableValueRange<int>;
            Expect(intRange != null, $"{IntSliderIdentity} has no AcceptableValueRange<int>");
            var intField = FindEditorWidget<TMP_InputField>(panel, intEntry);
            Expect(intField != null, $"the row for {IntSliderIdentity} has no TMP_InputField");
            var intSlider = FindEditorWidget<Slider>(panel, intEntry);
            Expect(intSlider != null, $"the row for {IntSliderIdentity} has no Slider");

            var floatEntry = FindSampleEntry<float>(FloatSliderIdentity);
            var floatField = FindEditorWidget<TMP_InputField>(panel, floatEntry);
            Expect(floatField != null, $"the row for {FloatSliderIdentity} has no TMP_InputField");

            // Each written value must differ from the starting one, or a write could not be told apart.
            const int submitted = 7;
            const float typedFloat = 0.333f;
            var start = intEntry.Value;
            RequirePrecondition(start != intRange.MaxValue && start != submitted,
                $"{IntSliderIdentity} already holds {start}");
            RequirePrecondition(floatEntry.Value != typedFloat, $"{FloatSliderIdentity} already holds {typedFloat}");
            Expect(intField.text == start.ToString(CultureInfo.InvariantCulture),
                $"the number field shows [{intField.text}] while the entry holds {start}");

            // Setting the text without a submit raises only the change event, as typed keys do. The handle
            // follows the typed value, at the nearer end of the range for a value outside it.
            var min = intRange.MinValue;
            var max = intRange.MaxValue;
            foreach (var (typed, handle) in new[] { ("1", 1), ("15", max), ("-5", min), ("150", max) })
            {
                intField.text = typed;
                yield return null;

                Expect(intEntry.Value == start,
                    $"typing [{typed}] without submitting changed the entry from {start} to {intEntry.Value}");
                Expect(intField.text == typed,
                    $"typing [{typed}] without submitting turned the field's text into [{intField.text}]");
                ExpectSavedValue(intEntry, start);
                Expect(intSlider.value == handle,
                    $"typing [{typed}] without submitting put the slider at {intSlider.value}, expected {handle}");
            }

            intField.onSubmit.Invoke(intField.text);
            yield return null;

            Expect(intEntry.Value == max, $"submitting [150] set the entry to {intEntry.Value}, expected {max}");
            ExpectSavedValue(intEntry, max);
            Expect(intField.text == max.ToString(CultureInfo.InvariantCulture),
                $"after submitting [150] the field shows [{intField.text}], expected [{max}]");
            Expect(intSlider.value == max, $"after submitting [150] the slider shows {intSlider.value}, expected {max}");

            intField.text = "abc";
            Expect(intSlider.value == max, $"typing [abc] moved the slider from {max} to {intSlider.value}");
            intField.onSubmit.Invoke(intField.text);
            yield return null;

            Expect(intEntry.Value == max, $"submitting [abc] changed the entry from {max} to {intEntry.Value}");
            Expect(intField.text == max.ToString(CultureInfo.InvariantCulture),
                $"after submitting [abc] the field shows [{intField.text}], expected [{max}]");

            intField.text = submitted.ToString(CultureInfo.InvariantCulture);
            intField.onDeselect.Invoke(intField.text);
            yield return null;

            Expect(intEntry.Value == submitted,
                $"leaving the field with [{submitted}] set the entry to {intEntry.Value}, expected {submitted}");

            floatField.text = "0.333";
            floatField.onSubmit.Invoke(floatField.text);
            yield return null;

            Expect(floatEntry.Value == typedFloat,
                $"submitting [0.333] set the entry to {floatEntry.Value.ToString("R", CultureInfo.InvariantCulture)}, " +
                $"expected {typedFloat.ToString("R", CultureInfo.InvariantCulture)}");
            Expect(floatField.text == "0.333",
                $"after submitting [0.333] the field shows [{floatField.text}], expected [0.333]");

            // Later checks and the saved config expect the defaults.
            PressReset(panel, intEntry);
            PressReset(panel, floatEntry);
            yield return null;
        }

        private IEnumerator CheckColorSwatch(SettingsGuiState state)
        {
            RequireModsPage(state);
            var panel = FindOpenPanel(state);
            RequirePrecondition(panel != null, "the plugin's panel is not open");

            var colorCodeEntry = FindSampleEntry<string>(ColorCodeIdentity);
            var field = FindEditorWidget<TMP_InputField>(panel, colorCodeEntry);
            Expect(field != null, $"the row for {ColorCodeIdentity} has no TMP_InputField");
            var fill = FindSwatchFill(panel, colorCodeEntry);
            Expect(fill != null, $"the row for {ColorCodeIdentity} has no {SwatchName} with a {SwatchFillName} image");

            var start = colorCodeEntry.Value;
            RequirePrecondition(start == (string)colorCodeEntry.DefaultValue,
                $"{ColorCodeIdentity} holds [{start}] instead of its default");
            ExpectSwatch(fill, new Color32(0x4F, 0xA9, 0xFF, 0xFF), $"the stored [{start}]");

            // Typed text only changes the swatch; nothing is written before the edit ends.
            field.text = "#FF0000";
            yield return null;
            ExpectSwatch(fill, Color.red, "typed [#FF0000]");
            Expect(colorCodeEntry.Value == start,
                $"typing [#FF0000] without submitting changed the entry to [{colorCodeEntry.Value}]");
            ExpectSavedValue(colorCodeEntry, start);

            field.text = "zzz";
            yield return null;
            Expect(fill.color.a < ColorTolerance,
                $"for typed [zzz] the swatch shows {fill.color}, expected a transparent fill");
            Expect(colorCodeEntry.Value == start,
                $"typing [zzz] without submitting changed the entry to [{colorCodeEntry.Value}]");
            ExpectSavedValue(colorCodeEntry, start);

            field.text = "#00FF00";
            field.onSubmit.Invoke(field.text);
            yield return null;
            Expect(colorCodeEntry.Value == "#00FF00",
                $"submitting [#00FF00] set the entry to [{colorCodeEntry.Value}]");
            ExpectSwatch(fill, Color.green, "the submitted [#00FF00]");

            PressReset(panel, colorCodeEntry);
            yield return null;
            Expect(colorCodeEntry.Value == start, $"Reset left {ColorCodeIdentity} at [{colorCodeEntry.Value}]");
            ExpectSwatch(fill, new Color32(0x4F, 0xA9, 0xFF, 0xFF), $"the reset [{start}]");

            var textEntry = FindSampleEntry<string>(TextIdentity);
            var textRow = FindRow(panel, textEntry);
            Expect(textRow != null, $"no shown row for {TextIdentity}");
            var textSlot = FindChild(textRow, EditorSlotName);
            Expect(textSlot != null && FindChild(textSlot, SwatchName) == null,
                $"the row for {TextIdentity} has no editor slot or shows a {SwatchName}");

            var colorEntry = _guiSample.ColorEntry;
            var colorIdentity = SettingsGuiSample.Identity(colorEntry);
            var colorFill = FindSwatchFill(panel, colorEntry);
            Expect(colorFill != null, $"the row for {colorIdentity} has no {SwatchName} with a {SwatchFillName} image");
            ExpectSwatch(colorFill, colorEntry.Value, $"the Color entry's {colorEntry.Value}");
        }

        private IEnumerator CheckResetRestoresDefaults(SettingsGuiState state)
        {
            RequireModsPage(state);
            var panel = FindOpenPanel(state);
            RequirePrecondition(panel != null, "the plugin's panel is not open");

            // A value that already equals its default would let a Reset that does nothing pass.
            var toggleEntry = _guiSample.ToggleEntry;
            if (toggleEntry.Value == (bool)toggleEntry.DefaultValue)
                toggleEntry.Value = !(bool)toggleEntry.DefaultValue;
            var choiceEntry = _guiSample.ChoiceEntry;
            if (choiceEntry.Value == (SampleChoice)choiceEntry.DefaultValue)
                choiceEntry.Value = Enum.GetValues(typeof(SampleChoice)).Cast<SampleChoice>()
                                        .First(x => x != (SampleChoice)choiceEntry.DefaultValue);

            foreach (var entry in new ConfigEntryBase[] { toggleEntry, choiceEntry })
            {
                var row = FindRow(panel, entry);
                Expect(row != null, $"no shown row for {SettingsGuiSample.Identity(entry)}");
                var reset = FindChild(row, ResetButtonName);
                var button = reset != null && reset.gameObject.activeInHierarchy ? reset.GetComponent<Button>() : null;
                Expect(button != null, $"the row for {SettingsGuiSample.Identity(entry)} has no Reset button");
                button.onClick.Invoke();
            }

            yield return null;

            Expect(toggleEntry.Value == (bool)toggleEntry.DefaultValue,
                $"Reset left {SettingsGuiSample.Identity(toggleEntry)} at {toggleEntry.Value}");
            Expect(choiceEntry.Value == (SampleChoice)choiceEntry.DefaultValue,
                $"Reset left {SettingsGuiSample.Identity(choiceEntry)} at {choiceEntry.Value}");

            var toggle = FindEditorWidget<Toggle>(panel, toggleEntry);
            Expect(toggle != null && toggle.isOn == (bool)toggleEntry.DefaultValue,
                "the toggle does not show the default value after Reset");
        }

        private IEnumerator CheckBackReturnsToModList(SettingsGuiState state)
        {
            RequireModsPage(state);
            var panel = FindOpenPanel(state);
            RequirePrecondition(panel != null, "the plugin's panel is not open");

            var footer = FindChild(panel, FooterName);
            var back = footer != null ? footer.GetComponentInChildren<Button>(false) : null;
            Expect(back != null, "the panel's footer shows no button");

            back.onClick.Invoke();
            yield return WaitFrames(2);

            Expect(Settings.instance != null, "Back on the panel closed the settings screen");
            Expect(state.List.gameObject.activeSelf, "the mod list is not shown after Back");
            Expect(FindOpenPanel(state) == null, "a panel is still shown after Back");
            var okButton = GetOkButton(state.Settings);
            Expect(okButton != null && okButton.gameObject.activeSelf,
                "the settings OK button is still hidden after Back");
        }

        private IEnumerator CheckReopenedPanelShowsCurrentValue(SettingsGuiState state)
        {
            RequireModsPage(state);
            var button = FindModButton(state);
            RequirePrecondition(button != null, "the mod list has no button for this plugin");
            RequirePrecondition(FindOpenPanel(state) == null, "the plugin's panel is still open");

            var toggleEntry = _guiSample.ToggleEntry;
            var changed = !(bool)toggleEntry.DefaultValue;
            toggleEntry.Value = changed;

            button.onClick.Invoke();
            yield return WaitFrames(2);

            var panel = FindOpenPanel(state);
            Expect(panel != null, "the plugin's panel did not open again");
            var toggle = FindEditorWidget<Toggle>(panel, toggleEntry);
            Expect(toggle != null, $"the row for {SettingsGuiSample.Identity(toggleEntry)} has no Toggle");
            Expect(toggle.isOn == changed,
                $"the reopened panel shows {toggle.isOn} for a value changed to {changed}");

            toggleEntry.Value = (bool)toggleEntry.DefaultValue;
        }

        private IEnumerator CheckTabSwitchClosesPanel(SettingsGuiState state)
        {
            RequireModsPage(state);
            RequirePrecondition(state.ModsTabIndex != 0, "the Mods tab is the first tab, so there is no other tab to switch to");
            if (FindOpenPanel(state) == null)
            {
                var button = FindModButton(state);
                RequirePrecondition(button != null, "the mod list has no button for this plugin");
                button.onClick.Invoke();
                yield return WaitFrames(2);
            }

            RequirePrecondition(FindOpenPanel(state) != null, "the plugin's panel could not be opened");

            state.TabHandler.SetActiveTab(0, forceSelect: true);
            yield return WaitFrames(2);
            state.TabHandler.SetActiveTab(state.ModsTabIndex, forceSelect: true);
            yield return WaitFrames(1);

            Expect(state.List.gameObject.activeInHierarchy,
                "the mod list is not shown when returning to the Mods tab");
            Expect(FindOpenPanel(state) == null, "a panel is still shown when returning to the Mods tab");
            var okButton = GetOkButton(state.Settings);
            Expect(okButton != null && okButton.gameObject.activeSelf,
                "the settings OK button is still hidden after leaving the panel through another tab");
        }

        private IEnumerator CheckOkCallbackRunsOnce(SettingsGuiState state)
        {
            RequireModsPage(state);
            var tab = state.Page.GetComponent<ISettingsTab>();
            RequirePrecondition(tab != null, "the Mods page has no ISettingsTab component");

            var calls = 0;
            tab.OnOkAsync(() => calls++);
            Expect(calls == 1, $"OnOkAsync called its callback {calls} times before returning, expected 1");
            yield break;
        }

        private IEnumerator CheckOkClosesSettings()
        {
            var settings = Settings.instance;
            RequirePrecondition(settings != null, "the settings screen is not open");

            settings.OnOk();
            yield return WaitForSettingsClosed();
            Expect(Settings.instance == null, $"the settings screen is still open {SettingsCloseMaxFrames} frames after OK");
        }

        private static IEnumerator WaitFrames(int frames)
        {
            for (var i = 0; i < frames; i++)
                yield return null;
        }

        private static IEnumerator WaitForSettingsClosed()
        {
            for (var i = 0; i < SettingsCloseMaxFrames && Settings.instance != null; i++)
                yield return null;
        }

        /// <summary>
        /// Saves a screenshot under the smoke directory. Unity writes the file after the frame ends, so
        /// this waits until the file exists and then a little longer for the write to finish. A file that
        /// does not appear in time fails the check that takes the screenshot.
        /// </summary>
        private IEnumerator CaptureScreenshot(string fileName)
        {
            var directory = Path.Combine(GetSmokeBaseDirectory(), ScreenshotDirectoryName);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, fileName);
            if (File.Exists(path))
                File.Delete(path);

            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path);

            var start = Time.realtimeSinceStartup;
            while (!File.Exists(path) && Time.realtimeSinceStartup - start < ScreenshotWaitSeconds)
                yield return null;
            yield return new WaitForSecondsRealtime(ScreenshotSettleSeconds);

            Expect(File.Exists(path), $"the screenshot {path} was not written within {ScreenshotWaitSeconds:0} seconds");
            Logger.LogInfo($"[ModUtils Smoke] Saved screenshot {path}");
        }

        private static void ExpectSwatch(Image fill, Color expected, string shown)
        {
            var actual = fill.color;
            Expect(Mathf.Abs(actual.r - expected.r) < ColorTolerance &&
                   Mathf.Abs(actual.g - expected.g) < ColorTolerance &&
                   Mathf.Abs(actual.b - expected.b) < ColorTolerance &&
                   Mathf.Abs(actual.a - expected.a) < ColorTolerance,
                $"for {shown} the swatch shows {actual}, expected {expected}");
        }

        private void ExpectSavedValue(ConfigEntryBase entry, object value)
        {
            var expected = TomlTypeConverter.ConvertToString(value, entry.SettingType);
            var saved = ReadSavedValue(entry.Definition);
            Expect(string.Equals(saved, expected, StringComparison.Ordinal),
                $"the config file holds [{saved}] for {SettingsGuiSample.Identity(entry)}, expected [{expected}]");
        }

        /// <summary>
        /// Reads one value from this plugin's config file on disk, the way BepInEx writes it:
        /// "[Section]" headers followed by "Key = value" lines.
        /// </summary>
        private string ReadSavedValue(ConfigDefinition definition)
        {
            var section = "";
            foreach (var rawLine in File.ReadAllLines(Config.ConfigFilePath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator <= 0) continue;

                if (string.Equals(section, definition.Section, StringComparison.Ordinal) &&
                    string.Equals(line.Substring(0, separator).Trim(), definition.Key, StringComparison.Ordinal))
                    return line.Substring(separator + 1).Trim();
            }

            return null;
        }

        private static int CountModsTabs(TabHandler tabHandler)
        {
            return tabHandler.m_tabs.Count(x => x?.m_page != null &&
                                                string.Equals(x.m_page.name, ModsPageName,
                                                    StringComparison.Ordinal));
        }

        private static void RequireModsPage(SettingsGuiState state)
        {
            RequirePrecondition(Settings.instance != null && state.Settings == Settings.instance,
                "the settings screen is not open");
            RequirePrecondition(state.Page != null && state.List != null && state.Panels != null,
                "the Mods page with its mod list and panel area was not found");
        }

        private static Button FindModButton(SettingsGuiState state)
        {
            if (state.List == null) return null;
            var child = FindChild(state.List, state.Guid);
            return child != null && child.gameObject.activeInHierarchy ? child.GetComponent<Button>() : null;
        }

        /// <summary>
        /// The panel this plugin shows on the Mods page, if one is open. A closed panel is destroyed
        /// at the end of its frame, so a shown panel is the last active child with the plugin's name.
        /// </summary>
        private static Transform FindOpenPanel(SettingsGuiState state)
        {
            if (state.Panels == null) return null;

            Transform found = null;
            for (var i = 0; i < state.Panels.childCount; i++)
            {
                var child = state.Panels.GetChild(i);
                if (string.Equals(child.name, state.Guid, StringComparison.Ordinal) &&
                    child.gameObject.activeInHierarchy)
                    found = child;
            }

            return found;
        }

        /// <summary>
        /// Rows the panel shows. Sections start expanded, so every visible entry has an active row.
        /// </summary>
        private static List<Transform> FindRows(Transform panel)
        {
            return panel.GetComponentsInChildren<Transform>(false)
                        .Where(x => x.gameObject.activeInHierarchy &&
                                    x.name.StartsWith(RowNamePrefix, StringComparison.Ordinal))
                        .ToList();
        }

        private static Transform FindRow(Transform panel, ConfigEntryBase entry)
        {
            var name = RowNamePrefix + SettingsGuiSample.Identity(entry);
            return FindRows(panel).FirstOrDefault(x => string.Equals(x.name, name, StringComparison.Ordinal));
        }

        private static void PressReset(Transform panel, ConfigEntryBase entry)
        {
            var row = FindRow(panel, entry);
            var reset = row != null ? FindChild(row, ResetButtonName) : null;
            var button = reset != null && reset.gameObject.activeInHierarchy ? reset.GetComponent<Button>() : null;
            Expect(button != null, $"the row for {SettingsGuiSample.Identity(entry)} has no Reset button");
            button.onClick.Invoke();
        }

        /// <summary>
        /// The fill image of the colour swatch in an entry's editor area, if the row shows one.
        /// </summary>
        private static Image FindSwatchFill(Transform panel, ConfigEntryBase entry)
        {
            var row = FindRow(panel, entry);
            var slot = row != null ? FindChild(row, EditorSlotName) : null;
            var swatch = slot != null ? FindChild(slot, SwatchName) : null;
            var fill = swatch != null && swatch.gameObject.activeInHierarchy ? FindChild(swatch, SwatchFillName) : null;
            return fill != null ? fill.GetComponent<Image>() : null;
        }

        private ConfigEntry<T> FindSampleEntry<T>(string identity)
        {
            var entry = _guiSample.VisibleEntries.FirstOrDefault(x => string.Equals(SettingsGuiSample.Identity(x),
                identity, StringComparison.Ordinal)) as ConfigEntry<T>;
            Expect(entry != null, $"the sample has no {typeof(T).Name} entry {identity}");
            return entry;
        }

        private static T FindEditorWidget<T>(Transform panel, ConfigEntryBase entry) where T : Component
        {
            return FindEditorWidgets<T>(panel, entry).FirstOrDefault();
        }

        /// <summary>
        /// Shown components of type <typeparamref name="T"/> in the editor area of an entry's row.
        /// </summary>
        private static List<T> FindEditorWidgets<T>(Transform panel, ConfigEntryBase entry) where T : Component
        {
            var row = FindRow(panel, entry);
            var slot = row != null ? FindChild(row, EditorSlotName) : null;
            if (slot == null || !slot.gameObject.activeInHierarchy) return new List<T>();
            return slot.GetComponentsInChildren<T>(false).Where(x => x.gameObject.activeInHierarchy).ToList();
        }

        /// <summary>
        /// Text the config screen shows for a value without an editor: the serialized value on one line,
        /// cut to <see cref="ReadOnlyTextMaxLength"/> characters followed by "...".
        /// </summary>
        private static string ToReadOnlyText(string serialized)
        {
            var text = (serialized ?? "").Replace('\r', ' ').Replace('\n', ' ');
            return text.Length > ReadOnlyTextMaxLength ? text.Substring(0, ReadOnlyTextMaxLength) + "..." : text;
        }

        private static Transform FindChild(Transform parent, string name)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (string.Equals(child.name, name, StringComparison.Ordinal))
                    return child;
            }

            return null;
        }

        private static Button GetOkButton(Settings settings)
        {
            if (settings == null) return null;
            return AccessTools.Field(typeof(Settings), "m_okButton")?.GetValue(settings) as Button;
        }

        private static TabHandler GetTabHandler(Settings settings)
        {
            var tabHandler = AccessTools.Field(typeof(Settings), "m_tabHandler")?.GetValue(settings) as TabHandler;
            return tabHandler != null ? tabHandler : settings.GetComponentInChildren<TabHandler>();
        }

        private static void RequirePrecondition(bool condition, string reason)
        {
            if (!condition)
                throw new SkipCheckException(reason);
        }

        private sealed class SkipCheckException : Exception
        {
            public SkipCheckException(string message) : base(message)
            {
            }
        }

        /// <summary>
        /// The open settings screen and the parts of the Mods page that the checks use.
        /// </summary>
        private sealed class SettingsGuiState
        {
            public SettingsGuiState(string guid)
            {
                Guid = guid;
            }

            public string Guid { get; }
            public Settings Settings { get; private set; }
            public TabHandler TabHandler { get; private set; }
            public int ModsTabIndex { get; private set; } = -1;
            public RectTransform Page { get; private set; }
            public Transform List { get; private set; }
            public Transform Panels { get; private set; }

            public void Refresh()
            {
                Settings = Settings.instance;
                TabHandler = Settings != null ? GetTabHandler(Settings) : null;
                ModsTabIndex = -1;
                Page = null;
                List = null;
                Panels = null;
                if (TabHandler == null) return;

                for (var i = 0; i < TabHandler.m_tabs.Count; i++)
                {
                    var page = TabHandler.m_tabs[i]?.m_page;
                    if (page == null || !string.Equals(page.name, ModsPageName, StringComparison.Ordinal))
                        continue;

                    ModsTabIndex = i;
                    Page = page;
                    List = FindChild(page, ModListName);
                    Panels = FindChild(page, PanelsName);
                    return;
                }
            }
        }
    }
}
