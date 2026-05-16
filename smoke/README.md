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
[ModUtils Smoke] Summary: total=9 passed=9 failed=0 skipped=0 failedChecks=[] skippedChecks=[]
```

The smoke checks create temporary translation files only under
`BepInEx/config/ModUtilsSmoke/` and clean them up on a best-effort basis.

## Remove

To uninstall the smoke plugin, remove the dedicated deploy directory:

```bash
rm -r "$VALHEIM_DIR/BepInEx/plugins/ModUtilsSmoke"
```

The smoke project is optional developer tooling. Repositories that consume
ModUtils as a submodule should continue to build the root solution unless they
explicitly scan nested project files under submodules.
