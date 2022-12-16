using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using TypeConverter = BepInEx.Configuration.TypeConverter;

namespace ModUtils
{
    public class Configuration
    {
        private const int DefaultOrder = 4096;

        private readonly ConfigFile _config;
        private readonly L10N _localization;
        private Logger _logger;

        static Configuration()
        {
            if (!TomlTypeConverter.CanConvert(typeof(StringList)))
                TomlTypeConverter.AddConverter(typeof(StringList), new TypeConverter
                {
                    ConvertToObject = (str, type) => string.IsNullOrEmpty(str)
                        ? new StringList()
                        : new StringList(Csv.ParseLine(str)),
                    ConvertToString = (obj, type) =>
                    {
                        var list = (StringList)obj;
                        return string.Join(", ", list.Select(Csv.Escape));
                    }
                });

            if (!TomlTypeConverter.CanConvert(typeof(KeyboardShortcut)))
                // Although the same processing exists in the static constructor of KeyboardShortcut,
                // if BepInEx.ConfigurationManager is not installed, an error will occur that
                // KeyboardShortcut is not a conversion target.
                TomlTypeConverter.AddConverter(typeof(KeyboardShortcut), new TypeConverter
                {
                    ConvertToObject = (str, type) => KeyboardShortcut.Deserialize(str),
                    ConvertToString = (obj, type) => ((KeyboardShortcut)obj).Serialize()
                });
        }

        public Configuration(ConfigFile config, L10N localization)
        {
            _config = config;
            _localization = localization;
        }

        private string Section { get; set; } = "general";

        private int Order { get; set; } = DefaultOrder;

        private void LogSection(string section)
        {
            _logger?.Debug($"[CONFIG] === {GetSection(section)} / [{section}]");
        }

        private void LogConfigEntry<T>(ConfigEntry<T> entry,
            ConfigurationManagerAttributes attributes)
        {
            _logger?.Debug($"[CONFIG] ==== {attributes.DispName} / [{entry.Definition.Key}]");
            _logger?.Debug($"[CONFIG] {entry.Description.Description}");
            _logger?.Debug("[CONFIG] ");

            var type = typeof(T);
            var defaultValue = entry.DefaultValue;

            if (attributes.ObjToStr != null)
                _logger?.Debug($"[CONFIG] - Default value: {attributes.ObjToStr(defaultValue)}");
            else if (TomlTypeConverter.CanConvert(type))
                _logger?.Debug(
                    $"[CONFIG] - Default value: {TomlTypeConverter.ConvertToString(defaultValue, type)}");
            else
                _logger?.Debug($"[CONFIG] - Default value: {defaultValue}");

            var acceptableValues = entry.Description.AcceptableValues;
            if (acceptableValues != null)
            {
                foreach (var line in acceptableValues.ToDescriptionString().Split('\n'))
                    _logger?.Debug($"[CONFIG] - {line}");
            }
            else if (type.IsEnum)
            {
                var values = Enum.GetValues(type).OfType<T>().Select(x => Enum.GetName(type, x))
                                 .ToList();
                _logger?.Debug($"[CONFIG] - Acceptable values: {string.Join(", ", values)}");
                if (type.GetCustomAttributes(typeof(FlagsAttribute), false).Any())
                {
                    var filtered = values.Where(x =>
                        !string.Equals(x, "none", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(x, "all", StringComparison.OrdinalIgnoreCase)).Take(2);
                    _logger?.Debug(
                        $"[CONFIG] - Multiple values can be set at the same time by separating them with , (e.g. {string.Join(", ", filtered)})");
                }
            }

            _logger?.Debug("[CONFIG] ");
        }

        public void SetDebugLogger(Logger logger)
        {
            _logger = logger;
        }

        public void ChangeSection(string section, int initialOrder = DefaultOrder)
        {
            Section = section;
            Order = initialOrder;
            if (!(_logger is null)) LogSection(Section);
        }

        private ConfigEntry<T> Bind<T>(string section, int order, string key, T defaultValue,
            AcceptableValueBase acceptableValue = null,
            Action<ConfigurationManagerAttributes> initializer = null)
        {
            var attributes = new ConfigurationManagerAttributes
            {
                Category = GetSection(section),
                Order = order,
                DispName = GetName(section, key),
                CustomDrawer = ConfigurationCustomDrawer.Get(typeof(T), acceptableValue)
            };
            initializer?.Invoke(attributes);

            var description = string.IsNullOrEmpty(attributes.Description)
                ? GetDescription(section, key)
                : attributes.Description;

            var configEntry = _config.Bind(section, key, defaultValue,
                new ConfigDescription(description, acceptableValue, attributes));

            if (!(_logger is null)) LogConfigEntry(configEntry, attributes);
            return configEntry;
        }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue,
            AcceptableValueBase acceptableValue = null,
            Action<ConfigurationManagerAttributes> initializer = null)
        {
            return Bind(section, Order--, key, defaultValue, acceptableValue, initializer);
        }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue,
            (T, T) acceptableValue,
            Action<ConfigurationManagerAttributes> initializer = null) where T : IComparable
        {
            var (minValue, maxValue) = acceptableValue;
            return Bind(section, key, defaultValue, new AcceptableValueRange<T>(minValue, maxValue),
                initializer);
        }

        public ConfigEntry<T> Bind<T>(string key, T defaultValue,
            AcceptableValueBase acceptableValue = null,
            Action<ConfigurationManagerAttributes> initializer = null)
        {
            return Bind(Section, key, defaultValue, acceptableValue, initializer);
        }

        public ConfigEntry<T> Bind<T>(string key, T defaultValue, (T, T) acceptableValue,
            Action<ConfigurationManagerAttributes> initializer = null) where T : IComparable
        {
            return Bind(Section, key, defaultValue, acceptableValue, initializer);
        }

        private string GetSection(string section)
        {
            return _localization.Translate($"@config_{section}_section");
        }

        private string GetName(string section, string key)
        {
            return _localization.Translate($"@config_{section}_{key}_name");
        }

        private string GetDescription(string section, string key)
        {
            return _localization.Translate($"@config_{section}_{key}_description");
        }
    }

    public class StringList : ICollection<string>
    {
        private readonly HashSet<string> _values;

        public StringList()
        {
            _values = new HashSet<string>();
        }

        public StringList(IEnumerable<string> collection)
        {
            _values = new HashSet<string>(collection);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerator<string> GetEnumerator()
        {
            return _values.GetEnumerator();
        }

        public void Add(string item)
        {
            _values.Add(item);
        }

        public void Clear()
        {
            _values.Clear();
        }

        public bool Contains(string item)
        {
            return _values.Contains(item);
        }

        public void CopyTo(string[] array, int arrayIndex)
        {
            _values.CopyTo(array, arrayIndex);
        }

        public bool Remove(string item)
        {
            return _values.Remove(item);
        }

        public int Count => _values.Count;

        public bool IsReadOnly => false;

        public bool TryAdd(string item)
        {
            return _values.Add(item);
        }
    }

    public class LocalizedDescriptionAttribute : DescriptionAttribute
    {
        private readonly string _prefix;
        private readonly string _key;

        public LocalizedDescriptionAttribute(string prefix, string key) : base(key)
        {
            _prefix = prefix;
            _key = key;
        }

        public LocalizedDescriptionAttribute(string key) : this("", key)
        {
        }

        public override string Description => L10N.Translate(_prefix, _key);
    }

    public class AcceptableValueEnum<T> : AcceptableValueBase where T : Enum
    {
        private readonly bool _isFlags;
        private readonly IList<T> _values;

        public AcceptableValueEnum(params T[] values) : base(typeof(T))
        {
            _isFlags = ValueType.GetCustomAttributes(typeof(FlagsAttribute), false).Any();
            _values = MakeValues(ValueType, values, _isFlags);
        }

        private static IList<T> MakeValues(Type type, IReadOnlyCollection<T> values, bool isFlags)
        {
            var enumerable =
                new List<T>(values.Count == 0 ? Enum.GetValues(type).OfType<T>() : values);
            if (!isFlags) return enumerable;

            var set = new HashSet<long>();
            foreach (var value in enumerable.Select(@enum => Convert.ToInt64(@enum)))
            {
                foreach (var other in set.ToArray())
                    set.Add(other | value);
                set.Add(value);
            }

            return set.Select(x => Enum.ToObject(type, x)).Cast<T>().ToList();
        }

        public override object Clamp(object value)
        {
            return IsValid(value) ? value : _values[0];
        }

        public override bool IsValid(object value)
        {
            if (value is T @enum) return _values.Contains(@enum);
            if (!(value is IConvertible)) return false;

            var @long = Convert.ToInt64(value);
            return _values.Any(x => Convert.ToInt64(x) == @long);
        }

        public override string ToDescriptionString()
        {
            var buffer = new StringBuilder();

            var type = typeof(T);
            var values =
                (from x in _values where Enum.IsDefined(type, x) select Enum.GetName(type, x))
                .ToList();
            buffer.Append("# Acceptable values: ").Append(string.Join(", ", values));

            if (!_isFlags) return buffer.ToString();

            var list = values.Where(x =>
                                 !string.Equals(x, "none", StringComparison.OrdinalIgnoreCase) &&
                                 !string.Equals(x, "all", StringComparison.OrdinalIgnoreCase))
                             .Take(2).ToList();
            if (list.Count == 2)
                buffer.Append('\n')
                      .Append(
                          "# Multiple values can be set at the same time by separating them with , (e.g. ")
                      .Append(string.Join(", ", list)).Append(")");

            return buffer.ToString();
        }
    }
}