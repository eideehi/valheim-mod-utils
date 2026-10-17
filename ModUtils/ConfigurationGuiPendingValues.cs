using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;

namespace ModUtils
{
    /// <summary>
    /// Values edited in an open mod panel that are not written to their entries yet. Nothing reaches an
    /// entry before <see cref="Apply"/>; dropping the panel drops the values.
    /// </summary>
    internal sealed class ConfigurationGuiPendingValues
    {
        private readonly Dictionary<ConfigEntryBase, object> _values = new Dictionary<ConfigEntryBase, object>();

        /// <summary>
        /// Called after every change to the pending values.
        /// </summary>
        internal Action Changed;

        internal ConfigurationGuiPendingValues(string pluginName)
        {
            PluginName = pluginName;
        }

        /// <summary>
        /// Name of the mod whose panel owns the values, for log messages.
        /// </summary>
        internal string PluginName { get; }

        internal bool HasPending => _values.Count > 0;

        /// <summary>
        /// The value an editor shows and edits: the pending value, else the entry's.
        /// </summary>
        internal object Get(ConfigEntryBase entry)
        {
            return _values.TryGetValue(entry, out var value) ? value : entry.BoxedValue;
        }

        /// <summary>
        /// Keeps <paramref name="value"/>, clamped by the entry's acceptable values as the entry would clamp
        /// it, until Apply. A value equal to the entry's current value leaves nothing pending.
        /// </summary>
        internal void Stage(ConfigEntryBase entry, object value)
        {
            var acceptableValues = entry.Description?.AcceptableValues;
            if (acceptableValues != null)
                value = acceptableValues.Clamp(value);

            if (IsSame(entry, entry.BoxedValue, value))
                _values.Remove(entry);
            else
                _values[entry] = value;

            Changed?.Invoke();
        }

        /// <summary>
        /// Writes every pending value to its entry. A completed write drops the value, even when a
        /// SettingChanged handler corrected it. A failed write is logged and the others are still written;
        /// its value stays pending only while its entry does not hold it.
        /// </summary>
        internal void Apply()
        {
            foreach (var pair in _values.ToList())
            {
                try
                {
                    pair.Key.BoxedValue = pair.Value;
                    _values.Remove(pair.Key);
                }
                catch (Exception e)
                {
                    var definition = pair.Key.Definition;
                    UnityEngine.Debug.LogError(
                        $"[ModUtils] Failed to set [{definition.Section}:{definition.Key}] of {PluginName}: {e}");
                    if (IsSame(pair.Key, pair.Key.BoxedValue, pair.Value))
                        _values.Remove(pair.Key);
                }
            }

            Changed?.Invoke();
        }

        internal void Discard()
        {
            _values.Clear();
            Changed?.Invoke();
        }

        /// <summary>
        /// Compares two values of an entry by the text written to the config file, so separate instances
        /// with the same content (such as two lists) count as equal. Types without a converter, and values
        /// the converter rejects, are compared with Equals.
        /// </summary>
        private static bool IsSame(ConfigEntryBase entry, object a, object b)
        {
            var type = entry.SettingType;
            if (TomlTypeConverter.CanConvert(type))
            {
                try
                {
                    return string.Equals(TomlTypeConverter.ConvertToString(a, type),
                        TomlTypeConverter.ConvertToString(b, type), StringComparison.Ordinal);
                }
                catch (Exception)
                {
                    // Falls through to Equals.
                }
            }

            return Equals(a, b);
        }
    }
}
