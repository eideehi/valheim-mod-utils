using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ModUtils.SmokeTest
{
    [BepInPlugin("net.eideehi.modutils.smoke", "ModUtils Smoke Test", "0.1.0")]
    public sealed class SmokePlugin : BaseUnityPlugin
    {
        private const float RuntimeWaitSeconds = 20f;
        private const string SmokeDirectoryName = "ModUtilsSmoke";

        private bool _hasRun;

        private IEnumerator Start()
        {
            var start = Time.realtimeSinceStartup;
            while (TryGetGameLocalization() == null &&
                   Time.realtimeSinceStartup - start < RuntimeWaitSeconds)
                yield return new WaitForSeconds(0.5f);

            while (ObjectDB.instance == null &&
                   Time.realtimeSinceStartup - start < RuntimeWaitSeconds)
                yield return new WaitForSeconds(0.5f);

            RunSmokeChecksOnce();
        }

        private void RunSmokeChecksOnce()
        {
            if (_hasRun) return;
            _hasRun = true;

            var results = new List<CheckResult>
            {
                RunCheck("csv_parse_line", CheckCsvParseLine),
                RunCheck("csv_empty_and_whitespace_fields", CheckCsvEmptyAndWhitespaceFields),
                RunCheck("csv_parse_document", CheckCsvParseDocument),
                RunCheck("csv_escape_round_trip", CheckCsvEscapeRoundTrip),
                RunCheck("string_list_config_converter", CheckStringListConfigConverter),
                RunCheck("l10n_localize_inputs", CheckL10NLocalizeInputs),
                RunCheck("translations_loader_null_translations", CheckTranslationsLoaderNullTranslations),
                RunCheck("localization_cache_refresh", CheckLocalizationCacheRefresh),
                RunCheck("inventory_fill_free_stack_space_prefab_name", CheckInventoryFillFreeStackSpace)
            };

            foreach (var result in results)
            {
                if (result.Status == CheckStatus.Pass)
                    Logger.LogInfo($"[ModUtils Smoke] PASS {result.Name}");
                else if (result.Status == CheckStatus.Skip)
                    Logger.LogWarning($"[ModUtils Smoke] SKIP {result.Name}: {result.Message}");
                else
                    Logger.LogError($"[ModUtils Smoke] FAIL {result.Name}: {result.Message}");
            }

            var failed = results.Where(result => result.Status == CheckStatus.Fail)
                                .Select(result => result.Name)
                                .ToArray();
            var skipped = results.Where(result => result.Status == CheckStatus.Skip)
                                 .Select(result => result.Name)
                                 .ToArray();
            var passed = results.Count(result => result.Status == CheckStatus.Pass);

            Logger.LogInfo(
                $"[ModUtils Smoke] Summary: total={results.Count} passed={passed} failed={failed.Length} skipped={skipped.Length} failedChecks=[{string.Join(", ", failed)}] skippedChecks=[{string.Join(", ", skipped)}]");
        }

        private CheckResult RunCheck(string name, Func<CheckResult> check)
        {
            try
            {
                var result = check();
                return result.WithName(name);
            }
            catch (Exception e)
            {
                return CheckResult.Fail(name, e.GetType().Name + ": " + e.Message);
            }
        }

        private CheckResult CheckCsvParseLine()
        {
            var fields = Csv.ParseLine("alpha,\"bravo,charlie\",\"d\"\"e\"");
            Expect(fields.Count == 3, "expected three fields");
            Expect(fields[0] == "alpha", "first field mismatch");
            Expect(fields[1] == "bravo,charlie", "quoted comma field mismatch");
            Expect(fields[2] == "d\"e", "escaped quote field mismatch");
            return CheckResult.Pass();
        }

        private CheckResult CheckCsvEmptyAndWhitespaceFields()
        {
            var preserved = Csv.ParseLine(" alpha ,,bravo, ");
            Expect(preserved.SequenceEqual(new[] { " alpha ", "", "bravo", " " }),
                "default CSV parsing should preserve unquoted whitespace and empty fields");

            var trailingEmpty = Csv.ParseLine("alpha,");
            Expect(trailingEmpty.SequenceEqual(new[] { "alpha", "" }),
                "CSV parsing should preserve a trailing empty field");

            var trimmed = Csv.ParseLine(" alpha ,\" bravo \", ", true);
            Expect(trimmed.SequenceEqual(new[] { "alpha", " bravo ", "" }),
                "trim mode should trim only unquoted fields");

            var spacedQuoted = Csv.ParseLine(" alpha , \"bravo,charlie\", \" delta \" ", true);
            Expect(spacedQuoted.SequenceEqual(new[] { "alpha", "bravo,charlie", " delta " }),
                "trim mode should allow separator whitespace before quoted fields");

            Expect(Csv.Escape(null) == "", "CSV escaping should tolerate null fields");
            return CheckResult.Pass();
        }

        private CheckResult CheckCsvParseDocument()
        {
            var records = Csv.Parse("a,b\r\n1,2\n\"3,3\",4");
            Expect(records.Count == 3, "expected three records");
            Expect(records[0].SequenceEqual(new[] { "a", "b" }), "header row mismatch");
            Expect(records[1].SequenceEqual(new[] { "1", "2" }), "second row mismatch");
            Expect(records[2].SequenceEqual(new[] { "3,3", "4" }), "quoted row mismatch");
            return CheckResult.Pass();
        }

        private CheckResult CheckCsvEscapeRoundTrip()
        {
            var source = new[] { "", " alpha", "bravo,charlie", "d\"e", "line\nbreak" };
            var line = string.Join(",", source.Select(Csv.Escape));
            var parsed = Csv.ParseLine(line);
            Expect(parsed.SequenceEqual(source), "escaped fields did not parse back to the original values");
            return CheckResult.Pass();
        }

        private CheckResult CheckStringListConfigConverter()
        {
            RuntimeHelpers.RunClassConstructor(typeof(Configuration).TypeHandle);
            Expect(TomlTypeConverter.CanConvert(typeof(StringList)),
                "StringList TOML converter is not registered");

            var source = new StringList(new[] { "alpha", "bravo,charlie", " delta " });
            var serialized = TomlTypeConverter.ConvertToString(source, typeof(StringList));
            var parsed = (StringList)TomlTypeConverter.ConvertToValue(serialized, typeof(StringList));

            Expect(parsed.Count == 3,
                $"StringList count did not round-trip; serialized=[{serialized}], count={parsed.Count}");
            Expect(parsed.Contains("alpha"), "StringList lost alpha");
            Expect(parsed.Contains("bravo,charlie"), "StringList lost comma-containing value");
            Expect(parsed.Contains(" delta "), "StringList lost quoted whitespace-preserving value");

            var trimmed = (StringList)TomlTypeConverter.ConvertToValue(" alpha , bravo ", typeof(StringList));
            Expect(trimmed.Contains("alpha"), "StringList did not trim first unquoted value");
            Expect(trimmed.Contains("bravo"), "StringList did not trim second unquoted value");
            return CheckResult.Pass();
        }

        private CheckResult CheckL10NLocalizeInputs()
        {
            var localization = new L10N("modutils_smoke");
            localization.AddWord("@hello", "Hello");
            localization.AddWord("@template", "Slot $1/$2");

            Expect(localization.Localize(null) == null, "null text should stay null");
            Expect(localization.Localize("") == "", "empty text should stay empty");
            Expect(localization.Localize("plain literal") == "plain literal",
                "literal text should be preserved");
            Expect(localization.Localize("Say @hello now") == "Say Hello now",
                "literal prefix/suffix was not preserved around an internal name");
            Expect(localization.Localize("@template", "first", null) == "Slot first/",
                "null argument value should be inserted as an empty string");
            Expect(localization.Localize("@template", null) == "Slot $1/$2",
                "null argument array should be treated as no arguments");
            return CheckResult.Pass();
        }

        private CheckResult CheckTranslationsLoaderNullTranslations()
        {
            var directory = CreateSmokeTempDirectory("translations");
            try
            {
                File.WriteAllText(Path.Combine(directory, "English.null.json"),
                    "{\"language\":\"English\",\"translations\":null}");
                File.WriteAllText(Path.Combine(directory, "English.valid.json"),
                    "{\"language\":\"English\",\"translations\":{\"@loader_check\":\"Loaded\"}}");

                var localization = new L10N("modutils_smoke");
                new TranslationsLoader(localization).LoadTranslations(directory, "English");
                Expect(localization.Translate("@loader_check") == "Loaded",
                    "valid translations were not loaded after a null translations file");
                return CheckResult.Pass();
            }
            finally
            {
                TryDeleteDirectory(directory);
            }
        }

        private CheckResult CheckLocalizationCacheRefresh()
        {
            var localization = TryGetGameLocalization();
            if (localization == null)
                return CheckResult.Skip("Valheim Localization.instance is not available");

            var key = "modutils_smoke_cache_" +
                      DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
            var source = "$" + key;
            var missing = localization.Localize(source);
            Expect(missing == "[" + key + "]", "expected missing localization to seed cache");

            var expected = "cache-ok-" + key;
            new L10N("modutils_smoke").AddWord(source, expected);
            Expect(localization.Localize(source) == expected,
                "Valheim Localization cache was not refreshed after AddWord");
            return CheckResult.Pass();
        }

        private static Localization TryGetGameLocalization()
        {
            try
            {
                return Localization.instance;
            }
            catch
            {
                return null;
            }
        }

        private CheckResult CheckInventoryFillFreeStackSpace()
        {
            var objectDb = ObjectDB.instance;
            if (objectDb == null)
                return CheckResult.Skip("ObjectDB.instance is not available");

            var prefab = objectDb.m_items.Select(item => item != null
                    ? new { Prefab = item, Drop = item.GetComponent<ItemDrop>() }
                    : null)
                .Where(item => item != null &&
                               item.Drop != null &&
                               item.Drop.m_itemData != null &&
                               item.Drop.m_itemData.m_shared != null &&
                               item.Drop.m_itemData.m_shared.m_maxStackSize >= 4)
                .Select(item => item.Prefab)
                .FirstOrDefault();

            if (prefab == null)
                return CheckResult.Skip("no stackable ObjectDB item prefab with max stack size >= 4");

            var itemDrop = prefab.GetComponent<ItemDrop>();
            var maxStack = itemDrop.m_itemData.m_shared.m_maxStackSize;
            var worldLevel = 0f;
            var transferAmount = 3;

            var from = new Inventory("ModUtilsSmokeFrom", null, 4, 4);
            var to = new Inventory("ModUtilsSmokeTo", null, 4, 4);
            var fromItem = CloneItemData(prefab, transferAmount, 1, (int)worldLevel);
            var toItem = CloneItemData(prefab, maxStack - 2, 1, (int)worldLevel);

            Expect(from.AddItem(fromItem), "failed to add source item to in-memory inventory");
            Expect(to.AddItem(toItem), "failed to add target item to in-memory inventory");

            var moved = Inventories.FillFreeStackSpace(from, to, prefab.name, worldLevel,
                transferAmount, 1, true);

            Expect(moved == 2, "expected exactly two items to fill target stack");
            Expect(from.NrOfItemsIncludingStacks() == 1,
                "source inventory should retain one item after transfer");
            Expect(to.NrOfItemsIncludingStacks() == maxStack,
                "target inventory should be filled to max stack");
            return CheckResult.Pass();
        }

        private static ItemDrop.ItemData CloneItemData(GameObject prefab, int stack, int quality,
            int worldLevel)
        {
            var itemDrop = prefab.GetComponent<ItemDrop>();
            var itemData = itemDrop.m_itemData.Clone();
            itemData.m_dropPrefab = prefab;
            itemData.m_stack = stack;
            itemData.m_quality = quality;
            itemData.m_worldLevel = worldLevel;
            return itemData;
        }

        private static string CreateSmokeTempDirectory(string purpose)
        {
            var baseDirectory = string.IsNullOrEmpty(Paths.ConfigPath)
                ? Path.Combine(Path.GetTempPath(), SmokeDirectoryName)
                : Path.Combine(Paths.ConfigPath, SmokeDirectoryName);
            var directory = Path.Combine(baseDirectory, purpose,
                DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void TryDeleteDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
            catch
            {
                // Smoke temp cleanup is best-effort; stale files remain under ModUtilsSmoke.
            }
        }

        private static void Expect(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private enum CheckStatus
        {
            Pass,
            Fail,
            Skip
        }

        private sealed class CheckResult
        {
            private CheckResult(CheckStatus status, string name, string message)
            {
                Status = status;
                Name = name ?? "";
                Message = message ?? "";
            }

            public CheckStatus Status { get; }
            public string Name { get; }
            public string Message { get; }

            public static CheckResult Pass()
            {
                return new CheckResult(CheckStatus.Pass, "", "");
            }

            public static CheckResult Fail(string name, string message)
            {
                return new CheckResult(CheckStatus.Fail, name, message);
            }

            public static CheckResult Skip(string message)
            {
                return new CheckResult(CheckStatus.Skip, "", message);
            }

            public CheckResult WithName(string name)
            {
                return new CheckResult(Status, name, Message);
            }
        }
    }
}
