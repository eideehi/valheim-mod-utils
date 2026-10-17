using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace ModUtils.SmokeTest
{
    public enum SampleChoice
    {
        [Description("First choice")] First,
        [Description("Second choice")] Second,
        [Description("Third choice")] Third
    }

    [Flags]
    public enum SampleFlags
    {
        None = 0,
        [Description("Alpha flag")] Alpha = 1,
        [Description("Beta flag")] Beta = 2,
        [Description("Gamma flag")] Gamma = 4
    }

    /// <summary>
    /// Entries that the smoke plugin shows on the in-game config screen: one per editor kind, plus
    /// hidden, advanced, read-only and self-correcting entries, and entries of the plugin's own types
    /// that use the editors it registers.
    /// </summary>
    internal sealed class SettingsGuiSample
    {
        private const string L10NPrefix = "modutils_smoke_gui";
        private const string EditorsSection = "editors";
        private const string KeysAndListsSection = "keys_and_lists";
        private const string VisibilitySection = "visibility";
        private const string CustomSection = "custom";
        internal const string EditorsCategory = "Editors";
        internal const string KeysAndListsCategory = "Keys and lists";
        internal const string VisibilityCategory = "Read-only and hidden";
        internal const string CustomCategory = "Custom editors";

        private static readonly string[] Materials =
            { "Wood", "Stone", "Flint", "Resin", "Feathers", "Leather scraps" };

        internal SettingsGuiSample(ConfigFile config)
        {
            var configuration = new Configuration(config, new L10N(L10NPrefix));

            ToggleEntry = configuration.Bind(EditorsSection, "toggle", false,
                initializer: Describe(EditorsCategory, "Toggle", "A bool entry, edited with a toggle."));
            var intSlider = configuration.Bind(EditorsSection, "int_slider", 5, (0, 10),
                Describe(EditorsCategory, "Whole number slider", "A whole number from 0 to 10."));
            var floatSlider = configuration.Bind(EditorsSection, "float_slider", 0.5f, (0f, 1f),
                Describe(EditorsCategory, "Decimal slider", "A decimal number from 0 to 1."));
            var intText = configuration.Bind(EditorsSection, "int_text", 3,
                initializer: Describe(EditorsCategory, "Whole number",
                    "A whole number without a range, edited as text. Text that is not a number is rejected."));
            var nonNegative = configuration.Bind(EditorsSection, "non_negative", 0,
                initializer: Describe(EditorsCategory, "Non-negative number",
                    "A whole number without a range. The plugin turns negative values into 0."));
            nonNegative.SettingChanged += (sender, args) =>
            {
                if (nonNegative.Value < 0)
                    nonNegative.Value = 0;
            };
            // The description is longer than the area below the list, so the config screen unfolds it.
            var text = configuration.Bind(EditorsSection, "text", "Hello, Valheim",
                initializer: Describe(EditorsCategory, "Text",
                    "A string entry, edited as text. Its description is long on purpose. The area below the " +
                    "list shows only the first lines of a description, so the config screen unfolds the " +
                    "whole text over the list once the pointer has stayed on the row for a moment, and " +
                    "folds it again when the pointer moves to another row or leaves the list. Mods with " +
                    "many options often explain each one at length: what the option changes, when it takes " +
                    "effect, which values make sense, how it works together with other options, and what " +
                    "to expect in multiplayer, where the server and every client may need the same value. " +
                    "Descriptions of several hundred characters are therefore common, and the longest ones " +
                    "come close to nine hundred. The sentences after the first one only make this " +
                    "description long enough to need unfolding at any common screen size, so that the " +
                    "smoke check can show the unfolded text in its screenshot."));
            var colorCode = configuration.Bind(EditorsSection, "color_code", "#4FA9FF",
                initializer: Describe(EditorsCategory, "Colour code",
                    "A colour code entry, edited as text with a swatch of the colour."));
            ChoiceEntry = configuration.Bind(EditorsSection, "choice", SampleChoice.First,
                initializer: Describe(EditorsCategory, "Choice",
                    "An enum entry. The options show the labels of the enum members."));
            var flags = configuration.Bind(EditorsSection, "flags", SampleFlags.Alpha,
                initializer: Describe(EditorsCategory, "Flags", "A flags enum entry, one toggle per flag."));

            var shortcut = configuration.Bind(KeysAndListsSection, "shortcut",
                new KeyboardShortcut(KeyCode.F7, KeyCode.LeftControl),
                initializer: Describe(KeysAndListsCategory, "Shortcut",
                    "A key with modifiers. Click, then press the keys; Escape cancels."));
            var key = configuration.Bind(KeysAndListsSection, "key", KeyCode.F8,
                initializer: Describe(KeysAndListsCategory, "Single key", "One key without modifiers."));
            var multiSelect = configuration.Bind(KeysAndListsSection, "multi_select",
                new StringList(new[] { "Wood", "Stone" }),
                initializer: Describe(KeysAndListsCategory, "Materials", "A selection from a fixed list.",
                    attributes => attributes.CustomDrawer =
                        ConfigurationCustomDrawer.MultiSelect<string>(() => Materials, x => x)));
            var plainList = configuration.Bind(KeysAndListsSection, "plain_list",
                new StringList(new[] { "alpha", "beta" }),
                initializer: Describe(KeysAndListsCategory, "Word list", "A list of words separated by commas."));

            ColorEntry = configuration.Bind(VisibilitySection, "color", new Color(0.85f, 0.65f, 0.25f, 1f),
                initializer: Describe(VisibilityCategory, "Color",
                    "A colour entry. It has no editor, so it is shown read-only."));
            ReadOnlyToggleEntry = configuration.Bind(VisibilitySection, "read_only_toggle", true,
                initializer: Describe(VisibilityCategory, "Locked toggle", "A bool entry marked read-only.",
                    attributes => attributes.ReadOnly = true));
            HiddenEntry = configuration.Bind(VisibilitySection, "hidden_text", "hidden",
                initializer: Describe(VisibilityCategory, "Hidden text", "Never shown on the config screen.",
                    attributes => attributes.Browsable = false));
            AdvancedEntry = configuration.Bind(VisibilitySection, "advanced_number", 1,
                initializer: Describe(VisibilityCategory, "Advanced number",
                    "An advanced entry. The config screen shows it only with advanced settings shown.",
                    attributes => attributes.IsAdvanced = true));

            var words = new[] { "Wood", "Stone" };
            WordListEntry = configuration.Bind(CustomSection, "word_list", new SampleWordList(words),
                initializer: Describe(CustomCategory, "Custom words",
                    "A list of words, edited by the sample editor that the smoke plugin registers."));
            LockedWordListEntry = configuration.Bind(CustomSection, "locked_word_list", new SampleWordList(words),
                initializer: Describe(CustomCategory, "Locked custom words",
                    "The same kind of list, marked read-only.", attributes => attributes.ReadOnly = true));
            BrokenFactoryEntry = configuration.Bind(CustomSection, "broken_factory",
                new SampleBrokenFactoryValue("factory sample"),
                initializer: Describe(CustomCategory, "Editor that cannot be created",
                    "Its registered editor factory fails, so the row shows the value read-only."));
            BrokenBuildEntry = configuration.Bind(CustomSection, "broken_build",
                new SampleBrokenBuildValue("build sample"),
                initializer: Describe(CustomCategory, "Editor that fails to build",
                    "Its registered editor fails while building, so the row shows the value read-only."));

            VisibleEntries = new List<ConfigEntryBase>
            {
                ToggleEntry, intSlider, floatSlider, intText, nonNegative, text, colorCode, ChoiceEntry, flags,
                shortcut, key, multiSelect, plainList, ColorEntry, ReadOnlyToggleEntry, WordListEntry,
                LockedWordListEntry, BrokenFactoryEntry, BrokenBuildEntry
            };
            AllEntries = VisibleEntries.Concat(new ConfigEntryBase[] { HiddenEntry, AdvancedEntry }).ToList();
        }

        internal ConfigEntry<bool> ToggleEntry { get; }

        internal ConfigEntry<SampleChoice> ChoiceEntry { get; }

        internal ConfigEntry<Color> ColorEntry { get; }

        internal ConfigEntry<bool> ReadOnlyToggleEntry { get; }

        internal ConfigEntry<string> HiddenEntry { get; }

        internal ConfigEntry<int> AdvancedEntry { get; }

        internal ConfigEntry<SampleWordList> WordListEntry { get; }

        internal ConfigEntry<SampleWordList> LockedWordListEntry { get; }

        internal ConfigEntry<SampleBrokenFactoryValue> BrokenFactoryEntry { get; }

        internal ConfigEntry<SampleBrokenBuildValue> BrokenBuildEntry { get; }

        /// <summary>
        /// Entries the config screen lists with an empty search and advanced settings not shown; the
        /// hidden and the advanced entry are not among them.
        /// </summary>
        internal IList<ConfigEntryBase> VisibleEntries { get; }

        internal IList<ConfigEntryBase> AllEntries { get; }

        /// <summary>
        /// Section and key of an entry, as the config screen uses them in its row names.
        /// </summary>
        internal static string Identity(ConfigEntryBase entry)
        {
            return entry.Definition.Section + "/" + entry.Definition.Key;
        }

        /// <summary>
        /// Puts every sample entry back to its default value, writing only the ones that differ.
        /// </summary>
        internal void RestoreDefaults()
        {
            foreach (var entry in AllEntries)
            {
                var defaultText = TomlTypeConverter.ConvertToString(entry.DefaultValue, entry.SettingType);
                if (!string.Equals(entry.GetSerializedValue(), defaultText, StringComparison.Ordinal))
                    entry.BoxedValue = entry.DefaultValue;
            }
        }

        private static Action<ConfigurationManagerAttributes> Describe(string category, string name,
            string description, Action<ConfigurationManagerAttributes> extra = null)
        {
            return attributes =>
            {
                attributes.Category = category;
                attributes.DispName = name;
                attributes.Description = description;
                extra?.Invoke(attributes);
            };
        }
    }
}
