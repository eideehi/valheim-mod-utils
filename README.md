# ModUtils - Valheim Library

ModUtils is a library of utilities for Valheim mod development. It is not a standalone Valheim mod package.

## In-game config screen

A mod opts in by calling `ConfigurationGui.Register(this)` once in its plugin `Awake`. The screen then shows the entries of that plugin's `Config`.

- The vanilla Settings screen (main menu and pause menu) gets a **Mods** tab that lists every registered mod by name. Clicking a mod replaces the list with that mod's panel; Back or Escape returns to the list. Several mods that each include ModUtils share one Mods tab.
- The panel groups entries into collapsible sections by `Category` (else the config section) and orders them by descending `Order`. Hovering a row shows its description below the list; a description too long for that area unfolds in full over the list when the pointer stays on the row for half a second. Each editable row has a Reset button that sets the default value.
- Changes stay pending in the panel until **Apply** is pressed, which writes them to the entries and so to the config file; the panel stays open and then shows the values the entries hold. Apply can be pressed only while changes are pending. **Reset all** sets the default value of every row that has a Reset button, including rows that the search or the advanced toggle hides; read-only rows keep their values. Like Reset, it takes effect on Apply.
- Back or Escape with pending changes asks whether to discard them: Yes discards them and returns to the list, No keeps the panel and the changes. Selecting another Settings tab or closing the Settings screen discards pending changes without asking. OK and Back on the Settings screen do not apply them.
- Editors:
  - `bool`: toggle.
  - `int` and `float` with an `AcceptableValueRange`: slider with a number field. A drag takes effect when the pointer is released. A typed number takes effect on Enter or when the field loses focus and is clamped to the range then, not while it is typed; text that is not a number brings back the current value.
  - `int` and `float` without a range, and `string`: text field. Text that does not convert brings back the current value.
  - A `string` whose default value is a colour code such as `#4FA9FF` also shows a swatch of the typed colour, and a `Color` entry shows a swatch of its value on its read-only row.
  - `enum`: dropdown labelled by `DescriptionAttribute` (including `LocalizedDescriptionAttribute`), else by the member name. A `[Flags]` enum gets one toggle per flag.
  - `KeyboardShortcut`: key capture that records held modifiers, plus a Clear button. `KeyCode`: key capture of a single key.
  - `StringList`: an expandable list of toggles when its drawer comes from `ConfigurationCustomDrawer.MultiSelect`, else a comma-separated text field.
  - Any other type (for example `Color` or a mod's own list type) is shown read-only as its saved text, unless a mod registers an editor for it (see [Custom editors](#custom-editors)).
- A search field above the rows shows only the rows whose name, category, description, config section or key contain every space-separated word typed, ignoring case. While searching, every section with a matching row is shown expanded; clearing the search brings back each section's collapsed state. Escape in the field leaves it and keeps the search. The search is empty each time a panel opens.
- Entries whose `ConfigurationManagerAttributes` set `Browsable = false` are never shown. `IsAdvanced = true` entries are shown when **Show advanced settings** next to the search field is on; it is off when the game starts, is kept until the game exits, and appears only for a mod that has such entries. A search also finds advanced entries while the toggle is off. `ReadOnly = true` entries are shown but cannot be edited and have no Reset button. Entries bound without these attributes use their section and key as labels.
- ConfigurationManager keeps working alongside the screen, with the same attributes and drawers. It shows a value applied on this screen the next time it draws. An open panel does not follow values changed elsewhere (in ConfigurationManager or by reloading the config file); it shows them when the panel is opened again.
- The screen's own labels are in English and Japanese; other languages use English.

Build requirement: a project that compiles the ModUtils sources must reference `Unity.TextMeshPro` from `valheim_Data/Managed`, for example:

```xml
<Reference Include="Unity.TextMeshPro">
  <HintPath>$(VALHEIM_DIR)\valheim_Data\Managed\Unity.TextMeshPro.dll</HintPath>
  <Private>False</Private>
</Reference>
```

### Custom editors

A mod can show its own setting types with its own editor. Register it once, before the Settings screen opens:

```csharp
ConfigurationGui.RegisterEditor((type, acceptableValues) => type == typeof(ItemList), () => new ItemListEditor());
```

The matcher receives the entry's setting type and acceptable values. Registered editors are consulted before the built-in ones, the last matching registration wins, and the factory must return a new editor for every row. The registration applies to the mods that the same copy of ModUtils registered with `ConfigurationGui.Register`.

An editor derives from `ConfigurationGuiCustomEditor` and follows these rules:

- Read the value through `Value`, which is the change pending in the panel, else the entry's value. Never change it in place.
- Propose a value only through `Commit`, with a new instance. The panel keeps it until Apply; on a `ReadOnly` entry it is ignored.
- Create widgets only through the `ConfigurationGuiWidgets` passed to `Build`: labels, buttons, single-line text fields, row and column containers, `SetLayout` and `ControlHeight`. The slot stacks its children top to bottom at its full width.
- Return the height from `Build`, and change it later only through `SetHeight`.
- The row keeps the name, Reset, the read-only note and the hover description. On a read-only row the widgets created through `ConfigurationGuiWidgets` are disabled unless the editor overrides `SetInteractable`.
- When the matcher, the factory or `Build` throws, the row shows the value read-only and the error is logged. Exceptions from `Refresh`, `SetInteractable` and `ConsumesEscape` are logged and ignored.

A minimal editor for a list type with a text field, an Add button and one line per element with a Remove button:

```csharp
internal sealed class ItemListEditor : ConfigurationGuiCustomEditor
{
    private ConfigurationGuiWidgets _widgets;
    private ConfigurationGuiInputField _field;
    private RectTransform _lines;

    private ItemList Items => (ItemList)Value;

    protected override float Build(RectTransform slot, ConfigurationGuiWidgets widgets)
    {
        _widgets = widgets;
        var input = widgets.CreateRow(slot);
        _field = widgets.CreateTextField(input, _ => { });
        if (_field != null)
            widgets.SetLayout(_field.Input.gameObject, preferredWidth: 0f, flexibleWidth: 1f);
        widgets.CreateButton(input, "Add", Add);
        _lines = widgets.CreateColumn(slot);

        LineHeight = widgets.ControlHeight;
        Refresh();
        return Height();
    }

    protected override void Refresh()
    {
        for (var i = _lines.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(_lines.GetChild(i).gameObject);

        foreach (var item in Items)
        {
            var line = _widgets.CreateRow(_lines);
            var label = _widgets.CreateLabel(line, item);
            _widgets.SetLayout(label.gameObject, preferredWidth: 0f, flexibleWidth: 1f, height: _widgets.ControlHeight);
            _widgets.CreateButton(line, "Remove", () => Commit(new ItemList(Items.Where(x => x != item))));
        }

        SetHeight(Height());
    }

    // The input line and one line per element, 4 units apart.
    private float Height() => _widgets.ControlHeight + Items.Count * (_widgets.ControlHeight + 4f);

    private void Add()
    {
        var text = _field.Input.text.Trim();
        if (text.Length == 0) return;

        _field.Input.SetTextWithoutNotify("");
        Commit(new ItemList(Items.Concat(new[] { text })));
    }
}
```

## Developer smoke tests

Optional in-game smoke-test tooling lives under [smoke/](smoke/). See [smoke/README.md](smoke/README.md) for build and deploy commands.

## Contacts

[Open an issue](https://github.com/eideehi/valheim-mod-utils/issues) for bug reports. Only bug reports are accepted under Issues.

## Credits

- **Dependencies**:
  - [Configuration Manager](https://github.com/BepInEx/BepInEx.ConfigurationManager)
  - [LitJSON](https://litjson.net)

## License

ModUtils is developed under the MIT license. For the full text of the license, see the [LICENSE](LICENSE) file.
