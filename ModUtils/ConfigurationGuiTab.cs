using System;
using UnityEngine;
using UnityEngine.UI;
using Valheim.SettingsGui;

namespace ModUtils
{
    /// <summary>
    /// Settings tab behind the shared Mods page. Mod panels write every change immediately, so the
    /// tab has nothing to apply on OK or to revert on Back.
    /// </summary>
    internal sealed class ConfigurationGuiTab : MonoBehaviour, ISettingsTab
    {
        public event Action<string, int> SharedSettingChanged
        {
            add { }
            remove { }
        }

        public void Initialize()
        {
        }

        public void Terminate()
        {
        }

        public void OnTabOpen(Button backButton, Button okButton)
        {
        }

        public void OnOkAsync(OkActionCompletedHandler okActionCompletedCallback)
        {
            okActionCompletedCallback?.Invoke();
        }

        public void OnBack()
        {
        }

        public void OnSharedSettingChanged(string setting, int value)
        {
        }
    }
}
