using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace ModUtils
{
    public class L10N
    {
        private static readonly Regex InternalNamePattern;
        private static readonly Dictionary<string, string> TranslationCache;
        private static readonly Dictionary<string, string> RuntimeWords;
        private static readonly Regex WordPattern;

        private const string HarmonyId = "net.eideehi.modutils.localization";

        private static bool _patchesApplied;
        private static bool _initializePatchApplied;
        private static bool _setLanguagePatchApplied;
        private static bool _initializeMethodWarningLogged;
        private static bool _setLanguageMethodWarningLogged;
        private static string _currentLanguage;
        private static readonly List<TranslationSource> _translationSources = new List<TranslationSource>();

        /// <summary>Fired when the language changes. The argument is the new language name.</summary>
        public static event Action<string> LanguageChanged;

        private readonly string _prefix;

        private struct TranslationSource
        {
            public L10N Localization;
            public string Directory;
        }

        static L10N()
        {
            InternalNamePattern = new Regex(@"^(\$|@)(\w|\d|[^\s(){}[\]+\-!?/\\&%,.:=<>])+$",
                RegexOptions.Compiled);
            TranslationCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            RuntimeWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            WordPattern = new Regex(@"(\$|@)((?:\w|\d|[^\s(){}[\]+\-!?/\\&%,.:=<>])+)",
                RegexOptions.Compiled);
        }

        public L10N(string prefix)
        {
            _prefix = prefix;
        }

        internal static void EnsurePatched()
        {
            if (_patchesApplied) return;

            try
            {
                var harmony = new Harmony(HarmonyId);

                if (!_initializePatchApplied)
                {
                    var initMethod = AccessTools.Method(typeof(global::Localization), "Initialize");
                    if (initMethod == null)
                    {
                        if (!_initializeMethodWarningLogged)
                        {
                            UnityEngine.Debug.LogWarning(
                                "[ModUtils] Localization.Initialize method not found. Auto-reload for initialization will not work until it becomes available.");
                            _initializeMethodWarningLogged = true;
                        }
                    }
                    else
                    {
                        harmony.Patch(initMethod,
                            postfix: new HarmonyMethod(typeof(L10N), nameof(OnLocalizationInitialized)));
                        _initializePatchApplied = true;
                    }
                }

                if (!_setLanguagePatchApplied)
                {
                    var setLangMethod = AccessTools.Method(typeof(global::Localization),
                        nameof(global::Localization.SetLanguage));
                    if (setLangMethod == null)
                    {
                        if (!_setLanguageMethodWarningLogged)
                        {
                            UnityEngine.Debug.LogWarning(
                                "[ModUtils] Localization.SetLanguage method not found. Auto-reload for language change will not work until it becomes available.");
                            _setLanguageMethodWarningLogged = true;
                        }
                    }
                    else
                    {
                        harmony.Patch(setLangMethod,
                            postfix: new HarmonyMethod(typeof(L10N), nameof(OnLanguageSet)));
                        _setLanguagePatchApplied = true;
                    }
                }

                _patchesApplied = _initializePatchApplied && _setLanguagePatchApplied;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning(
                    $"[ModUtils] Failed to patch Localization lifecycle methods. Auto-reload will stay disabled. {e}");
            }
        }

        private static void OnLocalizationInitialized()
        {
            try
            {
                var language = global::Localization.instance?.GetSelectedLanguage();
                if (!string.IsNullOrEmpty(language))
                    HandleLanguageChange(language);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[ModUtils] OnLocalizationInitialized error: {e}");
            }
        }

        private static void OnLanguageSet(string language)
        {
            try
            {
                if (!string.IsNullOrEmpty(language))
                    HandleLanguageChange(language);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[ModUtils] OnLanguageSet error: {e}");
            }
        }

        private static void HandleLanguageChange(string language)
        {
            ApplyLanguageChange(language, true);
        }

        public static string SyncCurrentLanguage()
        {
            EnsurePatched();

            try
            {
                var instance = global::Localization.instance;
                if (instance == null) return null;

                var language = instance.GetSelectedLanguage();
                if (string.IsNullOrEmpty(language)) return null;

                // Sync internal caches to the current game language without replaying LanguageChanged callbacks.
                ApplyLanguageChange(language, false);
                return language;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[ModUtils] SyncCurrentLanguage failed: {e}");
                return null;
            }
        }

        private static void ApplyLanguageChange(string language, bool fireLanguageChanged)
        {
            if (string.IsNullOrEmpty(language) ||
                string.Equals(language, _currentLanguage, StringComparison.OrdinalIgnoreCase))
                return;

            ReloadTranslations(language);
            _currentLanguage = language;
            RefreshConfigurationMetadata();
            RefreshDefaultDrawerTranslations();

            if (fireLanguageChanged)
                FireLanguageChanged(language);
        }

        private static void RefreshConfigurationMetadata()
        {
            try
            {
                Configuration.RefreshAllLocalizedMetadata();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(
                    $"[ModUtils] Failed to refresh localized config metadata: {e}");
            }
        }

        private static void RefreshDefaultDrawerTranslations()
        {
            try
            {
                ConfigurationCustomDrawer.RefreshDefaultTranslations();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(
                    $"[ModUtils] Failed to refresh custom drawer translations: {e}");
            }
        }

        private static void ReloadTranslations(string language)
        {
            // Clear file-sourced entries from previous language (runtime AddWord entries are preserved in RuntimeWords)
            TranslationCache.Clear();

            foreach (var source in _translationSources)
            {
                try
                {
                    new TranslationsLoader(source.Localization)
                        .LoadTranslations(source.Directory, language);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError(
                        $"[ModUtils] Failed to reload translations from {source.Directory}: {e}");
                }
            }

            // Re-apply runtime AddWord entries to both caches (Localization.instance takes priority in InvokeTranslate)
            var localization = TryGetLocalization();
            foreach (var kvp in RuntimeWords)
            {
                TranslationCache[kvp.Key] = kvp.Value;
                if (localization != null)
                    Reflections.InvokeMethod(localization, "AddWord", kvp.Key, kvp.Value);
            }
        }

        private static void FireLanguageChanged(string language)
        {
            var handler = LanguageChanged;
            if (handler == null) return;

            foreach (var d in handler.GetInvocationList())
            {
                try
                {
                    ((Action<string>)d).Invoke(language);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError(
                        $"[ModUtils] LanguageChanged handler error: {e}");
                }
            }
        }

        public void AddTranslationDirectory(string directory)
        {
            EnsurePatched();

            _translationSources.Add(new TranslationSource
            {
                Localization = this,
                Directory = directory
            });
            new TranslationsLoader(this)
                .LoadTranslations(directory, DetectCurrentLanguage());

            // Re-apply runtime AddWord entries so file-sourced entries do not override them
            var localization = TryGetLocalization();
            foreach (var kvp in RuntimeWords)
            {
                TranslationCache[kvp.Key] = kvp.Value;
                if (localization != null)
                    Reflections.InvokeMethod(localization, "AddWord", kvp.Key, kvp.Value);
            }
        }

        private static string DetectCurrentLanguage()
        {
            if (_currentLanguage != null)
                return _currentLanguage;

            try
            {
                var language = global::Localization.instance?.GetSelectedLanguage();
                if (!string.IsNullOrEmpty(language))
                    return language;
            }
            catch (Exception)
            {
                // instance not yet initialized
            }

            return "English";
        }

        internal static global::Localization TryGetLocalization()
        {
            try
            {
                var instance = global::Localization.instance;
                if (instance == null) return null;
                return string.IsNullOrEmpty(instance.GetSelectedLanguage()) ? null : instance;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string InvokeTranslate(string word)
        {
            var localization = TryGetLocalization();
            TranslationCache.TryGetValue(word, out var translated);
            if (localization == null)
                return translated ?? word;

            var localized = Reflections.InvokeMethod<string>(localization, "Translate", word);
            if (localized == null || IsMissingTranslation(word, localized))
                return translated ?? word;
            return localized;
        }

        private static bool IsMissingTranslation(string word, string localized)
        {
            return string.Equals(localized, word, StringComparison.Ordinal) ||
                   string.Equals(localized, $"[{word}]", StringComparison.Ordinal);
        }

        private static string InvokeInsertWordsFallback(string text, IReadOnlyList<string> words)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var result = text;
            for (var i = 0; i < words.Count; i++)
                result = result.Replace($"${i + 1}", words[i] ?? "");
            return result;
        }

        private static string InvokeInsertWords(string text, string[] words)
        {
            var localization = TryGetLocalization();
            return localization != null
                ? Reflections.InvokeMethod<string>(localization, "InsertWords", text, words)
                : InvokeInsertWordsFallback(text, words);
        }

        private static void InvokeAddWord(string key, string word)
        {
            TranslationCache[key] = word;

            var localization = TryGetLocalization();
            if (localization != null)
                Reflections.InvokeMethod(localization, "AddWord", key, word);
        }

        private static void InvokeAddRuntimeWord(string key, string word)
        {
            RuntimeWords[key] = word;
            TranslationCache[key] = word;

            var localization = TryGetLocalization();
            if (localization != null)
                Reflections.InvokeMethod(localization, "AddWord", key, word);
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
            InvokeAddRuntimeWord(GetTranslationKey(key), word);
        }

        internal void AddFileWord(string key, string word)
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
        private static readonly string DefaultLanguage;
        private static readonly string JsonFilePattern;

        private readonly L10N _localization;
        private Dictionary<string, TranslationsFile> _cache;
        private Logger _logger;

        static TranslationsLoader()
        {
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
            string language;
            try
            {
                language = global::Localization.instance?.GetSelectedLanguage();
            }
            catch (Exception)
            {
                language = null;
            }

            LoadTranslations(languagesDir, string.IsNullOrEmpty(language) ? DefaultLanguage : language);
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
                _localization.AddFileWord(translation.Key, translation.Value);

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
