# Build Notes

## Project layout
- Solution: `ModUtils.sln`
- Project: `ModUtils/ModUtils.csproj`
- Target framework: `.NET Framework 4.8`

## Repository notes
- This repository builds the ModUtils library. It does not publish a standalone Valheim mod package.
- `smoke/` contains optional developer smoke-test tooling. Build and deploy it only through the explicit smoke wrapper mode.
- Do not add main library deploy behavior, Thunderstore or Nexus packaging, `Languages/`, `Data/`, or `distributor/` structure to this repository.

## Required local dependencies
- .NET SDK 8.0 or newer
- A local Valheim installation
- BepInEx installed inside the Valheim directory

The project resolves game references from the Valheim install directory. Set the following environment variable before building:

- `VALHEIM_DIR`: absolute path to the Valheim root directory that contains `valheim_Data/Managed`

On Windows, if `VALHEIM_DIR` is not set, the project also tries to locate Valheim from the Steam uninstall registry key.

The expected directory shape is:

- `<Valheim>/valheim_Data/Managed`
- `<Valheim>/BepInEx/core`
- `<Valheim>/unstripped_corlib` if available

If `unstripped_corlib` is missing, the project falls back to `<Valheim>/valheim_Data/Managed` for Unity assemblies.

## Build commands
Use `scripts/build.sh` for normal local builds. It validates `VALHEIM_DIR` and BepInEx core files, passes `FrameworkPathOverride`, and runs `dotnet msbuild`.

Debug build:

```bash
scripts/build.sh
```

Release build:

```bash
scripts/build.sh Release
```

Clean first, then build:

```bash
scripts/build.sh Debug clean
```

Smoke test plugin build and deploy:

```bash
scripts/build.sh smoke Debug
```

Smoke test plugin build without deploy:

```bash
scripts/build.sh smoke Debug no-deploy
```

Version consistency check:

```bash
scripts/check-version.sh
```

## Direct MSBuild fallback
Use direct MSBuild commands only when debugging the build script or project file.

Debug build:

```bash
dotnet msbuild ModUtils/ModUtils.csproj /restore /t:Build /p:Configuration=Debug "/p:Platform=Any CPU" \
  "/p:FrameworkPathOverride=$VALHEIM_DIR/valheim_Data/Managed"
```

Release build:

```bash
dotnet msbuild ModUtils/ModUtils.csproj /restore /t:Build /p:Configuration=Release "/p:Platform=Any CPU" \
  "/p:FrameworkPathOverride=$VALHEIM_DIR/valheim_Data/Managed"
```

## Output
- Debug output: `ModUtils/bin/Debug/ModUtils.dll`
- Release output: `ModUtils/bin/Release/ModUtils.dll`
- Smoke Debug output: `smoke/ModUtils.SmokeTest/bin/Debug/ModUtils.SmokeTest.dll`
- Smoke deploy path when enabled: `<Valheim>/BepInEx/plugins/ModUtilsSmoke`

## Smoke test deploy
Smoke Debug builds through `scripts/build.sh smoke Debug` deploy to `<Valheim>/BepInEx/plugins/ModUtilsSmoke` by default. Pass `no-deploy` to build without installing, or pass `deploy` to force deployment for another configuration.

## Release repack
Release builds use `ILRepack.Lib.MSBuild.Task` via `<PackageReference>` in the csproj (`GeneratePathProperty="true"`).

- If the package is restored, the `ILRepacker` target runs after build and rewrites the final assembly in place.
- If the package is missing, the Release build fails. Run `dotnet restore` before building Release.

## Release packaging
Not applicable. ModUtils is a library repository and does not produce Thunderstore, Nexus, or other standalone mod packages.

## Troubleshooting
- If assembly references fail, verify that the chosen Valheim directory really contains `valheim_Data/Managed`.
- If BepInEx references fail, verify that `BepInEx/core/0Harmony.dll` and `BepInEx/core/BepInEx.dll` exist under the same Valheim directory.
- If the Release build fails because ILRepack targets are missing, run `dotnet restore` to restore the ILRepack NuGet package.
