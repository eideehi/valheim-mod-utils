# ModUtils - Valheim Library

ModUtils is a library of utilities for Valheim mod development. It is not a standalone Valheim mod package.

## In-game config screen

A mod opts in by calling `ConfigurationGui.Register(this)` once in its plugin `Awake`. The screen then shows the entries of that plugin's `Config`.

- The vanilla Settings screen (main menu and pause menu) gets a **Mods** tab that lists every registered mod by name. Clicking a mod replaces the list with that mod's panel; Back or Escape returns to the list. Several mods that each include ModUtils share one Mods tab.
- The panel groups entries into collapsible sections by `Category` (else the config section) and orders them by descending `Order`. Hovering a row shows its description, and each editable row has a Reset button that restores the default value.
- Every change is written to the entry, and so to the config file, immediately. OK and Back on the Settings screen neither apply nor undo these changes.
- Editors:
  - `bool`: toggle.
  - `int` and `float` with an `AcceptableValueRange`: slider with a number field. A drag is saved when the pointer is released. A typed number is saved on Enter or when the field loses focus and is clamped to the range then, not while it is typed; text that is not a number brings back the current value.
  - `int` and `float` without a range, and `string`: text field. Text that does not convert brings back the current value.
  - A `string` whose default value is a colour code such as `#4FA9FF` also shows a swatch of the typed colour, and a `Color` entry shows a swatch of its value on its read-only row.
  - `enum`: dropdown labelled by `DescriptionAttribute` (including `LocalizedDescriptionAttribute`), else by the member name. A `[Flags]` enum gets one toggle per flag.
  - `KeyboardShortcut`: key capture that records held modifiers, plus a Clear button. `KeyCode`: key capture of a single key.
  - `StringList`: an expandable list of toggles when its drawer comes from `ConfigurationCustomDrawer.MultiSelect`, else a comma-separated text field.
  - Any other type (for example `Color` or a mod's own list type) is shown read-only as its saved text.
- Entries whose `ConfigurationManagerAttributes` set `Browsable = false` or `IsAdvanced = true` are hidden. `ReadOnly = true` entries are shown but cannot be edited and have no Reset button. Entries bound without these attributes use their section and key as labels.
- ConfigurationManager keeps working alongside the screen, with the same attributes and drawers. It shows a value changed on this screen the next time it draws. An open panel does not follow values changed elsewhere (in ConfigurationManager or by reloading the config file); it shows them when the panel is opened again.
- The screen's own labels are in English and Japanese; other languages use English.

Build requirement: a project that compiles the ModUtils sources must reference `Unity.TextMeshPro` from `valheim_Data/Managed`, for example:

```xml
<Reference Include="Unity.TextMeshPro">
  <HintPath>$(VALHEIM_DIR)\valheim_Data\Managed\Unity.TextMeshPro.dll</HintPath>
  <Private>False</Private>
</Reference>
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
