# ModUtils Valheim Smoke Test

This directory contains optional developer tooling for in-game ModUtils smoke checks.
It is intentionally not part of the root `ModUtils.sln`, and it does not add
root-level MSBuild files or production project settings.

## Build

Set `VALHEIM_DIR` to a local Valheim install that contains `valheim_Data/Managed`
and `BepInEx/core`, then run the root build wrapper:

```bash
scripts/build.sh smoke Debug
```

The default Debug smoke build copies the smoke plugin to the local Valheim
BepInEx plugin directory when `VALHEIM_DIR` points to a valid install.
Pass `no-deploy` to build without installing:

```bash
scripts/build.sh smoke Debug no-deploy
```

## Deploy

The deploy target writes only to:

```text
<VALHEIM_DIR>/BepInEx/plugins/ModUtilsSmoke/
```

Debug smoke builds deploy automatically when the directory is available. Pass
`deploy` to force deployment for another configuration:

```bash
scripts/build.sh smoke Release deploy
```

If deploy is enabled and the Valheim or BepInEx directory cannot be resolved,
the build fails before copying files.

## Run and logs

Launch Valheim after deploying. The plugin logs individual smoke results and one
summary line to the BepInEx log:

```text
[ModUtils Smoke] Summary: total=23 passed=23 failed=0 skipped=0 failedChecks=[] skippedChecks=[]
```

The plugin also registers sample entries for the in-game config screen. Once the
main menu is shown, it opens the Settings screen by itself, drives the Mods tab
and its panel, and closes Settings again; leave the game alone until the summary
line appears. These `settings_gui_*` checks are skipped when the main menu does
not appear within a minute.

The smoke checks create temporary translation files only under
`BepInEx/config/ModUtilsSmoke/` and clean them up on a best-effort basis. The
config screen checks save `settings-gui-list.png`, `settings-gui-panel.png` (the
top of the panel) and `settings-gui-panel-bottom.png` (the panel scrolled to its
bottom with the multi-select list expanded) under
`BepInEx/config/ModUtilsSmoke/ui/` and keep them for review.

## Remove

To uninstall the smoke plugin, remove the dedicated deploy directory:

```bash
rm -r "$VALHEIM_DIR/BepInEx/plugins/ModUtilsSmoke"
```

The smoke project is optional developer tooling. Repositories that consume
ModUtils as a submodule should continue to build the root solution unless they
explicitly scan nested project files under submodules.
