using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModUtils
{
    /// <summary>
    /// Key capture for KeyboardShortcut and KeyCode entries. Clicking the key button waits for the next
    /// released key; for a shortcut, the other keys still held become its modifiers. Escape cancels the
    /// capture without a change, and a shortcut also has a Clear button.
    /// </summary>
    internal sealed class ConfigurationGuiKeyEditor : ConfigurationGuiEditor
    {
        private const float ClearWidth = 110f;
        private const float Spacing = 8f;

        // At most one capture runs at a time, like ConfigurationManager's.
        private static ConfigurationGuiKeyEditor _capturing;

        private readonly bool _shortcut;
        private Button _button;
        private ConfigurationGuiKeyCapture _capture;
        private Button _clear;
        private int _endFrame = -1;

        internal ConfigurationGuiKeyEditor(ConfigEntryBase entry, bool shortcut) : base(entry)
        {
            _shortcut = shortcut;
        }

        private bool IsCapturing => _capturing == this;

        internal override float Build(RectTransform slot, ConfigurationGuiTemplates templates)
        {
            var height = ConfigurationGuiTemplates.ControlHeight(templates.Button);

            var layout = slot.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = Spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            _button = ConfigurationGuiTemplates.Clone(templates.Button, slot, "Key");
            ConfigurationGuiTemplates.SetFixedButtonLabel(_button, templates.Label);
            ConfigurationGuiTemplates.AddLayout(_button.gameObject, minWidth: 0f, preferredWidth: 0f,
                preferredHeight: height, flexibleWidth: 1f);
            _capture = _button.gameObject.AddComponent<ConfigurationGuiKeyCapture>();
            _capture.enabled = false;
            _capture.Owner = this;
            _button.onClick.AddListener(StartCapture);

            if (_shortcut)
            {
                _clear = ConfigurationGuiTemplates.Clone(templates.Button, slot, "Clear");
                ConfigurationGuiTemplates.SetLabel(_clear, ConfigurationGui.Translate(ConfigurationGui.ClearKey));
                ConfigurationGuiTemplates.SetFixedButtonLabel(_clear, templates.Label, ClearWidth);
                ConfigurationGuiTemplates.AddLayout(_clear.gameObject, minWidth: ClearWidth,
                    preferredWidth: ClearWidth, preferredHeight: height);
                _clear.onClick.AddListener(() =>
                {
                    StopCapture();
                    Commit(KeyboardShortcut.Empty);
                });
            }

            Refresh();
            return height;
        }

        internal override void Refresh()
        {
            string label;
            if (IsCapturing)
                label = ConfigurationGui.Translate(ConfigurationGui.PressKeyKey);
            else if (_shortcut)
                label = ((KeyboardShortcut)Value).ToString();
            else
                label = Value.ToString();

            ConfigurationGuiTemplates.SetLabel(_button, label);
        }

        internal override void SetInteractable(bool interactable)
        {
            _button.interactable = interactable;
            if (_clear != null)
                _clear.interactable = interactable;
        }

        /// <summary>
        /// The capture reacts to Escape itself, which may happen before the panel looks at that press in
        /// the same frame.
        /// </summary>
        internal override bool ConsumesEscape()
        {
            return IsCapturing || _endFrame == Time.frameCount;
        }

        internal void StopCapture()
        {
            if (!IsCapturing) return;

            _capturing = null;
            _endFrame = Time.frameCount;
            if (_capture != null)
                _capture.enabled = false;
            if (_button != null)
                Refresh();
        }

        internal void FinishCapture(KeyCode key, KeyCode[] heldKeys)
        {
            StopCapture();
            Commit(_shortcut ? (object)new KeyboardShortcut(key, heldKeys) : key);
        }

        private void StartCapture()
        {
            if (IsCapturing) return;

            if (_capturing != null)
                _capturing.StopCapture();
            _capturing = this;

            // Enter or Space on a still-selected widget would otherwise press it during the capture.
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);

            _capture.Begin();
            Refresh();
        }
    }

    /// <summary>
    /// Polls the keyboard for a <see cref="ConfigurationGuiKeyEditor"/>; it is enabled only while a
    /// capture runs, and disabling it (also by leaving the panel) ends the capture.
    /// </summary>
    internal sealed class ConfigurationGuiKeyCapture : MonoBehaviour
    {
        private static KeyCode[] _keys;

        internal ConfigurationGuiKeyEditor Owner;
        private int _startFrame;

        // The left mouse button started the capture and Escape cancels it, so neither can be captured.
        private static KeyCode[] Keys =>
            _keys ?? (_keys = UnityInput.Current.SupportedKeyCodes
                                        .Except(new[] { KeyCode.None, KeyCode.Mouse0, KeyCode.Escape })
                                        .ToArray());

        internal void Begin()
        {
            _startFrame = Time.frameCount;
            enabled = true;
        }

        private void Update()
        {
            if (Owner == null || Time.frameCount == _startFrame) return;

            if (ZInput.GetKeyDown(KeyCode.Escape))
            {
                Owner.StopCapture();
                return;
            }

            var input = UnityInput.Current;
            foreach (var key in Keys)
            {
                if (!input.GetKeyUp(key)) continue;

                Owner.FinishCapture(key, Keys.Where(input.GetKey).ToArray());
                return;
            }
        }

        private void OnDisable()
        {
            if (Owner != null)
                Owner.StopCapture();
        }
    }
}
