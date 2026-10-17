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

        // The entry that ModUtils binds in this plugin's config to list the plugin on the Mods tab.
        private const string VisibilityEntrySection = "mod_utils";
        private const string VisibilityEntryKey = "show_on_settings_screen";
        private const string VisibilityEntryIdentity = VisibilityEntrySection + "/" + VisibilityEntryKey;

        // ModUtils' texts for that entry, written out so that the checks do not take them from ModUtils.
        private const string VisibilityEntryCategory = "Settings screen";
        private const string VisibilityEntryName = "Show on the Settings screen";
        private const string VisibilityEntryDescription =
            "Shows this mod on the Mods tab of the game's Settings screen. When no mod is shown there, the tab " +
            "is not added. A change applies the next time the Settings screen opens. After turning this off, " +
            "turn it back on in ConfigurationManager or in this mod's config file.";
        private const string VisibilityEntryCategoryJapanese = "\u8A2D\u5B9A\u753B\u9762";
        private const string VisibilityEntryNameJapanese = "\u8A2D\u5B9A\u753B\u9762\u306B\u8868\u793A";
        private const string VisibilityEntryDescriptionJapanese =
            "\u30B2\u30FC\u30E0\u306E\u8A2D\u5B9A\u753B\u9762\u306E\u300CMod\u300D\u30BF\u30D6\u306B\u3053\u306E Mod \u3092\u8868\u793A\u3057\u307E\u3059\u3002" +
            "\u8868\u793A\u3059\u308B Mod \u304C\u3072\u3068\u3064\u3082\u306A\u3044\u3068\u304D\u306F\u3001\u30BF\u30D6\u306F\u8FFD\u52A0\u3055\u308C\u307E\u305B\u3093\u3002" +
            "\u5909\u66F4\u306F\u6B21\u306B\u8A2D\u5B9A\u753B\u9762\u3092\u958B\u3044\u305F\u3068\u304D\u306B\u53CD\u6620\u3055\u308C\u307E\u3059\u3002" +
            "\u30AA\u30D5\u306B\u3057\u305F\u3042\u3068\u3067\u623B\u3059\u3068\u304D\u306F\u3001" +
            "ConfigurationManager \u304B\u3053\u306E Mod \u306E\u8A2D\u5B9A\u30D5\u30A1\u30A4\u30EB\u3067\u5909\u66F4\u3057\u3066\u304F\u3060\u3055\u3044\u3002";

        private const string RowNamePrefix = "Row:";
        private const string RowLabelName = "Name";
        private const string EditorSlotName = "Editor";
        private const string ResetButtonName = "Reset";
        private const string FooterName = "Footer";
        private const string ApplyButtonName = "Apply";
        private const string BackButtonName = "Back";
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
        private const int PanelBuildMaxFrames = 120;
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
            // The checks use this plugin's button, so the plugin is listed for them whatever the user chose,
            // and the user's choice is put back afterwards.
            var visibilityEntry = FindVisibilityEntry();
            var listedBefore = visibilityEntry != null && visibilityEntry.Value;
            if (visibilityEntry != null)
                RunCleanupAction("list this plugin on the Mods tab", () => visibilityEntry.Value = true);
            Application.logMessageReceived += OnGuiLogMessage;

            var state = new SettingsGuiState(Info.Metadata.GUID);
            var checks = new List<KeyValuePair<string, Func<IEnumerator>>>
            {
                UiCheck("settings_gui_visibility_entry_is_bound", CheckVisibilityEntryIsBound),
                UiCheck("settings_gui_adds_one_mods_tab", () => CheckAddsOneModsTab(state)),
                UiCheck("settings_gui_reopen_keeps_one_mods_tab", () => CheckReopenKeepsOneModsTab(state)),
                UiCheck("settings_gui_mod_list_shows_plugin", () => CheckModListShowsPlugin(state)),
                UiCheck("settings_gui_panel_shows_visible_entries", () => CheckPanelShowsVisibleEntries(state)),
                UiCheck("settings_gui_read_only_rows_not_editable", () => CheckReadOnlyRowsNotEditable(state)),
                UiCheck("settings_gui_editors_write_entry_and_file", () => CheckEditorsWriteEntryAndFile(state)),
                UiCheck("settings_gui_slider_number_field", () => CheckSliderNumberField(state)),
                UiCheck("settings_gui_color_swatch", () => CheckColorSwatch(state)),
                UiCheck("settings_gui_reset_restores_defaults", () => CheckResetRestoresDefaults(state)),
                UiCheck("settings_gui_editors_stage_until_apply", () => CheckEditorsStageUntilApply(state)),
                UiCheck("settings_gui_apply_button_state", () => CheckApplyButtonState(state)),
                UiCheck("settings_gui_apply_shows_corrected_value", () => CheckApplyShowsCorrectedValue(state)),
                UiCheck("settings_gui_reset_all", () => CheckResetAll(state)),
                UiCheck("settings_gui_custom_editor", () => CheckCustomEditor(state)),
                UiCheck("settings_gui_custom_editor_failure_falls_back",
                    () => CheckCustomEditorFailureFallsBack(state)),
                UiCheck("settings_gui_search_filters_rows", () => CheckSearchFiltersRows(state)),
                UiCheck("settings_gui_search_during_build_filters_later_rows",
                    () => CheckSearchDuringBuildFiltersLaterRows(state)),
                UiCheck("settings_gui_advanced_toggle", () => CheckAdvancedToggle(state)),
                UiCheck("settings_gui_long_description_unfolds", () => CheckLongDescriptionUnfolds(state)),
                UiCheck("settings_gui_back_returns_to_mod_list", () => CheckBackReturnsToModList(state)),
                UiCheck("settings_gui_reopened_panel_shows_current_value",
                    () => CheckReopenedPanelShowsCurrentValue(state)),
                UiCheck("settings_gui_back_with_pending_asks", () => CheckBackWithPendingAsks(state)),
                UiCheck("settings_gui_tab_switch_closes_panel", () => CheckTabSwitchClosesPanel(state)),
                UiCheck("settings_gui_tab_switch_discards_pending", () => CheckTabSwitchDiscardsPending(state)),
                UiCheck("settings_gui_ok_callback_runs_once", () => CheckOkCallbackRunsOnce(state)),
                UiCheck("settings_gui_hidden_mod_has_no_button", () => CheckHiddenModHasNoButton(state)),
                UiCheck("settings_gui_ok_closes_settings", CheckOkClosesSettings)
            };

            foreach (var check in checks)
                yield return StartCoroutine(RunUiCheck(check.Key, check.Value, results));

            Application.logMessageReceived -= OnGuiLogMessage;
            RunCleanupAction("restore the sample defaults", () => _guiSample.RestoreDefaults());
            if (visibilityEntry != null)
                RunCleanupAction("restore whether this plugin is listed on the Mods tab",
                    () => visibilityEntry.Value = listedBefore);
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

        private IEnumerator CheckVisibilityEntryIsBound()
        {
            var definition = new ConfigDefinition(VisibilityEntrySection, VisibilityEntryKey);
            Expect(Config.ContainsKey(definition), $"this plugin's config has no entry {VisibilityEntryIdentity}");
            var entry = Config[definition] as ConfigEntry<bool>;
            Expect(entry != null,
                $"{VisibilityEntryIdentity} is a {Config[definition].SettingType.Name} entry, expected a Boolean one");
            Expect((bool)entry.DefaultValue, $"{VisibilityEntryIdentity} defaults to {entry.DefaultValue}, expected True");

            var attributes = entry.Description?.Tags?.OfType<ConfigurationManagerAttributes>().FirstOrDefault();
            Expect(attributes != null, $"{VisibilityEntryIdentity} has no ConfigurationManagerAttributes");
            Expect(attributes.IsAdvanced != true, $"{VisibilityEntryIdentity} is marked advanced");
            Expect(attributes.ReadOnly != true, $"{VisibilityEntryIdentity} is marked read-only");

            var texts = GetVisibilityEntryTexts();
            foreach (var (what, shown, expected) in new[]
                     {
                         ("category", attributes.Category, texts.Category),
                         ("name", attributes.DispName, texts.Name),
                         ("description", attributes.Description, texts.Description),
                         ("stored description", entry.Description.Description, texts.Description)
                     })
                Expect(string.Equals(shown, expected, StringComparison.Ordinal),
                    $"the {what} of {VisibilityEntryIdentity} is [{shown}], expected [{expected}]");

            yield break;
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
            yield return WaitForPanelBuilt(state);

            var panel = FindOpenPanel(state);
            Expect(panel != null, $"no active panel named {state.Guid} after clicking the plugin's button");
            Expect(!state.List.gameObject.activeSelf, "the mod list is still shown while the panel is open");

            var shown = FindRows(panel).Select(x => x.name.Substring(RowNamePrefix.Length)).ToList();
            var expected = GetListedRowIdentities();
            var missing = expected.Except(shown).ToList();
            var unexpected = shown.Except(expected).ToList();
            var duplicated = shown.GroupBy(x => x).Where(x => x.Count() > 1).Select(x => x.Key).ToList();
            Expect(missing.Count == 0 && unexpected.Count == 0 && duplicated.Count == 0,
                $"rows differ from the visible entries: missing=[{string.Join(", ", missing)}] " +
                $"unexpected=[{string.Join(", ", unexpected)}] duplicated=[{string.Join(", ", duplicated)}]");

            // No sample entry sets an Order, so sections follow the order in which their entries are bound,
            // and so do the rows within each section. ModUtils binds its entry for the Mods tab last, when the
            // plugin registers, in a section of its own.
            var texts = GetVisibilityEntryTexts();
            var expectedPlaces = new[]
            {
                (SettingsGuiSample.EditorsCategory, new[]
                {
                    "editors/toggle", "editors/int_slider", "editors/float_slider", "editors/int_text",
                    "editors/non_negative", "editors/text", "editors/color_code", "editors/choice", "editors/flags"
                }),
                (SettingsGuiSample.KeysAndListsCategory, new[]
                {
                    "keys_and_lists/shortcut", "keys_and_lists/key", "keys_and_lists/multi_select",
                    "keys_and_lists/plain_list"
                }),
                (SettingsGuiSample.VisibilityCategory, new[] { "visibility/color", "visibility/read_only_toggle" }),
                (SettingsGuiSample.CustomCategory, new[]
                {
                    "custom/word_list", "custom/locked_word_list", "custom/broken_factory", "custom/broken_build"
                }),
                (texts.Category, new[] { VisibilityEntryIdentity })
            }.SelectMany(x => x.Item2.Select(identity => x.Item1 + " > " + identity)).ToList();
            var shownPlaces = FindRows(panel)
                              .Select(x => GetRowSectionCategory(x) + " > " + x.name.Substring(RowNamePrefix.Length))
                              .ToList();
            Expect(shownPlaces.SequenceEqual(expectedPlaces),
                $"rows are not shown in their sections in bind order: shown=[{string.Join(", ", shownPlaces)}] " +
                $"expected=[{string.Join(", ", expectedPlaces)}]");

            var visibilityEntry = FindVisibilityEntry();
            Expect(visibilityEntry != null, $"this plugin's config has no bool entry {VisibilityEntryIdentity}");
            var visibilityRow = FindRow(panel, visibilityEntry);
            Expect(visibilityRow != null, $"no shown row for {VisibilityEntryIdentity}");
            var label = FindChild(visibilityRow, RowLabelName)?.GetComponent<TMP_Text>();
            Expect(label != null && label.text == texts.Name,
                $"the row for {VisibilityEntryIdentity} is named [{label?.text}], expected [{texts.Name}]");
            var toggle = FindEditorWidget<Toggle>(panel, visibilityEntry);
            Expect(toggle != null && toggle.interactable,
                $"the row for {VisibilityEntryIdentity} shows no interactable Toggle");
            var reset = FindChild(visibilityRow, ResetButtonName);
            Expect(reset != null && reset.gameObject.activeInHierarchy && reset.GetComponent<Button>() != null,
                $"the row for {VisibilityEntryIdentity} has no Reset button");

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

            Expect(toggle.isOn == toggled, $"the toggle shows {toggle.isOn} after switching it to {toggled}");
            ExpectStored(toggleEntry, previousToggle, "before Apply");

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

            Expect(dropdown.value == index, $"the dropdown shows option {dropdown.value} after selecting {index}");
            ExpectStored(choiceEntry, previousChoice, "before Apply");

            PressApply(panel);
            yield return null;

            ExpectStored(toggleEntry, toggled, "after Apply");
            ExpectStored(choiceEntry, choices[index], "after Apply");
            ExpectApplyInteractable(panel, false, "after Apply");
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
            RequirePrecondition(floatEntry.Value == (float)floatEntry.DefaultValue,
                $"{FloatSliderIdentity} holds {floatEntry.Value} instead of its default");
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

            // The submitted value is clamped and shown, but kept in the panel until Apply.
            Expect(intField.text == max.ToString(CultureInfo.InvariantCulture),
                $"after submitting [150] the field shows [{intField.text}], expected [{max}]");
            Expect(intSlider.value == max, $"after submitting [150] the slider shows {intSlider.value}, expected {max}");
            ExpectStored(intEntry, start, "after submitting [150] before Apply");

            intField.text = "abc";
            Expect(intSlider.value == max, $"typing [abc] moved the slider from {max} to {intSlider.value}");
            intField.onSubmit.Invoke(intField.text);
            yield return null;

            Expect(intField.text == max.ToString(CultureInfo.InvariantCulture),
                $"after submitting [abc] the field shows [{intField.text}], expected the pending [{max}]");
            ExpectStored(intEntry, start, "after submitting [abc] before Apply");

            PressApply(panel);
            yield return null;
            ExpectStored(intEntry, max, "after Apply of [150]");

            intField.text = submitted.ToString(CultureInfo.InvariantCulture);
            intField.onDeselect.Invoke(intField.text);
            yield return null;

            Expect(intField.text == submitted.ToString(CultureInfo.InvariantCulture),
                $"after leaving the field with [{submitted}] it shows [{intField.text}]");
            ExpectStored(intEntry, max, $"after leaving the field with [{submitted}] before Apply");

            floatField.text = "0.333";
            floatField.onSubmit.Invoke(floatField.text);
            yield return null;

            Expect(floatField.text == "0.333",
                $"after submitting [0.333] the field shows [{floatField.text}], expected [0.333]");
            ExpectStored(floatEntry, floatEntry.DefaultValue, "after submitting [0.333] before Apply");

            PressApply(panel);
            yield return null;
            ExpectStored(intEntry, submitted, $"after Apply of [{submitted}]");
            ExpectStored(floatEntry, typedFloat, "after Apply of [0.333]");
            Expect(floatField.text == "0.333",
                $"after Apply of [0.333] the field shows [{floatField.text}], expected [0.333]");

            // Later checks and the saved config expect the defaults.
            PressReset(panel, intEntry);
            PressReset(panel, floatEntry);
            yield return null;
            PressApply(panel);
            yield return null;
            ExpectStored(intEntry, intEntry.DefaultValue, "after Reset and Apply");
            ExpectStored(floatEntry, floatEntry.DefaultValue, "after Reset and Apply");
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
            ExpectSwatch(fill, Color.green, "the submitted [#00FF00]");
            ExpectStored(colorCodeEntry, start, "after submitting [#00FF00] before Apply");

            PressApply(panel);
            yield return null;
            ExpectStored(colorCodeEntry, "#00FF00", "after Apply of [#00FF00]");
            ExpectSwatch(fill, Color.green, "the applied [#00FF00]");

            PressReset(panel, colorCodeEntry);
            yield return null;
            ExpectSwatch(fill, new Color32(0x4F, 0xA9, 0xFF, 0xFF), $"the reset [{start}]");
            Expect(field.text == start, $"after Reset the field shows [{field.text}], expected [{start}]");
            ExpectStored(colorCodeEntry, "#00FF00", "after Reset before Apply");

            PressApply(panel);
            yield return null;
            ExpectStored(colorCodeEntry, start, "after Reset and Apply");

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
            var changedToggle = toggleEntry.Value;
            var changedChoice = choiceEntry.Value;

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

            var toggle = FindEditorWidget<Toggle>(panel, toggleEntry);
            Expect(toggle != null && toggle.isOn == (bool)toggleEntry.DefaultValue,
                "the toggle does not show the default value after Reset");
            var dropdown = FindEditorWidget<TMP_Dropdown>(panel, choiceEntry);
            Expect(dropdown != null && dropdown.value == (int)(SampleChoice)choiceEntry.DefaultValue,
                "the dropdown does not show the default value after Reset");
            ExpectStored(toggleEntry, changedToggle, "after Reset before Apply");
            ExpectStored(choiceEntry, changedChoice, "after Reset before Apply");

            PressApply(panel);
            yield return null;

            ExpectStored(toggleEntry, toggleEntry.DefaultValue, "after Reset and Apply");
            ExpectStored(choiceEntry, choiceEntry.DefaultValue, "after Reset and Apply");
        }

        private IEnumerator CheckBackReturnsToModList(SettingsGuiState state)
        {
            RequireModsPage(state);
            var panel = FindOpenPanel(state);
            RequirePrecondition(panel != null, "the plugin's panel is not open");

            var apply = FindFooterButton(panel, ApplyButtonName);
            RequirePrecondition(apply != null && !apply.interactable, "the panel has pending changes");
            var back = FindFooterButton(panel, BackButtonName);
            Expect(back != null, $"the panel's footer shows no {BackButtonName} button");

            back.onClick.Invoke();
            yield return WaitFrames(2);

            Expect(Settings.instance != null, "Back on the panel closed the settings screen");
            Expect(state.List.gameObject.activeSelf, "the mod list is not shown after Back");
            Expect(FindOpenPanel(state) == null, "a panel is still shown after Back");
            Expect(!UnifiedPopup.IsVisible(), "Back without pending changes showed a popup");
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
            yield return WaitForPanelBuilt(state);

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
                yield return WaitForPanelBuilt(state);
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

        private IEnumerator CheckHiddenModHasNoButton(SettingsGuiState state)
        {
            var entry = FindVisibilityEntry();
            RequirePrecondition(entry != null, $"this plugin's config has no bool entry {VisibilityEntryIdentity}");

            try
            {
                // Another mod can still add the tab, but this plugin has no button on it.
                entry.Value = false;
                yield return OpenNewSettings(state);
                Expect(state.Settings != null, "the settings screen did not open with this plugin not listed");
                Expect(state.TabHandler != null, "the settings screen opened with this plugin not listed has no TabHandler");
                Expect(state.List == null || FindChild(state.List, state.Guid) == null,
                    $"with {VisibilityEntryIdentity} off, the mod list has a button named {state.Guid}");
                if (state.Page != null)
                    Expect(state.List != null && state.List.childCount > 0,
                        "with this plugin not listed, the Mods page was added without a mod to list");

                entry.Value = true;
                yield return OpenNewSettings(state);
                Expect(state.Settings != null && state.TabHandler != null,
                    "the settings screen did not open with this plugin listed again");
                Expect(state.Page != null && state.List != null,
                    $"with {VisibilityEntryIdentity} on again, the settings screen has no Mods page with a mod list");
                state.TabHandler.SetActiveTab(state.ModsTabIndex, forceSelect: true);
                yield return WaitFrames(2);
                Expect(FindModButton(state) != null,
                    $"with {VisibilityEntryIdentity} on again, the mod list shows no button named {state.Guid}");
            }
            finally
            {
                // The plugin stays listed until the suite puts the user's choice back.
                entry.Value = true;
            }
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
        /// Closes the settings screen if it is open and opens a new one, on which every ModUtils copy adds
        /// the Mods tab and its buttons again.
        /// </summary>
        private static IEnumerator OpenNewSettings(SettingsGuiState state)
        {
            if (Settings.instance != null)
            {
                Settings.instance.OnBack();
                yield return WaitForSettingsClosed();
                Expect(Settings.instance == null, "Back did not close the settings screen");
            }

            FejdStartup.instance.OnButtonSettings();
            yield return WaitFrames(SettingsOpenFrames);
            state.Refresh();
        }

        /// <summary>
        /// Waits after a click on the plugin's button until its panel has built every row, which the panel
        /// shows by making Reset all interactable. At least one frame passes, so the panel has been laid
        /// out. A panel still building after <see cref="PanelBuildMaxFrames"/> frames fails the check; when
        /// no panel opened, this returns and leaves that to the caller.
        /// </summary>
        private static IEnumerator WaitForPanelBuilt(SettingsGuiState state)
        {
            var built = false;
            for (var i = 0; i < PanelBuildMaxFrames && !built; i++)
            {
                yield return null;
                var panel = FindOpenPanel(state);
                if (panel == null) yield break;

                var resetAll = FindFooterButton(panel, ResetAllButtonName);
                built = resetAll != null && resetAll.interactable;
            }

            Expect(built, $"{ResetAllButtonName} is still missing or not interactable {PanelBuildMaxFrames} frames " +
                          "after the panel opened, so the panel did not finish adding its rows");
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

        /// <summary>
        /// Expects the entry and the config file on disk to hold <paramref name="value"/>.
        /// </summary>
        private void ExpectStored(ConfigEntryBase entry, object value, string when)
        {
            var expected = TomlTypeConverter.ConvertToString(value, entry.SettingType);
            var actual = entry.GetSerializedValue();
            Expect(string.Equals(actual, expected, StringComparison.Ordinal),
                $"{when}, {SettingsGuiSample.Identity(entry)} holds [{actual}], expected [{expected}]");
            ExpectSavedValue(entry, value);
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
        /// Rows the panel shows: rows that the search and the advanced toggle leave shown, in expanded
        /// sections.
        /// </summary>
        private static List<Transform> FindRows(Transform panel)
        {
            return panel.GetComponentsInChildren<Transform>(false)
                        .Where(x => x.gameObject.activeInHierarchy &&
                                    x.name.StartsWith(RowNamePrefix, StringComparison.Ordinal))
                        .ToList();
        }

        /// <summary>
        /// The category that the header of a row's section shows, without its collapse mark.
        /// </summary>
        private static string GetRowSectionCategory(Transform row)
        {
            var body = row.parent;
            var index = body.GetSiblingIndex();
            var header = index > 0 ? body.parent.GetChild(index - 1).GetComponent<Button>() : null;
            var text = header != null && header.name == SectionHeaderName ? GetHeaderText(header) : "";
            return text.Length > 2 ? text.Substring(2) : "";
        }

        private static Transform FindRow(Transform panel, ConfigEntryBase entry)
        {
            var name = RowNamePrefix + SettingsGuiSample.Identity(entry);
            return FindRows(panel).FirstOrDefault(x => string.Equals(x.name, name, StringComparison.Ordinal));
        }

        private static Button FindFooterButton(Transform panel, string name)
        {
            var footer = FindChild(panel, FooterName);
            var child = footer != null ? FindChild(footer, name) : null;
            return child != null && child.gameObject.activeInHierarchy ? child.GetComponent<Button>() : null;
        }

        /// <summary>
        /// Presses the footer's Apply button, which must be usable, as it is while changes are pending.
        /// </summary>
        private static void PressApply(Transform panel)
        {
            ExpectApplyInteractable(panel, true, "before pressing Apply");
            FindFooterButton(panel, ApplyButtonName).onClick.Invoke();
        }

        private static void ExpectApplyInteractable(Transform panel, bool interactable, string when)
        {
            var apply = FindFooterButton(panel, ApplyButtonName);
            Expect(apply != null, $"the panel's footer shows no {ApplyButtonName} button");
            Expect(apply.interactable == interactable,
                $"{when}, Apply is {(apply.interactable ? "" : "not ")}interactable");
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

        /// <summary>
        /// The entry that lists this plugin on the Mods tab, or null when the plugin's config has no bool
        /// entry with its section and key.
        /// </summary>
        private ConfigEntry<bool> FindVisibilityEntry()
        {
            var definition = new ConfigDefinition(VisibilityEntrySection, VisibilityEntryKey);
            return Config.ContainsKey(definition) ? Config[definition] as ConfigEntry<bool> : null;
        }

        /// <summary>
        /// The category, name and description of the entry that lists this plugin on the Mods tab in the
        /// game's language: Japanese for Japanese, English for every other language.
        /// </summary>
        private static (string Category, string Name, string Description) GetVisibilityEntryTexts()
        {
            var language = TryGetGameLocalization()?.GetSelectedLanguage();
            return string.Equals(language, "Japanese", StringComparison.Ordinal)
                ? (VisibilityEntryCategoryJapanese, VisibilityEntryNameJapanese, VisibilityEntryDescriptionJapanese)
                : (VisibilityEntryCategory, VisibilityEntryName, VisibilityEntryDescription);
        }

        /// <summary>
        /// Rows the panel lists with an empty search and advanced settings not shown: the sample's visible
        /// entries and the entry that lists this plugin on the Mods tab.
        /// </summary>
        private List<string> GetListedRowIdentities()
        {
            return _guiSample.VisibleEntries.Select(SettingsGuiSample.Identity)
                             .Concat(new[] { VisibilityEntryIdentity })
                             .ToList();
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
