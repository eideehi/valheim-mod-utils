using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LitJson;

namespace ModUtils
{
    public class L10N
    {
        private static readonly Regex InternalNamePattern;
        private static readonly Regex WordPattern;
        private static readonly Localization Localization;

        private readonly string _prefix;

        static L10N()
        {
            InternalNamePattern = new Regex(@"^(\$|@)(\w|\d|[^\s(){}[\]+\-!?/\\&%,.:=<>])+$",
                RegexOptions.Compiled);
            WordPattern = new Regex(@"(\$|@)((?:\w|\d|[^\s(){}[\]+\-!?/\\&%,.:=<>])+)",
                RegexOptions.Compiled);
            Localization = Localization.instance;
        }

        public L10N(string prefix)
        {
            _prefix = prefix;
        }

        private static string InvokeTranslate(string word)
        {
            return Reflections.InvokeMethod<string>(Localization, "Translate", word);
        }

        private static string InvokeInsertWords(string text, string[] words)
        {
            return Reflections.InvokeMethod<string>(Localization, "InsertWords", text, words);
        }

        private static void InvokeAddWord(string key, string word)
        {
            Reflections.InvokeMethod(Localization, "AddWord", key, word);
        }

        internal static string GetTranslationKey(string prefix, string internalName)
        {
            if (string.IsNullOrEmpty(internalName)) return "";

            switch (internalName[0])
            {
                case '$':
                    return internalName.Substring(1);
                case '@':
                    return $"{prefix}_{internalName.Substring(1)}";
                default:
                    return internalName;
            }
        }

        internal static string Translate(string prefix, string word)
        {
            return InvokeTranslate(GetTranslationKey(prefix, word));
        }

        public static bool IsInternalName(string text)
        {
            return !string.IsNullOrEmpty(text) && InternalNamePattern.IsMatch(text);
        }

        private string GetTranslationKey(string internalName)
        {
            return GetTranslationKey(_prefix, internalName);
        }

        public void AddWord(string key, string word)
        {
            InvokeAddWord(GetTranslationKey(key), word);
        }

        public string Translate(string word)
        {
            return InvokeTranslate(GetTranslationKey(word));
        }

        [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
        public string TranslateInternalName(string internalName)
        {
            return !IsInternalName(internalName)
                ? internalName
                : InvokeTranslate(GetTranslationKey(internalName));
        }

        [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
        public string Localize(string text)
        {
            var sb = new StringBuilder();
            var offset = 0;
            foreach (Match match in WordPattern.Matches(text))
            {
                var groups = match.Groups;
                var word = groups[1].Value == "@"
                    ? $"{_prefix}_{groups[2].Value}"
                    : groups[2].Value;

                sb.Append(text.Substring(offset, groups[0].Index - offset));
                sb.Append(InvokeTranslate(word));
                offset = groups[0].Index + groups[0].Value.Length;
            }

            return sb.ToString();
        }

        public string Localize(string text, params object[] args)
        {
            return InvokeInsertWords(Localize(text),
                Array.ConvertAll(args,
                    arg => arg is string s ? TranslateInternalName(s) : arg.ToString()));
        }

        public string LocalizeTextOnly(string text, params object[] args)
        {
            return InvokeInsertWords(Localize(text),
                Array.ConvertAll(args, arg => arg as string ?? arg.ToString()));
        }
    }

    public class TranslationsLoader
    {
        private static readonly Localization Localization;
        private static readonly string DefaultLanguage;
        private static readonly string JsonFilePattern;

        private readonly L10N _localization;
        private Dictionary<string, TranslationsFile> _cache;
        private Logger _logger;

        static TranslationsLoader()
        {
            Localization = Localization.instance;
            DefaultLanguage = "English";
            JsonFilePattern = "*.json";
        }

        public TranslationsLoader(L10N localization)
        {
            _localization = localization;
        }

        public void SetDebugLogger(Logger logger)
        {
            _logger = logger;
        }

        private bool LoadAllFile(string directory, string filePattern, string language,
            Func<string, string, bool> loading)
        {
            _logger?.Debug(
                $"Load translation files for {language} from directory: [directory: {directory}, file pattern: {filePattern}]");
            return Directory.EnumerateFiles(directory, filePattern, SearchOption.AllDirectories)
                            .Count(path => loading.Invoke(path, language)) > 0;
        }

        public void LoadTranslations(string languagesDir, string language)
        {
            _cache = new Dictionary<string, TranslationsFile>();

            if (!Directory.Exists(languagesDir))
            {
                _logger?.Error($"Directory does not exist: {languagesDir}");
                return;
            }

            if (language != DefaultLanguage)
                if (!LoadAllFile(languagesDir, JsonFilePattern, DefaultLanguage, ReadJsonFile))
                    _logger?.Warning(
                        $"Directory does not contain a translation file for the default language: {languagesDir}");

            if (!LoadAllFile(languagesDir, JsonFilePattern, language, ReadJsonFile))
                _logger?.Warning(
                    $"Directory does not contain a translation file for the {language}: {languagesDir}");

            _cache = null;
        }

        public void LoadTranslations(string languagesDir)
        {
            LoadTranslations(languagesDir, Localization.GetSelectedLanguage());
        }

        private bool ReadJsonFile(string path, string language)
        {
            if (!_cache.TryGetValue(path, out var json))
                try
                {
                    json = Json.Parse<TranslationsFile>(File.ReadAllText(path));
                    _cache.Add(path, json);
                }
                catch (Exception e)
                {
                    _logger?.Error($"Failed to read Json file\n{e}");
                    _cache.Add(path, new TranslationsFile());
                    return false;
                }

            if (!string.Equals(json.language, language, StringComparison.OrdinalIgnoreCase))
                return false;

            _logger?.Debug($"Load translations: {path}");
            foreach (var translation in json.translations)
                _localization.AddWord(translation.Key, translation.Value);

            return true;
        }
    }

    [Serializable]
    public struct TranslationsFile
    {
        public string language;

        [SuppressMessage("ReSharper", "UnassignedField.Global")]
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        public Dictionary<string, string> translations;
    }
}