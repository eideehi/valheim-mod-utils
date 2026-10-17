using System;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ModUtils
{
    /// <summary>
    /// Editor that a mod provides for its own setting types, registered with
    /// <see cref="ConfigurationGui.RegisterEditor"/>. The panel row around it keeps the name, the Reset
    /// button, the read-only handling and the hover description.
    /// </summary>
    /// <remarks>
    /// Read the value through <see cref="Value"/> and propose a new one only through
    /// <see cref="Commit"/>, with a new instance; never change <see cref="Value"/> in place, because it can
    /// be the entry's own value or its default. Build widgets only through the
    /// <see cref="ConfigurationGuiWidgets"/> passed to <see cref="Build"/>, and change the row height only
    /// through <see cref="SetHeight"/>. An exception from <see cref="Build"/> turns the row into a
    /// read-only row; exceptions from <see cref="Refresh"/>, <see cref="SetInteractable"/> and
    /// <see cref="ConsumesEscape"/> are logged and ignored.
    /// </remarks>
    public abstract class ConfigurationGuiCustomEditor
    {
        private ConfigurationGuiCustomEditorHost _host;
        private ConfigurationGuiWidgets _widgets;

        protected ConfigurationGuiCustomEditor()
        {
        }

        /// <summary>
        /// The entry the editor edits. Available from <see cref="Build"/> on, not in the constructor.
        /// </summary>
        protected ConfigEntryBase Entry => _host.HostEntry;

        /// <summary>
        /// The value to show: the change pending in the panel, else the entry's value. Do not change it in
        /// place; commit a new instance instead.
        /// </summary>
        protected object Value => _host.HostValue;

        /// <summary>
        /// Height of the editor's first line, which the row lines up its name and Reset button with. Zero
        /// means the whole height returned by <see cref="Build"/>. Set it in <see cref="Build"/>.
        /// </summary>
        protected float LineHeight { get; set; }

        /// <summary>
        /// Creates the editor's widgets inside <paramref name="slot"/>, which lays out its children top to
        /// bottom at its full width, 4 units apart, and returns the height they need. Show <see cref="Value"/> before
        /// returning.
        /// </summary>
        /// <param name="slot">The row's editor area.</param>
        /// <param name="widgets">Creates widgets in the style of the built-in editors.</param>
        protected abstract float Build(RectTransform slot, ConfigurationGuiWidgets widgets);

        /// <summary>
        /// Shows <see cref="Value"/> again. The panel calls it after a commit, a Reset, Reset all and Apply.
        /// </summary>
        protected abstract void Refresh();

        /// <summary>
        /// Enables or disables editing; the panel disables it on a read-only row. By default it sets every
        /// button and text field created through the editor's <see cref="ConfigurationGuiWidgets"/>.
        /// </summary>
        protected virtual void SetInteractable(bool interactable)
        {
            _widgets?.SetInteractable(interactable);
        }

        /// <summary>
        /// True while the editor handles the current Escape press itself, so the panel must stay open. By
        /// default it is true while a text field created through the editor's
        /// <see cref="ConfigurationGuiWidgets"/> is focused or ended editing on this frame.
        /// </summary>
        protected virtual bool ConsumesEscape()
        {
            return _widgets != null && _widgets.ConsumesEscape();
        }

        /// <summary>
        /// Proposes a new value: the panel keeps it until Apply, clamped by the entry's acceptable values,
        /// then calls <see cref="Refresh"/>. Pass a new instance, never the changed <see cref="Value"/>. On
        /// a read-only row the value is ignored.
        /// </summary>
        protected void Commit(object value)
        {
            _host.CommitFromEditor(value);
        }

        /// <summary>
        /// Changes the height of the row's editor area at once, for example after adding a line. During
        /// <see cref="Build"/>, return the height instead.
        /// </summary>
        protected void SetHeight(float height)
        {
            _host.SetSlotHeight(height);
        }

        internal float HostLineHeight => LineHeight;

        internal void Attach(ConfigurationGuiCustomEditorHost host)
        {
            _host = host;
        }

        internal float HostBuild(RectTransform slot, ConfigurationGuiWidgets widgets)
        {
            _widgets = widgets;
            return Build(slot, widgets);
        }

        internal void HostRefresh()
        {
            Refresh();
        }

        internal void HostSetInteractable(bool interactable)
        {
            SetInteractable(interactable);
        }

        internal bool HostConsumesEscape()
        {
            return ConsumesEscape();
        }
    }

    /// <summary>
    /// Runs a mod's <see cref="ConfigurationGuiCustomEditor"/> in a panel row and keeps its failures inside
    /// the row: a failed Build leaves the row read-only, other failures are logged and ignored.
    /// </summary>
    internal sealed class ConfigurationGuiCustomEditorHost : ConfigurationGuiEditor
    {
        private readonly ConfigurationGuiCustomEditor _editor;
        private readonly bool _readOnly;
        private ConfigurationGuiEditor _fallback;
        private RectTransform _slot;

        internal ConfigurationGuiCustomEditorHost(ConfigEntryBase entry, ConfigurationGuiCustomEditor editor) :
            base(entry)
        {
            _editor = editor;
            _readOnly = ConfigurationGuiPanel.GetAttributes(entry)?.ReadOnly == true;
            editor?.Attach(this);
        }

        internal override bool IsEditable => _fallback == null;

        internal ConfigEntryBase HostEntry => Entry;

        internal object HostValue => Value;

        internal override float Build(RectTransform slot, ConfigurationGuiTemplates templates)
        {
            _slot = slot;
            var layout = slot.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = ConfigurationGuiWidgets.Spacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            try
            {
                var height = _editor.HostBuild(slot, new ConfigurationGuiWidgets(templates));
                LineHeight = _editor.HostLineHeight;
                return height;
            }
            catch (Exception e)
            {
                LogError("Build", e);
            }

            // The slot gets only its RectTransform from the panel before Build, so everything else in it
            // came from the failed editor. Components go newest first, so none is still required by another.
            for (var i = slot.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(slot.GetChild(i).gameObject);
            var components = slot.GetComponents<Component>();
            for (var i = components.Length - 1; i >= 0; i--)
                if (!(components[i] is RectTransform))
                    Object.DestroyImmediate(components[i]);

            LineHeight = 0f;
            _fallback = CreateReadOnly(Entry, Pending);
            return _fallback.Build(slot, templates);
        }

        internal override void Refresh()
        {
            if (_fallback != null)
            {
                _fallback.Refresh();
                return;
            }

            try
            {
                _editor.HostRefresh();
            }
            catch (Exception e)
            {
                LogError("Refresh", e);
            }
        }

        internal override void SetInteractable(bool interactable)
        {
            if (_fallback != null)
            {
                _fallback.SetInteractable(interactable);
                return;
            }

            try
            {
                _editor.HostSetInteractable(interactable);
            }
            catch (Exception e)
            {
                LogError("SetInteractable", e);
            }
        }

        internal override bool ConsumesEscape()
        {
            if (_fallback != null) return false;

            try
            {
                return _editor.HostConsumesEscape();
            }
            catch (Exception e)
            {
                LogError("ConsumesEscape", e);
                return false;
            }
        }

        internal void CommitFromEditor(object value)
        {
            if (_readOnly)
                Refresh();
            else
                Commit(value);
        }

        /// <summary>
        /// The row sizes the slot from its layout element, which the panel adds after Build, so the slot
        /// grows and shrinks through it, as the multi-select editor's does.
        /// </summary>
        internal void SetSlotHeight(float height)
        {
            var slotLayout = _slot != null ? _slot.GetComponent<LayoutElement>() : null;
            if (slotLayout == null) return;

            slotLayout.minHeight = height;
            slotLayout.preferredHeight = height;
        }

        private void LogError(string member, Exception e)
        {
            var definition = Entry.Definition;
            UnityEngine.Debug.LogError(
                $"[ModUtils] The registered editor for [{definition.Section}:{definition.Key}] of {Pending.PluginName} failed in {member}: {e}");
        }
    }
}
