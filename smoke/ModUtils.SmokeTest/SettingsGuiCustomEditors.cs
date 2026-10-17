using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ModUtils.SmokeTest
{
    /// <summary>
    /// A list of words that only the sample editor below edits: it has a TOML converter, and no built-in
    /// editor matches it.
    /// </summary>
    public sealed class SampleWordList : IEnumerable<string>
    {
        private readonly List<string> _words;

        public SampleWordList(IEnumerable<string> words)
        {
            _words = words.ToList();
        }

        public int Count => _words.Count;

        public IEnumerator<string> GetEnumerator()
        {
            return _words.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    /// <summary>
    /// A value whose registered editor factory fails.
    /// </summary>
    public sealed class SampleBrokenFactoryValue
    {
        public SampleBrokenFactoryValue(string text)
        {
            Text = text;
        }

        public string Text { get; }
    }

    /// <summary>
    /// A value whose registered editor fails in Build.
    /// </summary>
    public sealed class SampleBrokenBuildValue
    {
        public SampleBrokenBuildValue(string text)
        {
            Text = text;
        }

        public string Text { get; }
    }

    /// <summary>
    /// The smoke plugin's own setting types and the editors it registers for them on the config screen.
    /// </summary>
    internal static class SettingsGuiCustomEditors
    {
        /// <summary>
        /// Adds the TOML converters, which BepInEx needs before the entries are bound, and registers the
        /// editors, which the config screen needs before the plugin is registered.
        /// </summary>
        internal static void Register()
        {
            TomlTypeConverter.AddConverter(typeof(SampleWordList), new TypeConverter
            {
                ConvertToString = (value, type) => string.Join(", ", ((SampleWordList)value).Select(Csv.Escape)),
                ConvertToObject = (text, type) => new SampleWordList(string.IsNullOrEmpty(text)
                    ? Enumerable.Empty<string>()
                    : Csv.ParseLine(text, true).Where(x => x.Length > 0))
            });
            TomlTypeConverter.AddConverter(typeof(SampleBrokenFactoryValue), new TypeConverter
            {
                ConvertToString = (value, type) => ((SampleBrokenFactoryValue)value).Text,
                ConvertToObject = (text, type) => new SampleBrokenFactoryValue(text)
            });
            TomlTypeConverter.AddConverter(typeof(SampleBrokenBuildValue), new TypeConverter
            {
                ConvertToString = (value, type) => ((SampleBrokenBuildValue)value).Text,
                ConvertToObject = (text, type) => new SampleBrokenBuildValue(text)
            });

            ConfigurationGui.RegisterEditor((type, acceptableValues) => type == typeof(SampleWordList),
                () => new SampleWordListEditor());
            ConfigurationGui.RegisterEditor((type, acceptableValues) => type == typeof(SampleBrokenFactoryValue),
                () => throw new InvalidOperationException("The smoke sample editor factory fails on purpose."));
            ConfigurationGui.RegisterEditor((type, acceptableValues) => type == typeof(SampleBrokenBuildValue),
                () => new SampleBrokenBuildEditor());
        }
    }

    /// <summary>
    /// Edits a <see cref="SampleWordList"/> like Automatics' list drawer: a text field and an Add button
    /// on the first line, then one line per word with a Remove button.
    /// </summary>
    internal sealed class SampleWordListEditor : ConfigurationGuiCustomEditor
    {
        private const float ButtonWidth = 120f;

        // The spacing that the slot and the column put between lines.
        private const float LineSpacing = 4f;

        private ConfigurationGuiInputField _field;
        private RectTransform _lines;
        private ConfigurationGuiWidgets _widgets;

        private SampleWordList Words => (SampleWordList)Value;

        protected override float Build(RectTransform slot, ConfigurationGuiWidgets widgets)
        {
            _widgets = widgets;
            var height = widgets.ControlHeight;

            var input = widgets.CreateRow(slot);
            _field = widgets.CreateTextField(input, _ => { });
            if (_field != null)
                widgets.SetLayout(_field.Input.gameObject, preferredWidth: 0f, flexibleWidth: 1f, height: height);
            var add = widgets.CreateButton(input, "Add", Add);
            widgets.SetLayout(add.gameObject, ButtonWidth, ButtonWidth, height: height);

            _lines = widgets.CreateColumn(slot);

            LineHeight = height;
            Refresh();
            return Height();
        }

        protected override void Refresh()
        {
            // Lines are hidden at once, because Destroy removes them only at the end of the frame.
            for (var i = _lines.childCount - 1; i >= 0; i--)
            {
                var line = _lines.GetChild(i).gameObject;
                if (!line.activeSelf) continue;

                line.SetActive(false);
                Object.Destroy(line);
            }

            var height = _widgets.ControlHeight;
            foreach (var word in Words)
            {
                var line = _widgets.CreateRow(_lines);
                var label = _widgets.CreateLabel(line, word);
                _widgets.SetLayout(label.gameObject, preferredWidth: 0f, flexibleWidth: 1f, height: height);
                var remove = _widgets.CreateButton(line, "Remove", () => Remove(word));
                _widgets.SetLayout(remove.gameObject, ButtonWidth, ButtonWidth, height: height);
            }

            SetHeight(Height());
        }

        private float Height()
        {
            return _widgets.ControlHeight + Words.Count * (_widgets.ControlHeight + LineSpacing);
        }

        private void Add()
        {
            if (_field == null) return;

            var word = _field.Input.text.Trim();
            if (word.Length == 0) return;

            _field.Input.SetTextWithoutNotify("");
            Commit(new SampleWordList(Words.Concat(new[] { word })));
        }

        private void Remove(string word)
        {
            Commit(new SampleWordList(Words.Where(x => x != word)));
        }
    }

    /// <summary>
    /// Creates a widget, then fails, so the row must drop the widget and show the value read-only.
    /// </summary>
    internal sealed class SampleBrokenBuildEditor : ConfigurationGuiCustomEditor
    {
        protected override float Build(RectTransform slot, ConfigurationGuiWidgets widgets)
        {
            widgets.CreateLabel(slot, "Half-built");
            throw new InvalidOperationException("The smoke sample editor fails in Build on purpose.");
        }

        protected override void Refresh()
        {
        }
    }
}
