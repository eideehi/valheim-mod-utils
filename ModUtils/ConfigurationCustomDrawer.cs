using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ModUtils
{
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    public static class ConfigurationCustomDrawer
    {
        private const string L10NPrefix = "mod_utils";
        private const string EnabledKey = "@config_button_enabled";
        private const string DisabledKey = "@config_button_disabled";
        private const string AddKey = "@config_button_add";
        private const string RemoveKey = "@config_button_remove";

        private static readonly Dictionary<IsMatchConfig, CustomDrawerSupplier> CustomDrawers;

        static ConfigurationCustomDrawer()
        {
            var addWord =
                MethodInvoker.GetHandler(AccessTools.Method(typeof(Localization), "AddWord"));
            var l10N = Localization.instance;
            addWord.Invoke(l10N, L10N.GetTranslationKey(L10NPrefix, EnabledKey), "Enabled");
            addWord.Invoke(l10N, L10N.GetTranslationKey(L10NPrefix, DisabledKey), "Disabled");
            addWord.Invoke(l10N, L10N.GetTranslationKey(L10NPrefix, AddKey), "+");
            addWord.Invoke(l10N, L10N.GetTranslationKey(L10NPrefix, RemoveKey), "x");

            CustomDrawers = new Dictionary<IsMatchConfig, CustomDrawerSupplier>
            {
                { IsBool, () => Bool },
                { IsFloatWithRange, () => FloatSlider },
                { IsStringList, StringList },
                { IsFlagsEnum, () => Flags }
            };
        }

        private static bool IsBool(Type type, AcceptableValueBase acceptableValue)
        {
            return type == typeof(bool);
        }

        private static bool IsFloatWithRange(Type type, AcceptableValueBase acceptableValue)
        {
            return type == typeof(float) && acceptableValue is AcceptableValueRange<float>;
        }

        private static bool IsStringList(Type type, AcceptableValueBase acceptableValue)
        {
            return type == typeof(float) && acceptableValue is AcceptableValueRange<float>;
        }

        private static bool IsFlagsEnum(Type type, AcceptableValueBase acceptableValue)
        {
            return type.IsEnum && type.GetCustomAttributes(typeof(FlagsAttribute), false).Any();
        }

        public static void Bool(ConfigEntryBase entry)
        {
            var @bool = (bool)entry.BoxedValue;
            var text = L10N.Translate(L10NPrefix, @bool ? EnabledKey : DisabledKey);
            var result = GUILayout.Toggle(@bool, text, GUILayout.ExpandWidth(true));
            if (result != @bool)
                entry.BoxedValue = result;
        }

        public static void FloatSlider(ConfigEntryBase entry)
        {
            var range = (AcceptableValueRange<float>)entry.Description.AcceptableValues;
            var value = (float)entry.BoxedValue;
            var min = range.MinValue;
            var max = range.MaxValue;

            var result = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
            result = Mathf.Floor(result * 100f) / 100f;
            if (Math.Abs(result - value) > Mathf.Abs(max - min) / 1000)
                entry.BoxedValue = result;

            var stringValue = value.ToString("0.00", CultureInfo.InvariantCulture);
            var stringResult = GUILayout.TextField(stringValue, GUILayout.Width(50));
            if (stringResult == stringValue) return;

            try
            {
                result = (float)Convert.ToDouble(stringResult, CultureInfo.InvariantCulture);
                var newValue = Convert.ChangeType(range.Clamp(result), entry.SettingType,
                    CultureInfo.InvariantCulture);
                entry.BoxedValue = newValue;
            }
            catch (FormatException)
            {
                // Ignore user typing in bad data
            }
        }

        private static Action<ConfigEntryBase> StringList()
        {
            var inputText = "";
            return entry =>
            {
                var guiWidth = Mathf.Min(Screen.width, 650);
                var maxWidth = guiWidth - Mathf.RoundToInt(guiWidth / 2.5f) - 115;
                var addButtonText = L10N.Translate(L10NPrefix, AddKey);
                var removeButtonText = L10N.Translate(L10NPrefix, RemoveKey);

                var list = new StringList((StringList)entry.BoxedValue);

                GUILayout.BeginVertical(GUILayout.MaxWidth(maxWidth));

                GUILayout.BeginHorizontal();

                inputText = GUILayout.TextField(inputText, GUILayout.ExpandWidth(true));

                var add = GUILayout.Button(addButtonText, GUILayout.ExpandWidth(false));
                if (add && !string.IsNullOrEmpty(inputText))
                {
                    if (list.TryAdd(inputText))
                        entry.BoxedValue = list;

                    inputText = "";
                }

                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                var lineWidth = 0.0;
                foreach (var value in list.ToList())
                {
                    var elementWidth =
                        Mathf.FloorToInt(GUI.skin.label.CalcSize(new GUIContent(value)).x) +
                        Mathf.FloorToInt(GUI.skin.button.CalcSize(new GUIContent(removeButtonText))
                                            .x);

                    lineWidth += elementWidth;
                    if (lineWidth > maxWidth)
                    {
                        GUILayout.EndHorizontal();
                        lineWidth = elementWidth;
                        GUILayout.BeginHorizontal();
                    }

                    GUILayout.Label(value, GUILayout.ExpandWidth(false));
                    if (GUILayout.Button(removeButtonText, GUILayout.ExpandWidth(false)))
                        if (list.Remove(value))
                            entry.BoxedValue = list;
                }

                GUILayout.EndHorizontal();

                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
            };
        }

        public static void Flags(ConfigEntryBase entry)
        {
            var guiWidth = Mathf.Min(Screen.width, 650);
            var maxWidth = guiWidth - Mathf.RoundToInt(guiWidth / 2.5f) - 115;

            var flagsType = entry.SettingType;
            var currentValue = Convert.ToInt64(entry.BoxedValue);
            var validator = entry.Description.AcceptableValues;

            GUILayout.BeginVertical(GUILayout.MaxWidth(maxWidth));

            var lineWidth = 0;
            GUILayout.BeginHorizontal();
            foreach (var @enum in Enum.GetValues(flagsType))
            {
                if (validator != null && !validator.IsValid(@enum)) continue;

                var value = Convert.ToInt64(@enum);
                if (value == 0) continue;

                var label = GetFlagsLabel(flagsType, @enum);

                var width =
                    Mathf.FloorToInt(GUI.skin.toggle.CalcSize(new GUIContent(label + "_")).x);
                lineWidth += width;
                if (lineWidth > maxWidth)
                {
                    GUILayout.EndHorizontal();
                    lineWidth = width;
                    GUILayout.BeginHorizontal();
                }

                GUI.changed = false;
                var @checked = GUILayout.Toggle((currentValue & value) == value, label,
                    GUILayout.ExpandWidth(false));
                if (!GUI.changed) continue;

                var newValue = @checked ? currentValue | value : currentValue & ~value;
                entry.BoxedValue = Enum.ToObject(flagsType, newValue);
            }

            GUILayout.EndHorizontal();
            GUI.changed = false;

            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();

            string GetFlagsLabel(Type type, object @object)
            {
                var member = type.GetMember(Enum.GetName(type, @object) ?? "").FirstOrDefault();
                var attribute = member?.GetCustomAttributes(typeof(DescriptionAttribute), false)
                                      .OfType<DescriptionAttribute>().FirstOrDefault();
                return attribute?.Description ?? @object.ToString();
            }
        }

        public static Action<ConfigEntryBase> MultiSelect<T>(
            Func<IEnumerable<T>> allElementSupplier, Func<T, string> labelGenerator)
            where T : IComparable<T>
        {
            return entry =>
            {
                var guiWidth = Mathf.Min(Screen.width, 650);
                var maxWidth = guiWidth - Mathf.RoundToInt(guiWidth / 2.5f) - 115;

                GUILayout.BeginVertical(GUILayout.MaxWidth(maxWidth));

                var type = entry.SettingType;
                var constructor = type.GetConstructor(new[] { typeof(IEnumerable<T>) });
                if (constructor is null)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(
                        $"{type} does not define a constructor that takes IEnumerable<{typeof(T)}> as an argument.");
                    GUILayout.EndHorizontal();
                    GUI.changed = false;

                    GUILayout.EndVertical();
                    GUILayout.FlexibleSpace();
                    return;
                }

                var elements = (ICollection<T>)constructor.Invoke(new[] { entry.BoxedValue });
                var validator = entry.Description.AcceptableValues;

                var lineWidth = 0;
                GUILayout.BeginHorizontal();
                foreach (var element in allElementSupplier.Invoke())
                {
                    if (validator != null && !validator.IsValid(element)) continue;

                    var label = labelGenerator(element);

                    var width =
                        Mathf.FloorToInt(GUI.skin.toggle.CalcSize(new GUIContent(label + "_")).x);
                    lineWidth += width;
                    if (lineWidth > maxWidth)
                    {
                        GUILayout.EndHorizontal();
                        lineWidth = width;
                        GUILayout.BeginHorizontal();
                    }

                    GUI.changed = false;
                    var @checked = GUILayout.Toggle(elements.Contains(element), label,
                        GUILayout.ExpandWidth(false));
                    if (!GUI.changed) continue;

                    if (@checked)
                        elements.Add(element);
                    else
                        elements.Remove(element);

                    entry.BoxedValue = elements;
                }

                GUILayout.EndHorizontal();
                GUI.changed = false;

                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
            };
        }

        public static void Register(IsMatchConfig matcher, CustomDrawerSupplier supplier)
        {
            CustomDrawers[matcher] = supplier;
        }

        public static Action<ConfigEntryBase> Get(Type type, AcceptableValueBase acceptableValue)
        {
            return CustomDrawers.Where(x => x.Key.Invoke(type, acceptableValue))
                                .Select(x => x.Value.Invoke())
                                .FirstOrDefault();
        }

        public delegate bool IsMatchConfig(Type type, AcceptableValueBase acceptableValue);

        public delegate Action<ConfigEntryBase> CustomDrawerSupplier();
    }
}