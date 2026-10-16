using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ModUtils
{
    /// <summary>
    /// In-game configuration screen reached from a "Mods" tab on the vanilla Settings screen.
    /// </summary>
    public static class ConfigurationGui
    {
        internal const string L10NPrefix = "mod_utils";
        internal const string ModsTabKey = "@config_gui_mods_tab";
        internal const string BackKey = "@config_gui_back";
        internal const string ResetKey = "@config_gui_reset";
        internal const string ApplyHintKey = "@config_gui_apply_hint";
        internal const string ReadOnlyKey = "@config_gui_read_only";
        internal const string PressKeyKey = "@config_gui_press_key";
        internal const string ClearKey = "@config_gui_clear";

        // Every ModUtils copy in the process finds the shared tab through these names, so they never change.
        internal const string PageName = "ModUtils.ModsTab.v1";
        internal const string TabButtonName = "ModUtils.ModsTab.v1.Button";
        internal const string ListName = "List";
        internal const string PanelsName = "Panels";
        internal const string ButtonTemplateName = "ButtonTemplate";

        private const string HarmonyIdPrefix = "net.eideehi.modutils.configurationgui.";
        private const float ListPadding = 16f;
        private const float ListSpacing = 8f;
        private const float ModButtonMinWidth = 320f;

        private static readonly List<ConfigurationGuiPlugin> Plugins = new List<ConfigurationGuiPlugin>();

        private static readonly Dictionary<string, Dictionary<string, string>> DefaultTranslations =
            new Dictionary<string, Dictionary<string, string>>
            {
                {
                    "English", new Dictionary<string, string>
                    {
                        { ModsTabKey, "Mods" },
                        { BackKey, "Back" },
                        { ResetKey, "Reset" },
                        { ApplyHintKey, "Changes are applied immediately." },
                        { ReadOnlyKey, "Read-only" },
                        { PressKeyKey, "Press a key..." },
                        { ClearKey, "Clear" }
                    }
                },
                {
                    "Japanese", new Dictionary<string, string>
                    {
                        { ModsTabKey, "Mod" },
                        { BackKey, "\u623B\u308B" },
                        { ResetKey, "\u30EA\u30BB\u30C3\u30C8" },
                        {
                            ApplyHintKey,
                            "\u5909\u66F4\u306F\u3059\u3050\u306B\u9069\u7528\u3055\u308C\u307E\u3059\u3002"
                        },
                        { ReadOnlyKey, "\u8AAD\u307F\u53D6\u308A\u5C02\u7528" },
                        { PressKeyKey, "\u30AD\u30FC\u3092\u62BC\u3057\u3066\u304F\u3060\u3055\u3044..." },
                        { ClearKey, "\u30AF\u30EA\u30A2" }
                    }
                }
            };

        private static bool _patchAttempted;
        private static bool _translationsRegistered;

        /// <summary>
        /// Lists the plugin on the "Mods" tab that is added to the vanilla Settings screen, where its
        /// <see cref="BaseUnityPlugin.Config"/> entries can be edited. Every change is written to the
        /// entry immediately. Calling this again for the same plugin GUID does nothing.
        /// </summary>
        /// <remarks>
        /// Call it once from the plugin's <c>Awake</c>. The first call patches <c>Settings.Awake</c> with
        /// the Harmony id <c>net.eideehi.modutils.configurationgui.&lt;plugin GUID&gt;</c>. A project that
        /// compiles the ModUtils sources must reference <c>Unity.TextMeshPro</c> from
        /// <c>valheim_Data/Managed</c>.
        /// </remarks>
        /// <param name="plugin">The plugin whose configuration is shown.</param>
        public static void Register(BaseUnityPlugin plugin)
        {
            if (plugin == null) throw new ArgumentNullException(nameof(plugin));

            var metadata = plugin.Info.Metadata;
            if (Plugins.Any(x => string.Equals(x.Guid, metadata.GUID, StringComparison.Ordinal))) return;

            Plugins.Add(new ConfigurationGuiPlugin(metadata.GUID, metadata.Name, metadata.Version,
                plugin.Config));
            RegisterTranslations();
            ApplyPatch(metadata.GUID);
        }

        internal static string Translate(string key)
        {
            return L10N.Translate(L10NPrefix, key);
        }

        internal static Transform FindChild(Transform parent, string name)
        {
            if (parent == null) return null;

            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (string.Equals(child.name, name, StringComparison.Ordinal))
                    return child;
            }

            return null;
        }

        private static void RegisterTranslations()
        {
            if (_translationsRegistered) return;
            _translationsRegistered = true;

            foreach (var language in DefaultTranslations)
            foreach (var translation in language.Value)
                ConfigurationCustomDrawer.RegisterDefaultDrawerTranslation(language.Key, translation.Key,
                    translation.Value);
        }

        private static void ApplyPatch(string guid)
        {
            if (_patchAttempted) return;
            _patchAttempted = true;

            try
            {
                var awake = AccessTools.Method(typeof(Settings), "Awake");
                if (awake == null)
                {
                    UnityEngine.Debug.LogWarning(
                        "[ModUtils] Settings.Awake was not found. The Mods settings tab is unavailable.");
                    return;
                }

                new Harmony(HarmonyIdPrefix + guid).Patch(awake,
                    prefix: new HarmonyMethod(typeof(ConfigurationGui), nameof(OnSettingsAwake)));
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(
                    $"[ModUtils] Failed to patch Settings.Awake. The Mods settings tab is unavailable. {e}");
            }
        }

        private static void OnSettingsAwake(Settings __instance)
        {
            try
            {
                AddModsTab(__instance);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[ModUtils] Failed to add the Mods tab to the settings screen: {e}");
            }
        }

        private static void AddModsTab(Settings settings)
        {
            if (Plugins.Count == 0) return;

            var tabHandler = settings.GetComponentInChildren<TabHandler>();
            if (tabHandler == null || tabHandler.m_tabs == null || tabHandler.m_tabs.Count == 0) return;

            ConfigurationCustomDrawer.RefreshDefaultTranslations();

            var page = FindPage(tabHandler) ?? CreatePage(settings, tabHandler);
            if (page == null) return;

            AddPluginButtons(page);
        }

        private static RectTransform FindPage(TabHandler tabHandler)
        {
            foreach (var tab in tabHandler.m_tabs)
                if (tab?.m_page != null && string.Equals(tab.m_page.name, PageName, StringComparison.Ordinal))
                    return tab.m_page;

            return null;
        }

        private static RectTransform CreatePage(Settings settings, TabHandler tabHandler)
        {
            var tabs = tabHandler.m_tabs;
            Button sourceButton = null;
            for (var i = tabs.Count - 1; i >= 0 && sourceButton == null; i--)
                if (tabs[i]?.m_button != null)
                    sourceButton = tabs[i].m_button;

            var firstPage = tabs[0]?.m_page;
            var buttonStyle = ConfigurationGuiTemplates.FindButton(settings);
            if (sourceButton == null || firstPage == null || firstPage.parent == null || buttonStyle == null)
            {
                UnityEngine.Debug.LogWarning(
                    "[ModUtils] The settings screen has no usable tab button, page or button to copy. The Mods tab is not added.");
                return null;
            }

            GameObject tabButton = null;
            GameObject pageObject = null;
            try
            {
                tabButton = CreateTabButton(settings, sourceButton);
                pageObject = CreatePageObject(firstPage, tabs);
                CreatePageChildren(settings, (RectTransform)pageObject.transform, buttonStyle);

                // Settings.Awake asks every page for its ISettingsTab right after this prefix returns.
                pageObject.AddComponent<ConfigurationGuiTab>();
            }
            catch
            {
                if (tabButton != null) Object.Destroy(tabButton);
                if (pageObject != null) Object.Destroy(pageObject);
                throw;
            }

            var page = (RectTransform)pageObject.transform;
            tabs.Add(new TabHandler.Tab
            {
                m_button = tabButton.GetComponent<Button>(),
                m_page = page,
                m_default = false,
                m_onClick = new UnityEvent()
            });
            return page;
        }

        private static GameObject CreateTabButton(Settings settings, Button source)
        {
            var sourceTransform = source.transform;
            var clone = Object.Instantiate(source.gameObject, sourceTransform.parent, false);
            clone.name = TabButtonName;
            clone.transform.SetSiblingIndex(sourceTransform.GetSiblingIndex() + 1);

            RemoveTabKeyHints(settings, sourceTransform, clone.transform);
            ConfigurationGuiTemplates.Sanitize(clone);

            // TabHandler copies this label to the "Selected" child in Start, so it must be set now.
            var token = "$" + L10N.GetTranslationKey(L10NPrefix, ModsTabKey);
            foreach (var text in clone.GetComponentsInChildren<TMP_Text>(true))
                text.text = token;

            // Registers the token so the game's periodic re-localization keeps the label translated.
            var localization = L10N.TryGetLocalization();
            if (localization != null)
                localization.Localize(clone.transform);

            return clone;
        }

        private static void RemoveTabKeyHints(Settings settings, Transform source, Transform clone)
        {
            var field = AccessTools.Field(typeof(Settings), "m_tabKeyHints");
            if (!(field?.GetValue(settings) is GameObject[] hints)) return;

            foreach (var hint in hints)
            {
                if (hint == null || hint.transform == source || !hint.transform.IsChildOf(source)) continue;

                var copy = FindCopy(source, clone, hint.transform);
                if (copy == null) continue;

                copy.gameObject.SetActive(false);
                Object.Destroy(copy.gameObject);
            }
        }

        private static Transform FindCopy(Transform sourceRoot, Transform cloneRoot, Transform sourceTarget)
        {
            var path = new List<int>();
            for (var current = sourceTarget; current != sourceRoot; current = current.parent)
                path.Add(current.GetSiblingIndex());

            var copy = cloneRoot;
            for (var i = path.Count - 1; i >= 0; i--)
            {
                if (path[i] >= copy.childCount) return null;
                copy = copy.GetChild(path[i]);
            }

            return copy;
        }

        private static GameObject CreatePageObject(RectTransform firstPage, List<TabHandler.Tab> tabs)
        {
            var pageObject = new GameObject(PageName, typeof(RectTransform));
            pageObject.SetActive(false);
            pageObject.layer = firstPage.gameObject.layer;

            var page = (RectTransform)pageObject.transform;
            page.SetParent(firstPage.parent, false);
            page.anchorMin = firstPage.anchorMin;
            page.anchorMax = firstPage.anchorMax;
            page.pivot = firstPage.pivot;
            page.sizeDelta = firstPage.sizeDelta;
            page.anchoredPosition = firstPage.anchoredPosition;
            page.localScale = firstPage.localScale;
            page.localRotation = firstPage.localRotation;

            var lastPageIndex = tabs.Where(x => x?.m_page != null && x.m_page.parent == firstPage.parent)
                                    .Max(x => x.m_page.GetSiblingIndex());
            page.SetSiblingIndex(lastPageIndex + 1);

            return pageObject;
        }

        private static void CreatePageChildren(Settings settings, RectTransform page, Button buttonStyle)
        {
            var list = ConfigurationGuiTemplates.CreateRect(ListName, page);
            ConfigurationGuiTemplates.Stretch(list, ListPadding, ListPadding, ListPadding, ListPadding);
            var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = ListSpacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var panels = ConfigurationGuiTemplates.CreateRect(PanelsName, page);
            ConfigurationGuiTemplates.Stretch(panels, 0f, 0f, 0f, 0f);
            panels.gameObject.SetActive(false);

            var template = ConfigurationGuiTemplates.Clone(buttonStyle, page, ButtonTemplateName);
            template.gameObject.SetActive(false);

            var label = template.GetComponentInChildren<TMP_Text>(true);
            if (label == null)
            {
                label = ConfigurationGuiTemplates.CreateText(template.transform, "Label",
                    ConfigurationGuiTemplates.FindLabel(settings));
                ConfigurationGuiTemplates.Stretch((RectTransform)label.transform, 0f, 0f, 0f, 0f);
                label.alignment = TextAlignmentOptions.Center;
            }

            label.text = "";

            var sourceRect = (RectTransform)buttonStyle.transform;
            ConfigurationGuiTemplates.AddLayout(template.gameObject,
                preferredWidth: Mathf.Max(sourceRect.rect.width, ModButtonMinWidth),
                preferredHeight: ConfigurationGuiTemplates.ControlHeight(buttonStyle));
        }

        private static void AddPluginButtons(RectTransform page)
        {
            var list = FindChild(page, ListName);
            var template = FindChild(page, ButtonTemplateName);
            if (list == null || template == null)
            {
                UnityEngine.Debug.LogWarning(
                    "[ModUtils] The Mods tab has no mod list or button template. Mods cannot be listed.");
                return;
            }

            foreach (var plugin in Plugins)
            {
                if (FindChild(list, plugin.Guid) != null) continue;

                var buttonObject = Object.Instantiate(template.gameObject, list, false);
                buttonObject.name = plugin.Guid;

                var button = buttonObject.GetComponent<Button>();
                var label = buttonObject.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                    label.text = plugin.Name;

                var owner = plugin;
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(() => ConfigurationGuiPanel.Open(owner, page));
                buttonObject.SetActive(true);
            }

            SortByLabel(list);
        }

        private static void SortByLabel(Transform list)
        {
            var children = new List<Transform>();
            for (var i = 0; i < list.childCount; i++)
                children.Add(list.GetChild(i));

            var sorted = children.OrderBy(GetLabel, StringComparer.OrdinalIgnoreCase)
                                 .ThenBy(x => x.name, StringComparer.Ordinal)
                                 .ToList();
            for (var i = 0; i < sorted.Count; i++)
                sorted[i].SetSiblingIndex(i);
        }

        private static string GetLabel(Transform button)
        {
            var label = button.GetComponentInChildren<TMP_Text>(true);
            return label != null ? label.text ?? "" : button.name;
        }
    }

    internal sealed class ConfigurationGuiPlugin
    {
        internal ConfigurationGuiPlugin(string guid, string name, System.Version version, ConfigFile config)
        {
            Guid = guid;
            Name = name;
            Version = version;
            Config = config;
        }

        internal string Guid { get; }

        internal string Name { get; }

        internal System.Version Version { get; }

        internal ConfigFile Config { get; }
    }
}
