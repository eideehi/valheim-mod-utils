using System;
using HarmonyLib;
using UnityEngine;

namespace ModUtils
{
    /// <summary>
    /// Keeps the game from acting on the keyboard and the mouse while an entry window is open, as it does
    /// while its own screens are open: the player and the camera take no input, the cursor stays free,
    /// Escape does not open the pause menu, and the inventory, map and chat keys open nothing. Every patch
    /// leaves the game alone unless <see cref="ConfigurationGuiEntryWindow.BlocksInput"/> is true.
    /// </summary>
    internal static class ConfigurationGuiEntryWindowInput
    {
        private static bool _patchAttempted;

        /// <summary>
        /// Patches the game once for this copy of ModUtils.
        /// </summary>
        internal static void Apply(string harmonyId)
        {
            if (_patchAttempted) return;
            _patchAttempted = true;

            var harmony = new Harmony(harmonyId);
            // Movement, attacks, blocking, jumping, dodging and the mouse look.
            Patch(harmony, typeof(PlayerController), "TakeInput", new[] { typeof(bool) },
                "the player's movement, attacks and mouse look", postfix: nameof(RefuseInput));
            // Use, the hotbar, building and the build menu, the radial menu, emotes and the other player keys.
            Patch(harmony, typeof(Player), "TakeInput", Type.EmptyTypes,
                "the player's use, hotbar and build keys", postfix: nameof(RefuseInput));
            Patch(harmony, typeof(GameCamera), "UpdateMouseCapture", Type.EmptyTypes,
                "the game from locking the cursor", prefix: nameof(FreeCursor));
            // The camera zoom. The UI reads the wheel through its own input module, so the window's list still
            // scrolls.
            Patch(harmony, typeof(ZInput), "Internal_GetMouseScrollWheel", Type.EmptyTypes,
                "the mouse wheel from zooming the camera", prefix: nameof(HideScrollWheel));
            Patch(harmony, typeof(Menu), "Update", Type.EmptyTypes,
                "Escape from opening the pause menu", prefix: nameof(SkipWhileBlocked));
            // Also the keys bound to console commands, which the chat runs.
            Patch(harmony, typeof(Chat), "Update", Type.EmptyTypes,
                "the chat key", prefix: nameof(SkipWhileBlocked));
            Patch(harmony, typeof(InventoryGui), "Update", Type.EmptyTypes,
                "the inventory key", prefix: nameof(SkipWhileInventoryHidden));
            Patch(harmony, typeof(Minimap), "SetMapMode", new[] { typeof(Minimap.MapMode) },
                "the map key", prefix: nameof(KeepMapClosed));
            Patch(harmony, typeof(Minimap), "UpdateMap", new[] { typeof(Player), typeof(float), typeof(bool) },
                "the minimap zoom keys", prefix: nameof(RefuseMapInput));
        }

        private static void Patch(Harmony harmony, Type type, string name, Type[] parameters, string stops,
            string prefix = null, string postfix = null)
        {
            try
            {
                var method = AccessTools.Method(type, name, parameters);
                // A method inherited from a base class would patch other classes as well.
                if (method == null || method.DeclaringType != type)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[ModUtils] {type.Name}.{name} was not found. An entry window cannot stop {stops} while it is open.");
                    return;
                }

                harmony.Patch(method,
                    prefix: prefix != null ? new HarmonyMethod(typeof(ConfigurationGuiEntryWindowInput), prefix) : null,
                    postfix: postfix != null
                        ? new HarmonyMethod(typeof(ConfigurationGuiEntryWindowInput), postfix)
                        : null);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError(
                    $"[ModUtils] Failed to patch {type.Name}.{name}. An entry window cannot stop {stops} while it is open. {e}");
            }
        }

        private static void RefuseInput(ref bool __result)
        {
            if (ConfigurationGuiEntryWindow.BlocksInput)
                __result = false;
        }

        private static bool FreeCursor()
        {
            if (!ConfigurationGuiEntryWindow.BlocksInput) return true;

            // The game's capture would lock the cursor first, and every lock moves it to the screen's centre,
            // so it does not run at all.
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
            return false;
        }

        private static bool HideScrollWheel(ref float __result)
        {
            if (!ConfigurationGuiEntryWindow.BlocksInput) return true;

            __result = 0f;
            return false;
        }

        private static bool SkipWhileBlocked()
        {
            return !ConfigurationGuiEntryWindow.BlocksInput;
        }

        // A shown inventory keeps working; only opening it is held back.
        private static bool SkipWhileInventoryHidden()
        {
            return !ConfigurationGuiEntryWindow.BlocksInput || InventoryGui.IsVisible();
        }

        private static bool KeepMapClosed(Minimap.MapMode mode)
        {
            return !ConfigurationGuiEntryWindow.BlocksInput || mode != Minimap.MapMode.Large;
        }

        private static void RefuseMapInput(ref bool takeInput)
        {
            if (ConfigurationGuiEntryWindow.BlocksInput)
                takeInput = false;
        }
    }
}
