# ModUtils Valheim Smoke Test

This directory contains optional developer tooling for in-game ModUtils smoke checks.
It is intentionally not part of the root `ModUtils.sln`, and it does not add
root-level MSBuild files or production project settings.

## Build

Set `VALHEIM_DIR` to a local Valheim install that contains `valheim_Data/Managed`
and `BepInEx/core`, then build the smoke-only solution explicitly:

```bash
VALHEIM_DIR=/mnt/g/steam/steamapps/common/Valheim dotnet msbuild smoke/ModUtils.SmokeTest.sln /p:Configuration=Debug "/p:Platform=Any CPU"
```

The default build writes only under `smoke/ModUtils.SmokeTest/bin/` and the
referenced `ModUtils/bin/` output. It does not install anything into Valheim.

## Deploy

Deploy is opt-in. Pass `DeploySmoke=true` only when you want to copy the smoke
plugin to the local Valheim BepInEx plugin directory:

```bash
VALHEIM_DIR=/mnt/g/steam/steamapps/common/Valheim dotnet msbuild smoke/ModUtils.SmokeTest.sln /p:Configuration=Debug "/p:Platform=Any CPU" /p:DeploySmoke=true
```

The deploy target writes only to:

```text
<VALHEIM_DIR>/BepInEx/plugins/ModUtilsSmoke/
```

## Run And Logs

Launch Valheim after deploying. The plugin logs individual smoke results and one
summary line to the BepInEx log:

```text
[ModUtils Smoke] Summary: total=8 passed=8 failed=0 skipped=0 failedChecks=[] skippedChecks=[]
```

The smoke checks create temporary translation files only under
`BepInEx/config/ModUtilsSmoke/` and clean them up on a best-effort basis.

## Remove

To uninstall the smoke plugin, remove the dedicated deploy directory:

```bash
rm -r /mnt/g/steam/steamapps/common/Valheim/BepInEx/plugins/ModUtilsSmoke
```

The smoke project is optional developer tooling. Repositories that consume this
repository as a submodule should continue to build the root solution unless they
explicitly choose to scan nested project files under submodules.
