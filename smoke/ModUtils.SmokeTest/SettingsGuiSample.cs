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
    /// hidden, advanced, read-only and self-correcting entries.
    /// </summary>
    internal sealed class SettingsGuiSample
    {
        private const string L10NPrefix = "modutils_smoke_gui";
        private const string EditorsSection = "editors";
        private const string KeysAndListsSection = "keys_and_lists";
        private const string VisibilitySection = "visibility";
        private const string EditorsCategory = "Editors";
        private const string KeysAndListsCategory = "Keys and lists";
        private const string VisibilityCategory = "Read-only and hidden";

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
            var text = configuration.Bind(EditorsSection, "text", "Hello, Valheim",
                initializer: Describe(EditorsCategory, "Text", "A string entry, edited as text."));
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
            var hidden = configuration.Bind(VisibilitySection, "hidden_text", "hidden",
                initializer: Describe(VisibilityCategory, "Hidden text", "Never shown on the config screen.",
                    attributes => attributes.Browsable = false));
            var advanced = configuration.Bind(VisibilitySection, "advanced_number", 1,
                initializer: Describe(VisibilityCategory, "Advanced number",
                    "An advanced entry. The config screen hides it.",
                    attributes => attributes.IsAdvanced = true));

            VisibleEntries = new List<ConfigEntryBase>
            {
                ToggleEntry, intSlider, floatSlider, intText, nonNegative, text, colorCode, ChoiceEntry, flags,
                shortcut, key, multiSelect, plainList, ColorEntry, ReadOnlyToggleEntry
            };
            AllEntries = VisibleEntries.Concat(new ConfigEntryBase[] { hidden, advanced }).ToList();
        }

        internal ConfigEntry<bool> ToggleEntry { get; }

        internal ConfigEntry<SampleChoice> ChoiceEntry { get; }

        internal ConfigEntry<Color> ColorEntry { get; }

        internal ConfigEntry<bool> ReadOnlyToggleEntry { get; }

        /// <summary>
        /// Entries the config screen must list; the hidden and the advanced entry are not among them.
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
