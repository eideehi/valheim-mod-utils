# Build Notes

## Project layout
- Solution: `ModUtils.sln`
- Project: `ModUtils/ModUtils.csproj`
- Target framework: `.NET Framework 4.8`

## Required local dependencies
- A local Valheim installation
- A local BepInEx installation inside the Valheim directory

The project resolves game references from the Valheim install directory. Set one of these environment variables before building:

- `VALHEIM_DIR`: absolute path to the Valheim root directory that contains `valheim_Data/Managed`
- `VALHEIM_DIRECTORY`: same as `VALHEIM_DIR`
- `STEAM_GAME_DIR`: either the Valheim root directory, or a Steam library directory that contains `Valheim/valheim_Data/Managed`

On Windows, if none of the variables above are set, the project also tries to locate Valheim from the Steam uninstall registry key.

The expected directory shape is:

- `<Valheim>/valheim_Data/Managed`
- `<Valheim>/BepInEx/core`
- `<Valheim>/unstripped_corlib` if available

If `unstripped_corlib` is missing, the project falls back to `<Valheim>/valheim_Data/Managed` for Unity assemblies.

## Build commands
Use MSBuild-compatible tooling. The expected path on Windows is Visual Studio MSBuild.

Debug build:

```bash
msbuild ModUtils.sln /p:Configuration=Debug "/p:Platform=Any CPU"
```

Release build:

```bash
msbuild ModUtils.sln /p:Configuration=Release "/p:Platform=Any CPU"
```

If your environment prefers the .NET SDK entry point, `dotnet msbuild` can be used instead of `msbuild` as long as the machine can build .NET Framework 4.8 projects.

## Output
- Debug output: `ModUtils/bin/Debug/ModUtils.dll`
- Release output: `ModUtils/bin/Release/ModUtils.dll`

## Release repack
Release builds optionally import `ILRepack.Lib.MSBuild.Task` from `../packages/ILRepack.Lib.MSBuild.Task.2.0.18.2/...`.

- If the package is available, the `ILRepacker` target runs after build and rewrites the final assembly in place.
- If the package is missing, the Release build does not fail; it logs that standalone repack was skipped.

`packages.config` currently contains only this optional ILRepack package, so package restore is only needed if you want the repack step.

## Troubleshooting
- If assembly references fail, verify that the chosen Valheim directory really contains `valheim_Data/Managed`.
- If BepInEx references fail, verify that `BepInEx/core/0Harmony.dll` and `BepInEx/core/BepInEx.dll` exist under the same Valheim directory.
- If Release build succeeds but no repack happens, restore the ILRepack package into `../packages`.
