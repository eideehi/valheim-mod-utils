using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModUtils
{
    /// <summary>
    /// What a mod's panel on the Mods tab and the entry window share: one row per entry with its editor and
    /// Reset button, edits that stay pending until Apply, and leaving through a button or Escape, which asks
    /// first whether to drop pending edits.
    /// </summary>
    internal abstract class ConfigurationGuiEntryView : ConfigurationGuiView
    {
        private const float ResetWidth = 120f;

        private Button _apply;

        /// <summary>
        /// The rows built so far.
        /// </summary>
        protected List<Row> Rows { get; } = new List<Row>();

        /// <summary>
        /// The edits that stay in the view until Apply. Set before any row is built.
        /// </summary>
        private protected ConfigurationGuiPendingValues Pending { get; set; }

        /// <summary>
        /// True while the discard prompt that this view showed is open.
        /// </summary>
        protected bool IsPrompting { get; set; }

        /// <summary>
        /// True when an editor in this view handles the current Escape press itself, so Escape must not
        /// leave the view.
        /// </summary>
        internal override bool IsEscapeConsumed()
        {
            foreach (var row in Rows)
                if (row.Editor.ConsumesEscape())
                    return true;

            return false;
        }

        /// <summary>
        /// Whether the discard prompt covers the screen behind it, so that nothing behind it takes clicks.
        /// </summary>
        protected virtual bool CoversBackgroundWhilePrompting => false;

        /// <summary>
        /// The leave button, Escape and the gamepad back button: closes the view at once without pending
        /// edits, else asks through the game's Yes/No popup whether to drop them.
        /// </summary>
        protected override void Leave()
        {
            // The popup is answered with the same keys, so a press while it is shown, or on the frame it
            // closed, belongs to the popup.
            if (IsClosed || UnifiedPopup.IsVisible() || UnifiedPopup.WasVisibleThisFrame()) return;

            if (!Pending.HasPending)
            {
                Close();
                return;
            }

            var header = ConfigurationGui.Translate(ConfigurationGui.DiscardHeaderKey);
            var question = ConfigurationGui.Translate(ConfigurationGui.DiscardQuestionKey);
            IsPrompting = true;
            UnifiedPopup.Push(new YesNoPopup(header, question, OnDiscardConfirmed, OnDiscardCancelled, false,
                CoversBackgroundWhilePrompting));
        }

        // Both answers always dismiss the popup, as the game's own answers do, and touch the view only
        // while it is still open.
        private void OnDiscardConfirmed()
        {
            UnifiedPopup.Pop();
            IsPrompting = false;
            if (this == null || IsClosed) return;

            Pending.Discard();
            Close();
        }

        private void OnDiscardCancelled()
        {
            UnifiedPopup.Pop();
            IsPrompting = false;
        }

        /// <summary>
        /// Takes down the discard prompt that this view showed, for a view that closes without an answer.
        /// </summary>
        protected void DismissPrompt()
        {
            if (!IsPrompting) return;

            IsPrompting = false;
            if (UnifiedPopup.IsVisible())
                UnifiedPopup.Pop();
        }

        protected virtual void Apply()
        {
            Pending.Apply();
            RefreshEditors();
        }

        protected void RefreshEditors()
        {
            foreach (var row in Rows)
                row.Editor.Refresh();
        }

        /// <summary>
        /// Whether a newly built row shows. Every row shows unless a subclass filters them.
        /// </summary>
        protected virtual bool IsShown(Row row)
        {
            return true;
        }

        protected void UpdateApplyButton()
        {
            if (_apply != null)
                _apply.interactable = Pending.HasPending;
        }

        /// <summary>
        /// The line under the title lines that tells that changes are saved on Apply.
        /// </summary>
        protected void CreateStagedApplyHint()
        {
            var hint = ConfigurationGuiTemplates.CreateText(transform, "Hint", Templates.Label, 0.85f);
            hint.text = ConfigurationGui.Translate(ConfigurationGui.StagedApplyHintKey);
            hint.color = new Color(hint.color.r, hint.color.g, hint.color.b, hint.color.a * 0.75f);
        }

        /// <summary>
        /// The Apply button, available only while edits are pending once <see cref="UpdateApplyButton"/>
        /// runs.
        /// </summary>
        protected void CreateApplyButton(Transform footer, Button style, float width, float height)
        {
            _apply = CreateFooterButton(style, footer, "Apply", ConfigurationGui.ApplyKey, width, height, Apply);
        }

        protected void CreateRow(Row row, RectTransform body)
        {
            var templates = Templates;
            // The name identifies the entry, so automated checks can find a row by section and key.
            var definition = row.Entry.Definition;
            var rowRect = CreateRowObject($"Row:{definition.Section}/{definition.Key}", body, row.Name,
                row.Description, out var name);

            var slot = ConfigurationGuiTemplates.CreateRect("Editor", rowRect);
            var editor = ConfigurationGuiEditor.Create(row.Entry, Pending, templates);
            var height = editor.Build(slot, templates);
            var lineHeight = Mathf.Max(editor.LineHeight > 0f ? editor.LineHeight : height,
                RowMinHeight - 2 * RowPadding);
            var slotHeight = Mathf.Max(height, lineHeight);
            LayOutRowParts(name, slot, lineHeight, slotHeight);
            row.Object = rowRect.gameObject;
            row.Editor = editor;
            row.Editable = editor.IsEditable && !row.ReadOnly;
            Rows.Add(row);

            if (row.Editable)
            {
                var reset = ConfigurationGuiTemplates.Clone(templates.Button, rowRect, "Reset");
                ConfigurationGuiTemplates.SetLabel(reset, ConfigurationGui.Translate(ConfigurationGui.ResetKey));
                ConfigurationGuiTemplates.SetFixedButtonLabel(reset, templates.Label, ResetWidth);

                ConfigurationGuiTemplates.AddLayout(reset.gameObject, minWidth: ResetWidth,
                    preferredWidth: ResetWidth, preferredHeight: lineHeight);
                reset.onClick.AddListener(editor.ResetToDefault);
            }
            else
            {
                editor.SetInteractable(false);
                var note = ConfigurationGuiTemplates.CreateText(rowRect, "ReadOnly", templates.Label, 0.85f);
                note.text = ConfigurationGui.Translate(ConfigurationGui.ReadOnlyKey);
                note.alignment = TextAlignmentOptions.Center;
                ConfigurationGuiTemplates.AddLayout(note.gameObject, minWidth: ResetWidth, minHeight: lineHeight,
                    preferredWidth: ResetWidth);
            }

            rowRect.gameObject.SetActive(IsShown(row));
        }

        internal static ConfigurationManagerAttributes GetAttributes(ConfigEntryBase entry)
        {
            return entry.Description?.Tags?.OfType<ConfigurationManagerAttributes>().FirstOrDefault();
        }

        internal sealed class Row
        {
            private Row(ConfigEntryBase entry, int index, string category, string name, string description,
                int order, bool readOnly, bool advanced)
            {
                Entry = entry;
                Index = index;
                Category = category;
                Name = name;
                Description = description;
                Order = order;
                ReadOnly = readOnly;
                Advanced = advanced;
            }

            internal ConfigEntryBase Entry { get; }

            internal int Index { get; }

            internal string Category { get; }

            internal string Name { get; }

            internal string Description { get; }

            internal int Order { get; }

            internal bool ReadOnly { get; }

            internal bool Advanced { get; }

            internal GameObject Object { get; set; }

            internal ConfigurationGuiEditor Editor { get; set; }

            /// <summary>
            /// True when the row has an editor that can change the value and the entry is not read-only.
            /// </summary>
            internal bool Editable { get; set; }

            /// <summary>
            /// The row of <paramref name="entry"/>, or null for an entry that is never shown
            /// (<c>Browsable = false</c>).
            /// </summary>
            internal static Row Create(ConfigEntryBase entry, int index)
            {
                var attributes = GetAttributes(entry);
                if (attributes?.Browsable == false) return null;

                var definition = entry.Definition;
                var category = string.IsNullOrEmpty(attributes?.Category) ? definition.Section : attributes.Category;
                var name = string.IsNullOrEmpty(attributes?.DispName) ? definition.Key : attributes.DispName;
                var description = string.IsNullOrEmpty(attributes?.Description)
                    ? entry.Description?.Description
                    : attributes.Description;

                return new Row(entry, index, category, name, description ?? "", attributes?.Order ?? 0,
                    attributes?.ReadOnly == true, attributes?.IsAdvanced == true);
            }

            internal bool Matches(string[] terms)
            {
                var definition = Entry.Definition;
                return terms.All(x => Contains(Name, x) || Contains(Category, x) || Contains(Description, x) ||
                                      Contains(definition.Section, x) || Contains(definition.Key, x));
            }

            private static bool Contains(string text, string term)
            {
                return text != null && text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }
    }
}
