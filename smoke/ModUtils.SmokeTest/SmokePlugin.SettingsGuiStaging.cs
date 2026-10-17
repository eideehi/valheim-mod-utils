using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModUtils.SmokeTest
{
    /// <summary>
    /// Checks of the mod panel's pending changes, Apply, Reset all, the discard prompt, search, the
    /// advanced toggle and the editors that the smoke plugin registers.
    /// </summary>
    public sealed partial class SmokePlugin
    {
        private const string ResetAllButtonName = "ResetAll";
        private const string FilterName = "Filter";
        private const string SearchFieldName = "Search";
        private const string ShowAdvancedName = "ShowAdvanced";
        private const string SectionHeaderName = "Section";
        private const string ReadOnlyNoteName = "ReadOnly";
        private const string ReadOnlyValueName = "Value";
        private const string WidgetRowName = "Row";
        private const string WidgetColumnName = "Column";
        private const string WidgetTextFieldName = "TextField";
        private const string WidgetButtonName = "Button";
        private const string WidgetLabelName = "Label";
        private const string DiscardPromptScreenshot = "settings-gui-discard-prompt.png";

        // UnifiedPopup shows Yes on its right button and No on its left button.
        private const string PopupYesButtonField = "buttonRight";
        private const string PopupNoButtonField = "buttonLeft";

        private readonly List<string> _guiErrors = new List<string>();

        private void OnGuiLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception)
                _guiErrors.Add(condition ?? "");
        }

        private IEnumerator CheckEditorsStageUntilApply(SettingsGuiState state)
        {
            yield return OpenFreshPanel(state);
            var panel = FindOpenPanel(state);
            ExpectApplyInteractable(panel, false, "on a panel opened with nothing changed");

            // A drag only shows the value while the handle is held and stages it on release.
            var floatEntry = FindSampleEntry<float>(FloatSliderIdentity);
            var floatSlider = FindEditorWidget<Slider>(panel, floatEntry);
            var floatField = FindEditorWidget<TMP_InputField>(panel, floatEntry);
            Expect(floatSlider != null && floatField != null,
                $"the row for {FloatSliderIdentity} has no Slider or TMP_InputField");
            var press = CreateHandlePress(floatSlider);
            ExecuteEvents.Execute(floatSlider.gameObject, press, ExecuteEvents.pointerDownHandler);
            floatSlider.value = 0.8f;
            yield return null;
            Expect(floatField.text == "0.8", $"while dragging to 0.8 the field shows [{floatField.text}]");
            ExpectApplyInteractable(panel, false, "while the slider handle is held");

            ExecuteEvents.Execute(floatSlider.gameObject, press, ExecuteEvents.pointerUpHandler);
            yield return null;
            ExpectApplyInteractable(panel, true, "after releasing the slider handle");
            Expect(floatField.text == "0.8", $"after releasing the handle at 0.8 the field shows [{floatField.text}]");
            ExpectStored(floatEntry, floatEntry.DefaultValue, "after the drag before Apply");

            // A slider change without a held pointer, as the arrow keys make, is staged at once.
            var intEntry = FindSampleEntry<int>(IntSliderIdentity);
            var intSlider = FindEditorWidget<Slider>(panel, intEntry);
            var intField = FindEditorWidget<TMP_InputField>(panel, intEntry);
            Expect(intSlider != null && intField != null,
                $"the row for {IntSliderIdentity} has no Slider or TMP_InputField");
            intSlider.value = 8;
            yield return null;
            Expect(intField.text == "8", $"after moving the slider to 8 the field shows [{intField.text}]");
            ExpectStored(intEntry, intEntry.DefaultValue, "after moving the slider before Apply");

            PressApply(panel);
            yield return null;
            ExpectStored(floatEntry, 0.8f, "after Apply of the drag");
            ExpectStored(intEntry, 8, "after Apply of the slider move");
            ExpectApplyInteractable(panel, false, "after Apply");

            // Flags: two changes add up, and going back to the entry's value leaves nothing pending.
            var flagsEntry = FindSampleEntry<SampleFlags>("editors/flags");
            var flagToggles = FindEditorWidgets<Toggle>(panel, flagsEntry);
            var alpha = FindToggle(flagToggles, "Alpha flag");
            var beta = FindToggle(flagToggles, "Beta flag");
            var gamma = FindToggle(flagToggles, "Gamma flag");
            beta.isOn = true;
            gamma.isOn = true;
            yield return null;
            Expect(alpha.isOn && beta.isOn && gamma.isOn,
                $"after checking Beta and Gamma the flags show {alpha.isOn}/{beta.isOn}/{gamma.isOn}");
            ExpectApplyInteractable(panel, true, "after checking two flags");
            beta.isOn = false;
            gamma.isOn = false;
            yield return null;
            Expect(alpha.isOn && !beta.isOn && !gamma.isOn,
                $"after unchecking Beta and Gamma the flags show {alpha.isOn}/{beta.isOn}/{gamma.isOn}");
            ExpectApplyInteractable(panel, false, "after changing the flags back to the entry's value");
            gamma.isOn = true;
            yield return null;
            ExpectStored(flagsEntry, flagsEntry.DefaultValue, "after checking Gamma before Apply");

            var toggleEntry = _guiSample.ToggleEntry;
            var toggle = FindEditorWidget<Toggle>(panel, toggleEntry);
            Expect(toggle != null, $"the row for {SettingsGuiSample.Identity(toggleEntry)} has no Toggle");
            toggle.isOn = !toggleEntry.Value;
            yield return null;
            Expect(toggle.isOn != toggleEntry.Value, "the toggle does not show the switched value");
            ExpectStored(toggleEntry, toggleEntry.DefaultValue, "after switching the toggle before Apply");

            var choiceEntry = _guiSample.ChoiceEntry;
            var dropdown = FindEditorWidget<TMP_Dropdown>(panel, choiceEntry);
            Expect(dropdown != null, $"the row for {SettingsGuiSample.Identity(choiceEntry)} has no TMP_Dropdown");
            dropdown.value = (int)SampleChoice.Second;
            yield return null;
            Expect(dropdown.value == (int)SampleChoice.Second, $"the dropdown shows option {dropdown.value}");
            ExpectStored(choiceEntry, choiceEntry.DefaultValue, "after selecting an option before Apply");

            // Multi-select: two changes add up.
            var multiSelectEntry = FindSampleEntry<StringList>("keys_and_lists/multi_select");
            var summary = FindEditorChild(panel, multiSelectEntry, MultiSelectSummaryName)?.GetComponent<Button>();
            Expect(summary != null, "the multi-select row has no summary button");
            summary.onClick.Invoke();
            var materials = FindEditorWidgets<Toggle>(panel, multiSelectEntry);
            FindToggle(materials, "Flint").isOn = true;
            FindToggle(materials, "Resin").isOn = true;
            yield return null;
            var checkedMaterials = new[] { "Wood", "Stone", "Flint", "Resin" };
            Expect(checkedMaterials.All(x => FindToggle(materials, x).isOn),
                "the multi-select does not show both added materials with the original ones");
            ExpectStoredWords(multiSelectEntry, new[] { "Wood", "Stone" }, "after selecting two materials before Apply");
            summary.onClick.Invoke();

            var plainListEntry = FindSampleEntry<StringList>("keys_and_lists/plain_list");
            var plainListField = FindEditorWidget<TMP_InputField>(panel, plainListEntry);
            Expect(plainListField != null, "the plain list row has no TMP_InputField");
            plainListField.text = "alpha, beta, gamma";
            plainListField.onSubmit.Invoke(plainListField.text);
            yield return null;
            ExpectWords(Csv.ParseLine(plainListField.text, true), new[] { "alpha", "beta", "gamma" },
                "the plain list field after submitting three words");
            ExpectStoredWords(plainListEntry, new[] { "alpha", "beta" }, "after submitting the list before Apply");

            // Text that is not a number brings back the pending value, not the entry's.
            var intTextEntry = FindSampleEntry<int>("editors/int_text");
            var intTextField = FindEditorWidget<TMP_InputField>(panel, intTextEntry);
            Expect(intTextField != null, "the whole number row has no TMP_InputField");
            intTextField.text = "42";
            intTextField.onSubmit.Invoke(intTextField.text);
            yield return null;
            intTextField.text = "abc";
            intTextField.onSubmit.Invoke(intTextField.text);
            yield return null;
            Expect(intTextField.text == "42",
                $"after submitting [abc] over a pending 42 the field shows [{intTextField.text}]");
            ExpectStored(intTextEntry, intTextEntry.DefaultValue, "after submitting [42] before Apply");

            var textEntry = FindSampleEntry<string>(TextIdentity);
            var textField = FindEditorWidget<TMP_InputField>(panel, textEntry);
            Expect(textField != null, $"the row for {TextIdentity} has no TMP_InputField");
            textField.text = "Hello, smoke";
            textField.onDeselect.Invoke(textField.text);
            yield return null;
            Expect(textField.text == "Hello, smoke", $"the text field shows [{textField.text}]");
            ExpectStored(textEntry, textEntry.DefaultValue, "after leaving the text field before Apply");

            intField.text = "9";
            intField.onSubmit.Invoke(intField.text);
            yield return null;
            Expect(intField.text == "9" && Mathf.Approximately(intSlider.value, 9f),
                $"after submitting [9] the slider row shows [{intField.text}] at {intSlider.value}");
            ExpectStored(intEntry, 8, "after submitting [9] before Apply");

            var colorCodeEntry = FindSampleEntry<string>(ColorCodeIdentity);
            var colorField = FindEditorWidget<TMP_InputField>(panel, colorCodeEntry);
            var colorFill = FindSwatchFill(panel, colorCodeEntry);
            Expect(colorField != null && colorFill != null, $"the row for {ColorCodeIdentity} has no field or swatch");
            colorField.text = "#123456";
            colorField.onSubmit.Invoke(colorField.text);
            yield return null;
            ExpectSwatch(colorFill, new Color32(0x12, 0x34, 0x56, 0xFF), "the submitted [#123456]");
            ExpectStored(colorCodeEntry, colorCodeEntry.DefaultValue, "after submitting [#123456] before Apply");

            var shortcutEntry = FindSampleEntry<KeyboardShortcut>("keys_and_lists/shortcut");
            var clear = FindEditorChild(panel, shortcutEntry, "Clear")?.GetComponent<Button>();
            var keyLabel = FindEditorChild(panel, shortcutEntry, "Key")?.GetComponentInChildren<TMP_Text>(true);
            Expect(clear != null && keyLabel != null, "the shortcut row has no Clear button or key label");
            clear.onClick.Invoke();
            yield return null;
            Expect(keyLabel.text == KeyboardShortcut.Empty.ToString(),
                $"after Clear the key button shows [{keyLabel.text}]");
            ExpectStored(shortcutEntry, shortcutEntry.DefaultValue, "after Clear before Apply");

            var wordListEntry = _guiSample.WordListEntry;
            var wordField = FindWordListField(panel, wordListEntry);
            wordField.text = "Flint";
            FindWordListAddButton(panel, wordListEntry).onClick.Invoke();
            yield return null;
            ExpectWordLines(panel, wordListEntry, new[] { "Wood", "Stone", "Flint" }, "after adding a word");
            ExpectStoredWords(wordListEntry, new[] { "Wood", "Stone" }, "after adding a word before Apply");

            PressApply(panel);
            yield return null;

            ExpectStored(flagsEntry, SampleFlags.Alpha | SampleFlags.Gamma, "after Apply");
            ExpectStored(toggleEntry, !(bool)toggleEntry.DefaultValue, "after Apply");
            ExpectStored(choiceEntry, SampleChoice.Second, "after Apply");
            ExpectStoredWords(multiSelectEntry, checkedMaterials, "after Apply");
            ExpectStoredWords(plainListEntry, new[] { "alpha", "beta", "gamma" }, "after Apply");
            ExpectStored(intTextEntry, 42, "after Apply");
            ExpectStored(textEntry, "Hello, smoke", "after Apply");
            ExpectStored(intEntry, 9, "after Apply");
            ExpectStored(colorCodeEntry, "#123456", "after Apply");
            ExpectStored(shortcutEntry, KeyboardShortcut.Empty, "after Apply");
            ExpectStoredWords(wordListEntry, new[] { "Wood", "Stone", "Flint" }, "after Apply");
            ExpectApplyInteractable(panel, false, "after Apply");
        }

        private IEnumerator CheckApplyButtonState(SettingsGuiState state)
        {
            yield return OpenFreshPanel(state);
            var panel = FindOpenPanel(state);
            ExpectApplyInteractable(panel, false, "with nothing changed");

            var toggleEntry = _guiSample.ToggleEntry;
            var toggle = FindEditorWidget<Toggle>(panel, toggleEntry);
            Expect(toggle != null, $"the row for {SettingsGuiSample.Identity(toggleEntry)} has no Toggle");
            var original = toggleEntry.Value;

            toggle.isOn = !original;
            yield return null;
            ExpectApplyInteractable(panel, true, "after switching the toggle");

            toggle.isOn = original;
            yield return null;
            ExpectApplyInteractable(panel, false, "after switching the toggle back to the entry's value");

            toggle.isOn = !original;
            yield return null;
            PressApply(panel);
            yield return null;
            ExpectApplyInteractable(panel, false, "after Apply");
            ExpectStored(toggleEntry, !original, "after Apply");
        }

        private IEnumerator CheckApplyShowsCorrectedValue(SettingsGuiState state)
        {
            var entry = FindSampleEntry<int>("editors/non_negative");
            yield return OpenFreshPanel(state, () => entry.Value = 4);
            var panel = FindOpenPanel(state);
            var field = FindEditorWidget<TMP_InputField>(panel, entry);
            Expect(field != null, "the non-negative row has no TMP_InputField");
            Expect(field.text == "4", $"the non-negative field shows [{field.text}] for an entry holding 4");

            // The sample's SettingChanged handler turns a negative value into 0.
            field.text = "-5";
            field.onSubmit.Invoke(field.text);
            yield return null;
            Expect(field.text == "-5", $"after submitting [-5] the field shows [{field.text}]");
            ExpectStored(entry, 4, "after submitting [-5] before Apply");

            PressApply(panel);
            yield return null;
            Expect(field.text == "0", $"after Apply of [-5] the field shows [{field.text}], expected the corrected [0]");
            ExpectStored(entry, 0, "after Apply of [-5]");
            ExpectApplyInteractable(panel, false, "after Apply of a value that the entry corrected");
        }

        private IEnumerator CheckResetAll(SettingsGuiState state)
        {
            var toggleEntry = _guiSample.ToggleEntry;
            var textEntry = FindSampleEntry<string>(TextIdentity);
            var advancedEntry = _guiSample.AdvancedEntry;
            var wordListEntry = _guiSample.WordListEntry;
            var readOnlyEntry = _guiSample.ReadOnlyToggleEntry;
            var hiddenEntry = _guiSample.HiddenEntry;
            var colorEntry = _guiSample.ColorEntry;
            var lockedWordListEntry = _guiSample.LockedWordListEntry;
            var changedColor = new Color(0.1f, 0.2f, 0.9f, 1f);
            var changedWords = new[] { "Flint" };

            yield return OpenFreshPanel(state, () =>
            {
                toggleEntry.BoxedValue = !(bool)toggleEntry.DefaultValue;
                textEntry.BoxedValue = "Changed text";
                advancedEntry.BoxedValue = 7;
                wordListEntry.BoxedValue = new SampleWordList(changedWords);
                readOnlyEntry.BoxedValue = !(bool)readOnlyEntry.DefaultValue;
                hiddenEntry.BoxedValue = "changed";
                colorEntry.BoxedValue = changedColor;
                lockedWordListEntry.BoxedValue = new SampleWordList(changedWords);
            });
            var panel = FindOpenPanel(state);
            var search = FindSearchField(panel);
            Expect(search != null, $"the panel has no {SearchFieldName} field");

            // The search hides the toggle and text rows, and the advanced row is not shown at all.
            search.text = "shortcut";
            yield return null;
            ExpectShownRows(panel, new[] { "keys_and_lists/shortcut" }, "while searching [shortcut]");

            var resetAll = FindFooterButton(panel, ResetAllButtonName);
            Expect(resetAll != null, $"the panel's footer shows no {ResetAllButtonName} button");
            resetAll.onClick.Invoke();
            yield return null;

            ExpectApplyInteractable(panel, true, "after Reset all");
            ExpectStored(toggleEntry, !(bool)toggleEntry.DefaultValue, "after Reset all before Apply");
            ExpectStored(textEntry, "Changed text", "after Reset all before Apply");
            ExpectStored(advancedEntry, 7, "after Reset all before Apply");
            ExpectStoredWords(wordListEntry, changedWords, "after Reset all before Apply");

            search.text = "";
            yield return null;
            var toggle = FindEditorWidget<Toggle>(panel, toggleEntry);
            Expect(toggle != null && toggle.isOn == (bool)toggleEntry.DefaultValue,
                "the toggle does not show its default after Reset all");
            var textField = FindEditorWidget<TMP_InputField>(panel, textEntry);
            Expect(textField != null && textField.text == (string)textEntry.DefaultValue,
                $"the text field shows [{textField?.text}] after Reset all");
            ExpectWordLines(panel, wordListEntry, new[] { "Wood", "Stone" }, "after Reset all");

            PressApply(panel);
            yield return null;

            ExpectStored(toggleEntry, toggleEntry.DefaultValue, "after Reset all and Apply");
            ExpectStored(textEntry, textEntry.DefaultValue, "after Reset all and Apply");
            ExpectStored(advancedEntry, advancedEntry.DefaultValue, "after Reset all and Apply");
            ExpectStoredWords(wordListEntry, new[] { "Wood", "Stone" }, "after Reset all and Apply");
            ExpectStored(readOnlyEntry, !(bool)readOnlyEntry.DefaultValue, "after Reset all and Apply");
            ExpectStored(hiddenEntry, "changed", "after Reset all and Apply");
            ExpectStored(colorEntry, changedColor, "after Reset all and Apply");
            ExpectStoredWords(lockedWordListEntry, changedWords, "after Reset all and Apply");
            ExpectApplyInteractable(panel, false, "after Reset all and Apply");
        }

        private IEnumerator CheckCustomEditor(SettingsGuiState state)
        {
            yield return OpenFreshPanel(state);
            var panel = FindOpenPanel(state);
            var entry = _guiSample.WordListEntry;
            var field = FindWordListField(panel, entry);
            var add = FindWordListAddButton(panel, entry);
            ExpectWordLines(panel, entry, new[] { "Wood", "Stone" }, "on a panel opened with the default");
            var startHeight = GetEditorSlotHeight(panel, entry);

            field.text = "Flint";
            add.onClick.Invoke();
            yield return null;
            ExpectWordLines(panel, entry, new[] { "Wood", "Stone", "Flint" }, "after Add");
            var addedHeight = GetEditorSlotHeight(panel, entry);
            Expect(addedHeight > startHeight, $"after Add the editor height is {addedHeight}, was {startHeight}");
            Expect(field.text == "", $"after Add the text field still shows [{field.text}]");
            ExpectStoredWords(entry, new[] { "Wood", "Stone" }, "after Add before Apply");

            PressApply(panel);
            yield return null;
            ExpectStoredWords(entry, new[] { "Wood", "Stone", "Flint" }, "after Add and Apply");

            var wood = FindWordLines(panel, entry).FirstOrDefault(x => GetWordLineText(x) == "Wood");
            var remove = wood != null ? FindChild(wood, WidgetButtonName)?.GetComponent<Button>() : null;
            Expect(remove != null, "the line for [Wood] has no Remove button");
            remove.onClick.Invoke();
            yield return null;
            ExpectWordLines(panel, entry, new[] { "Stone", "Flint" }, "after Remove");
            var removedHeight = GetEditorSlotHeight(panel, entry);
            Expect(removedHeight < addedHeight,
                $"after Remove the editor height is {removedHeight}, was {addedHeight}");
            ExpectStoredWords(entry, new[] { "Wood", "Stone", "Flint" }, "after Remove before Apply");

            PressApply(panel);
            yield return null;
            ExpectStoredWords(entry, new[] { "Stone", "Flint" }, "after Remove and Apply");

            PressReset(panel, entry);
            yield return null;
            ExpectWordLines(panel, entry, new[] { "Wood", "Stone" }, "after Reset");
            ExpectStoredWords(entry, new[] { "Stone", "Flint" }, "after Reset before Apply");

            PressApply(panel);
            yield return null;
            ExpectStoredWords(entry, new[] { "Wood", "Stone" }, "after Reset and Apply");

            // The read-only list: every widget is disabled, there is no Reset, and Add stages nothing.
            var locked = _guiSample.LockedWordListEntry;
            var lockedIdentity = SettingsGuiSample.Identity(locked);
            var lockedRow = FindRow(panel, locked);
            Expect(lockedRow != null, $"no shown row for {lockedIdentity}");
            Expect(FindChild(lockedRow, ResetButtonName) == null, $"the row for {lockedIdentity} has a Reset button");
            var enabled = lockedRow.GetComponentsInChildren<Selectable>(false).Where(x => x.interactable)
                                   .Select(x => x.name).ToList();
            Expect(enabled.Count == 0,
                $"the row for {lockedIdentity} has interactable widgets: [{string.Join(", ", enabled)}]");

            FindWordListField(panel, locked).text = "Resin";
            FindWordListAddButton(panel, locked).onClick.Invoke();
            yield return null;
            ExpectApplyInteractable(panel, false, $"after Add on {lockedIdentity}");
            ExpectWordLines(panel, locked, new[] { "Wood", "Stone" }, $"after Add on {lockedIdentity}");
            ExpectStoredWords(locked, new[] { "Wood", "Stone" }, $"after Add on {lockedIdentity}");
        }

        private IEnumerator CheckCustomEditorFailureFallsBack(SettingsGuiState state)
        {
            var mark = 0;
            yield return OpenFreshPanel(state, () => mark = _guiErrors.Count);
            var panel = FindOpenPanel(state);

            ExpectShownRows(panel, _guiSample.VisibleEntries.Select(SettingsGuiSample.Identity),
                "with the failing editors");

            foreach (var entry in new ConfigEntryBase[] { _guiSample.BrokenFactoryEntry, _guiSample.BrokenBuildEntry })
            {
                var identity = SettingsGuiSample.Identity(entry);
                var row = FindRow(panel, entry);
                Expect(row != null, $"no shown row for {identity}");
                Expect(FindChild(row, ResetButtonName) == null, $"the row for {identity} has a Reset button");
                var note = FindChild(row, ReadOnlyNoteName);
                Expect(note != null && note.gameObject.activeInHierarchy, $"the row for {identity} has no read-only note");

                var slot = FindChild(row, EditorSlotName);
                var value = slot != null ? FindChild(slot, ReadOnlyValueName)?.GetComponent<TMP_Text>() : null;
                var expected = ToReadOnlyText(entry.GetSerializedValue());
                Expect(value != null && value.text == expected,
                    $"the row for {identity} shows [{value?.text}], expected the read-only [{expected}]");
                Expect(FindChild(slot, WidgetLabelName) == null,
                    $"the row for {identity} still holds a widget of its failed editor");

                var definition = entry.Definition;
                var key = $"[{definition.Section}:{definition.Key}]";
                Expect(_guiErrors.Skip(mark).Any(x => x.Contains(key)),
                    $"no error was logged for {key} while the panel was built");
            }
        }

        private IEnumerator CheckSearchFiltersRows(SettingsGuiState state)
        {
            yield return OpenFreshPanel(state);
            var panel = FindOpenPanel(state);
            var search = FindSearchField(panel);
            Expect(search != null, $"the panel has no {SearchFieldName} field");
            var editors = FindSectionHeader(panel, SettingsGuiSample.EditorsCategory);
            var keysAndLists = FindSectionHeader(panel, SettingsGuiSample.KeysAndListsCategory);
            Expect(editors != null && keysAndLists != null, "the panel has no header for the sample sections");

            // "int_" occurs in two keys and "slider" in two other rows; only one row holds both.
            search.text = "int_ slider";
            yield return null;
            ExpectShownRows(panel, new[] { IntSliderIdentity }, "while searching [int_ slider]");
            Expect(editors.gameObject.activeInHierarchy, "the header of the section with the match is hidden");
            Expect(!keysAndLists.gameObject.activeInHierarchy,
                "the header of a section without a match is shown");

            search.text = "INT_ SLIDER";
            yield return null;
            ExpectShownRows(panel, new[] { IntSliderIdentity }, "while searching [INT_ SLIDER]");

            // The category matches every row of its section; the hidden entry never shows, the advanced one does.
            search.text = "hidden";
            yield return null;
            ExpectShownRows(panel,
                new ConfigEntryBase[] { _guiSample.ColorEntry, _guiSample.ReadOnlyToggleEntry, _guiSample.AdvancedEntry }
                    .Select(SettingsGuiSample.Identity), "while searching [hidden]");

            search.text = "";
            yield return null;
            ExpectShownRows(panel, _guiSample.VisibleEntries.Select(SettingsGuiSample.Identity),
                "after clearing the search");

            // A section collapsed before the search opens for a match and closes again afterwards.
            keysAndLists.onClick.Invoke();
            yield return null;
            Expect(FindRow(panel, FindSampleEntry<KeyboardShortcut>("keys_and_lists/shortcut")) == null,
                "the collapsed section still shows its rows");
            search.text = "shortcut";
            yield return null;
            ExpectShownRows(panel, new[] { "keys_and_lists/shortcut" }, "while searching [shortcut]");
            search.text = "";
            yield return null;
            Expect(GetHeaderText(keysAndLists).StartsWith("+ ", StringComparison.Ordinal),
                $"after clearing the search the section header shows [{GetHeaderText(keysAndLists)}]");
            Expect(FindRow(panel, FindSampleEntry<KeyboardShortcut>("keys_and_lists/shortcut")) == null,
                "after clearing the search the collapsed section shows its rows");
            Expect(FindRow(panel, _guiSample.ToggleEntry) != null,
                "after clearing the search a row of an expanded section is hidden");

            keysAndLists.onClick.Invoke();
            yield return null;
            ExpectShownRows(panel, _guiSample.VisibleEntries.Select(SettingsGuiSample.Identity),
                "after expanding the section again");
            ExpectApplyInteractable(panel, false, "after searching");
        }

        private IEnumerator CheckAdvancedToggle(SettingsGuiState state)
        {
            yield return OpenFreshPanel(state);
            try
            {
                var panel = FindOpenPanel(state);
                var advanced = _guiSample.AdvancedEntry;
                var toggle = FindAdvancedToggle(panel);
                Expect(toggle != null, $"the panel has no {ShowAdvancedName} toggle");
                Expect(!toggle.isOn, "the advanced toggle starts on");
                Expect(FindRow(panel, advanced) == null, "the advanced row is shown with the toggle off");
                Expect(FindRow(panel, _guiSample.ColorEntry) != null, "the rows of the advanced row's section are hidden");

                toggle.isOn = true;
                yield return null;
                ExpectAdvancedRowShown(panel, "after turning the toggle on");

                var back = FindFooterButton(panel, BackButtonName);
                Expect(back != null, $"the panel's footer shows no {BackButtonName} button");
                back.onClick.Invoke();
                yield return WaitFrames(2);
                var button = FindModButton(state);
                Expect(button != null, "the mod list is not shown after Back");
                button.onClick.Invoke();
                yield return WaitFrames(2);
                panel = FindOpenPanel(state);
                Expect(panel != null && FindAdvancedToggle(panel)?.isOn == true,
                    "the advanced toggle is off after reopening the panel");
                ExpectAdvancedRowShown(panel, "after reopening the panel");

                Settings.instance.OnBack();
                yield return WaitForSettingsClosed();
                Expect(Settings.instance == null, "Back did not close the settings screen");
                FejdStartup.instance.OnButtonSettings();
                yield return WaitFrames(SettingsOpenFrames);
                state.Refresh();
                RequireModsPage(state);
                state.TabHandler.SetActiveTab(state.ModsTabIndex, forceSelect: true);
                yield return WaitFrames(2);
                button = FindModButton(state);
                Expect(button != null, "the mod list is not shown after reopening the settings screen");
                button.onClick.Invoke();
                yield return WaitFrames(2);
                panel = FindOpenPanel(state);
                toggle = panel != null ? FindAdvancedToggle(panel) : null;
                Expect(toggle != null && toggle.isOn, "the advanced toggle is off after reopening the settings screen");
                ExpectAdvancedRowShown(panel, "after reopening the settings screen");

                toggle.isOn = false;
                yield return null;
                Expect(FindRow(panel, advanced) == null, "the advanced row is shown after turning the toggle off");
            }
            finally
            {
                // Later checks and the user's session start with the toggle off.
                var panel = FindOpenPanel(state);
                var toggle = panel != null ? FindAdvancedToggle(panel) : null;
                if (toggle != null && toggle.isOn)
                    toggle.isOn = false;
            }
        }

        private IEnumerator CheckBackWithPendingAsks(SettingsGuiState state)
        {
            RequirePrecondition(UnifiedPopup.IsAvailable(), "the game's popup is not available");
            yield return OpenFreshPanel(state);
            var panel = FindOpenPanel(state);
            ExpectApplyInteractable(panel, false, "before the change");

            var entry = _guiSample.ToggleEntry;
            var original = entry.Value;
            var toggle = FindEditorWidget<Toggle>(panel, entry);
            Expect(toggle != null, $"the row for {SettingsGuiSample.Identity(entry)} has no Toggle");
            toggle.isOn = !original;
            yield return null;

            FindFooterButton(panel, BackButtonName).onClick.Invoke();
            yield return null;
            Expect(UnifiedPopup.IsVisible(), "Back with a pending change showed no popup");
            Expect(FindOpenPanel(state) == panel, "Back with a pending change closed the panel");
            yield return CaptureScreenshot(DiscardPromptScreenshot);

            PressPopupButton(PopupNoButtonField);
            yield return WaitFrames(2);
            Expect(!UnifiedPopup.IsVisible(), "the popup is still shown after No");
            Expect(FindOpenPanel(state) == panel, "No closed the panel");
            Expect(toggle.isOn == !original, "No dropped the pending change from the row");
            ExpectApplyInteractable(panel, true, "after No");
            ExpectStored(entry, original, "after No");

            FindFooterButton(panel, BackButtonName).onClick.Invoke();
            yield return null;
            Expect(UnifiedPopup.IsVisible(), "the second Back with a pending change showed no popup");
            PressPopupButton(PopupYesButtonField);
            yield return WaitFrames(2);
            Expect(!UnifiedPopup.IsVisible(), "the popup is still shown after Yes");
            Expect(state.List.gameObject.activeSelf, "the mod list is not shown after Yes");
            Expect(FindOpenPanel(state) == null, "a panel is still shown after Yes");
            ExpectStored(entry, original, "after Yes");

            // Another tab closing the panel drops its pending change and dismisses the popup it was showing,
            // so Escape cannot reach Settings while the popup is still up.
            var button = FindModButton(state);
            Expect(button != null, "the mod list has no button for this plugin");
            button.onClick.Invoke();
            yield return WaitFrames(2);
            panel = FindOpenPanel(state);
            Expect(panel != null, "the plugin's panel did not open again");
            toggle = FindEditorWidget<Toggle>(panel, entry);
            toggle.isOn = !original;
            yield return null;
            FindFooterButton(panel, BackButtonName).onClick.Invoke();
            yield return null;
            Expect(UnifiedPopup.IsVisible(), "Back with a pending change showed no popup");

            state.TabHandler.SetActiveTab(0, forceSelect: true);
            yield return WaitFrames(2);
            Expect(FindChild(state.Panels, state.Guid) == null, "the panel was not closed by selecting another tab");
            Expect(!UnifiedPopup.IsVisible(), "the popup is still shown after another tab closed its panel");

            state.TabHandler.SetActiveTab(state.ModsTabIndex, forceSelect: true);
            yield return WaitFrames(2);
            Expect(state.List.gameObject.activeInHierarchy, "the mod list is not shown when returning to the Mods tab");
            ExpectStored(entry, original, "after leaving the Mods tab with the popup shown");
        }

        private IEnumerator CheckTabSwitchDiscardsPending(SettingsGuiState state)
        {
            yield return OpenFreshPanel(state);
            var panel = FindOpenPanel(state);
            ExpectApplyInteractable(panel, false, "before the change");
            var entry = _guiSample.ToggleEntry;
            var original = entry.Value;
            var toggle = FindEditorWidget<Toggle>(panel, entry);
            Expect(toggle != null, $"the row for {SettingsGuiSample.Identity(entry)} has no Toggle");
            toggle.isOn = !original;
            yield return null;
            ExpectStored(entry, original, "after switching the toggle");

            state.TabHandler.SetActiveTab(0, forceSelect: true);
            yield return WaitFrames(2);
            state.TabHandler.SetActiveTab(state.ModsTabIndex, forceSelect: true);
            yield return WaitFrames(1);

            Expect(state.List.gameObject.activeInHierarchy, "the mod list is not shown when returning to the Mods tab");
            Expect(FindOpenPanel(state) == null, "a panel is still shown when returning to the Mods tab");
            Expect(!UnifiedPopup.IsVisible(), "leaving the Mods tab with a pending change showed a popup");
            ExpectStored(entry, original, "after leaving the Mods tab");

            FindModButton(state).onClick.Invoke();
            yield return WaitFrames(2);
            panel = FindOpenPanel(state);
            Expect(panel != null, "the plugin's panel did not open again");
            toggle = FindEditorWidget<Toggle>(panel, entry);
            Expect(toggle != null && toggle.isOn == original,
                "the reopened panel does not show the entry's value");
            ExpectApplyInteractable(panel, false, "after reopening the panel");
        }

        /// <summary>
        /// Closes the plugin's panel by leaving the Mods tab, which drops pending changes without asking,
        /// puts the sample entries back to their defaults, lets <paramref name="prepare"/> change entries
        /// while no panel shows them, and opens the panel again with an empty search and the advanced
        /// toggle off.
        /// </summary>
        private IEnumerator OpenFreshPanel(SettingsGuiState state, Action prepare = null)
        {
            RequireModsPage(state);
            RequirePrecondition(state.ModsTabIndex != 0, "the Mods tab is the first tab, so there is no other tab to switch to");
            RequirePrecondition(!UnifiedPopup.IsVisible(), "a popup is shown");

            state.TabHandler.SetActiveTab(0, forceSelect: true);
            yield return WaitFrames(2);
            _guiSample.RestoreDefaults();
            prepare?.Invoke();
            state.TabHandler.SetActiveTab(state.ModsTabIndex, forceSelect: true);
            yield return WaitFrames(1);

            var button = FindModButton(state);
            RequirePrecondition(button != null, "the mod list has no button for this plugin");
            button.onClick.Invoke();
            yield return WaitFrames(2);

            var panel = FindOpenPanel(state);
            RequirePrecondition(panel != null, "the plugin's panel could not be opened");
            var toggle = FindAdvancedToggle(panel);
            if (toggle != null && toggle.isOn)
            {
                toggle.isOn = false;
                yield return null;
            }
        }

        /// <summary>
        /// A left-button press on the slider's handle, aimed through the event system's raycast as a real
        /// click is, so that the slider keeps its value on pointer down instead of jumping to the pointer.
        /// </summary>
        private static PointerEventData CreateHandlePress(Slider slider)
        {
            var eventSystem = EventSystem.current;
            Expect(eventSystem != null, "there is no EventSystem to send pointer events through");
            Expect(slider.handleRect != null, "the slider has no handle");
            var raycaster = slider.GetComponentInParent<GraphicRaycaster>();
            Expect(raycaster != null, "the slider is not on a canvas that receives pointer events");

            // A canvas drawn over the screen has no camera, and its world positions are screen positions.
            var camera = raycaster.eventCamera;
            var world = slider.handleRect.position;
            Vector2 position = camera != null ? camera.WorldToScreenPoint(world) : world;
            var press = new PointerEventData(eventSystem)
            {
                button = PointerEventData.InputButton.Left,
                position = position,
                pressPosition = position
            };

            var hits = new List<RaycastResult>();
            eventSystem.RaycastAll(press, hits);
            var hit = hits.FirstOrDefault(x => x.gameObject != null && x.gameObject.transform.IsChildOf(slider.transform));
            Expect(hit.gameObject != null, $"no raycast at {position} reaches the slider's handle");
            press.pointerCurrentRaycast = hit;
            press.pointerPressRaycast = hit;
            return press;
        }

        private static void PressPopupButton(string fieldName)
        {
            var popup = AccessTools.Field(typeof(UnifiedPopup), "instance")?.GetValue(null) as UnifiedPopup;
            Expect(popup != null, "the game's popup has no instance");
            var button = AccessTools.Field(typeof(UnifiedPopup), fieldName)?.GetValue(popup) as Button;
            Expect(button != null && button.gameObject.activeInHierarchy, $"the popup shows no {fieldName}");
            button.onClick.Invoke();
        }

        private void ExpectAdvancedRowShown(Transform panel, string when)
        {
            var row = FindRow(panel, _guiSample.AdvancedEntry);
            var sibling = FindRow(panel, _guiSample.ColorEntry);
            Expect(row != null, $"{when}, the advanced row is not shown");
            Expect(sibling != null && row.parent == sibling.parent, $"{when}, the advanced row is not in its section");
        }

        private static TMP_InputField FindSearchField(Transform panel)
        {
            var filter = FindChild(panel, FilterName);
            var field = filter != null ? FindChild(filter, SearchFieldName) : null;
            return field != null && field.gameObject.activeInHierarchy ? field.GetComponent<TMP_InputField>() : null;
        }

        private static Toggle FindAdvancedToggle(Transform panel)
        {
            var filter = FindChild(panel, FilterName);
            var toggle = filter != null ? FindChild(filter, ShowAdvancedName) : null;
            return toggle != null && toggle.gameObject.activeInHierarchy ? toggle.GetComponent<Toggle>() : null;
        }

        private static Button FindSectionHeader(Transform panel, string category)
        {
            return panel.GetComponentsInChildren<Button>(true)
                        .FirstOrDefault(x => x.name == SectionHeaderName && GetHeaderText(x).Length > 2 &&
                                             GetHeaderText(x).Substring(2) == category);
        }

        private static string GetHeaderText(Button header)
        {
            return header.GetComponentInChildren<TMP_Text>(true)?.text ?? "";
        }

        private static void ExpectShownRows(Transform panel, IEnumerable<string> expected, string when)
        {
            var shown = FindRows(panel).Select(x => x.name.Substring(RowNamePrefix.Length)).ToList();
            var wanted = expected.ToList();
            var missing = wanted.Except(shown).ToList();
            var unexpected = shown.Except(wanted).ToList();
            Expect(missing.Count == 0 && unexpected.Count == 0,
                $"{when}, the rows differ: missing=[{string.Join(", ", missing)}] " +
                $"unexpected=[{string.Join(", ", unexpected)}]");
        }

        private static Transform FindEditorChild(Transform panel, ConfigEntryBase entry, string name)
        {
            var row = FindRow(panel, entry);
            var slot = row != null ? FindChild(row, EditorSlotName) : null;
            return slot != null ? FindChild(slot, name) : null;
        }

        private static Toggle FindToggle(IEnumerable<Toggle> toggles, string label)
        {
            var toggle = toggles.FirstOrDefault(x => x.GetComponentsInChildren<TMP_Text>(true).Any(t => t.text == label));
            Expect(toggle != null, $"no toggle is labelled [{label}]");
            return toggle;
        }

        private static float GetEditorSlotHeight(Transform panel, ConfigEntryBase entry)
        {
            var row = FindRow(panel, entry);
            var slot = row != null ? FindChild(row, EditorSlotName) : null;
            var layout = slot != null ? slot.GetComponent<LayoutElement>() : null;
            Expect(layout != null, $"the row for {SettingsGuiSample.Identity(entry)} has no sized editor area");
            return layout.preferredHeight;
        }

        /// <summary>
        /// The text field on the first line of the sample word list editor.
        /// </summary>
        private static TMP_InputField FindWordListField(Transform panel, ConfigEntryBase entry)
        {
            var input = FindEditorChild(panel, entry, WidgetRowName);
            var field = input != null ? FindChild(input, WidgetTextFieldName)?.GetComponent<TMP_InputField>() : null;
            Expect(field != null, $"the row for {SettingsGuiSample.Identity(entry)} has no text field on its first line");
            return field;
        }

        private static Button FindWordListAddButton(Transform panel, ConfigEntryBase entry)
        {
            var input = FindEditorChild(panel, entry, WidgetRowName);
            var button = input != null ? FindChild(input, WidgetButtonName)?.GetComponent<Button>() : null;
            Expect(button != null, $"the row for {SettingsGuiSample.Identity(entry)} has no Add button");
            return button;
        }

        /// <summary>
        /// The shown lines of the sample word list editor, one per word.
        /// </summary>
        private static List<Transform> FindWordLines(Transform panel, ConfigEntryBase entry)
        {
            var column = FindEditorChild(panel, entry, WidgetColumnName);
            var lines = new List<Transform>();
            if (column == null) return lines;

            for (var i = 0; i < column.childCount; i++)
                if (column.GetChild(i).gameObject.activeInHierarchy)
                    lines.Add(column.GetChild(i));
            return lines;
        }

        private static string GetWordLineText(Transform line)
        {
            return FindChild(line, WidgetLabelName)?.GetComponent<TMP_Text>()?.text;
        }

        private static void ExpectWordLines(Transform panel, ConfigEntryBase entry, string[] words, string when)
        {
            var shown = FindWordLines(panel, entry).Select(GetWordLineText).ToList();
            Expect(shown.SequenceEqual(words),
                $"{when}, {SettingsGuiSample.Identity(entry)} shows [{string.Join(", ", shown)}], " +
                $"expected [{string.Join(", ", words)}]");
        }

        private static void ExpectWords(IEnumerable<string> actual, string[] expected, string what)
        {
            var words = actual.Where(x => x.Length > 0).ToList();
            Expect(words.Count == expected.Length && !expected.Except(words).Any(),
                $"{what} holds [{string.Join(", ", words)}], expected [{string.Join(", ", expected)}]");
        }

        /// <summary>
        /// Expects a list entry and the config file on disk to hold exactly <paramref name="words"/>, in
        /// any order.
        /// </summary>
        private void ExpectStoredWords(ConfigEntryBase entry, string[] words, string when)
        {
            var identity = SettingsGuiSample.Identity(entry);
            ExpectWords((IEnumerable<string>)entry.BoxedValue, words, $"{when}, {identity}");
            var saved = ReadSavedValue(entry.Definition) ?? "";
            ExpectWords(Csv.ParseLine(saved, true), words, $"{when}, the config file for {identity}");
        }
    }
}
