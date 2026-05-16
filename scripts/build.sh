#!/usr/bin/env bash
#
# Build the ModUtils library or optional smoke test plugin.
#
# Usage:
#   scripts/build.sh                         # Debug library build
#   scripts/build.sh Release                 # Release library build
#   scripts/build.sh Debug clean             # Clean, then Debug library build
#   scripts/build.sh smoke Debug             # Build and deploy smoke plugin
#   scripts/build.sh smoke Debug no-deploy   # Build smoke plugin without deploy
#
# Environment:
#   VALHEIM_DIR   Path to the Valheim install that contains
#                 valheim_Data/Managed/assembly_valheim.dll and
#                 BepInEx/core/{0Harmony.dll,BepInEx.dll}.
#                 If unset, the script checks common Steam locations on
#                 WSL, Linux, and macOS.
#
set -euo pipefail

usage() {
    echo "usage: scripts/build.sh [Debug|Release] [clean]" >&2
    echo "       scripts/build.sh smoke [Debug|Release] [clean] [deploy|no-deploy]" >&2
}

mode="library"
if [[ "${1:-}" == "smoke" ]]; then
    mode="smoke"
    shift
fi

config="Debug"
clean=""
deploy_arg=""

if [[ $# -gt 3 ]]; then
    usage
    exit 1
fi

if [[ $# -gt 0 ]]; then
    config="$1"
fi

case "$config" in
    Debug | Release) ;;
    *)
        echo "error: configuration must be Debug or Release (got: $config)" >&2
        exit 1
        ;;
esac

if [[ "$mode" == "library" ]]; then
    if [[ $# -gt 2 ]]; then
        usage
        exit 1
    fi

    clean="${2:-}"
    case "$clean" in
        "" | clean) ;;
        *)
            echo "error: second argument must be clean when provided (got: $clean)" >&2
            exit 1
            ;;
    esac
else
    second_arg="${2:-}"
    third_arg="${3:-}"
    deploy_mode=""

    case "$second_arg" in
        "")
            ;;
        clean)
            clean="clean"
            deploy_mode="$third_arg"
            ;;
        deploy | no-deploy)
            deploy_mode="$second_arg"
            if [[ -n "$third_arg" ]]; then
                usage
                exit 1
            fi
            ;;
        *)
            echo "error: second argument must be clean, deploy, or no-deploy when provided (got: $second_arg)" >&2
            exit 1
            ;;
    esac

    case "$deploy_mode" in
        "")
            ;;
        deploy)
            deploy_arg="/p:DeploySmoke=true"
            ;;
        no-deploy)
            deploy_arg="/p:DeploySmoke=false"
            ;;
        *)
            echo "error: smoke deploy argument must be deploy or no-deploy (got: $deploy_mode)" >&2
            exit 1
            ;;
    esac
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &>/dev/null && pwd)"
repo_root="$(cd -- "$script_dir/.." &>/dev/null && pwd)"

if [[ "$mode" == "smoke" ]]; then
    project="$repo_root/smoke/ModUtils.SmokeTest/ModUtils.SmokeTest.csproj"
    output_dll="$repo_root/smoke/ModUtils.SmokeTest/bin/$config/ModUtils.SmokeTest.dll"
else
    project="$repo_root/ModUtils/ModUtils.csproj"
    output_dll="$repo_root/ModUtils/bin/$config/ModUtils.dll"
fi

if [[ ! -f "$project" ]]; then
    echo "error: project file not found: $project" >&2
    exit 1
fi

if [[ -z "${VALHEIM_DIR:-}" ]]; then
    candidates=(
        "/mnt/c/Program Files (x86)/Steam/steamapps/common/Valheim"
        "$HOME/.steam/steam/steamapps/common/Valheim"
        "$HOME/.local/share/Steam/steamapps/common/Valheim"
        "$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
    )
    for path in "${candidates[@]}"; do
        if [[ -f "$path/valheim_Data/Managed/assembly_valheim.dll" ]]; then
            export VALHEIM_DIR="$path"
            echo "Auto-detected VALHEIM_DIR=$VALHEIM_DIR"
            break
        fi
    done
fi

if [[ -z "${VALHEIM_DIR:-}" ]]; then
    echo "error: VALHEIM_DIR is not set and no Valheim install was auto-detected." >&2
    echo "       Expected: \$VALHEIM_DIR/valheim_Data/Managed/assembly_valheim.dll" >&2
    echo "       Set VALHEIM_DIR=/path/to/Valheim and re-run." >&2
    exit 1
fi

managed_dir="$VALHEIM_DIR/valheim_Data/Managed"
bepinex_core_dir="$VALHEIM_DIR/BepInEx/core"

required_files=(
    "$managed_dir/assembly_valheim.dll"
    "$bepinex_core_dir/0Harmony.dll"
    "$bepinex_core_dir/BepInEx.dll"
)

missing_files=()
for required_file in "${required_files[@]}"; do
    if [[ ! -f "$required_file" ]]; then
        missing_files+=("$required_file")
    fi
done

if (( ${#missing_files[@]} > 0 )); then
    echo "error: VALHEIM_DIR is missing required Valheim or BepInEx files." >&2
    for missing_file in "${missing_files[@]}"; do
        echo "       Missing: $missing_file" >&2
    done
    echo "       Set VALHEIM_DIR=/path/to/Valheim with BepInEx installed and re-run." >&2
    exit 1
fi

msbuild_args=(
    "$project"
    /nologo
    /restore
    "/p:Configuration=$config"
    "/p:Platform=Any CPU"
    "/p:FrameworkPathOverride=$managed_dir"
)

if [[ "$mode" == "smoke" && -n "$deploy_arg" ]]; then
    msbuild_args+=("$deploy_arg")
fi

if [[ "$clean" == "clean" ]]; then
    dotnet msbuild "${msbuild_args[@]}" /t:Clean
fi

dotnet msbuild "${msbuild_args[@]}" /t:Build

echo
if [[ -f "$output_dll" ]]; then
    echo "Build succeeded. Output: $output_dll"
else
    echo "Build succeeded. Output directory: $(dirname -- "$output_dll")"
fi

if [[ "$mode" == "smoke" ]]; then
    deploy_enabled="false"
    if [[ "$deploy_arg" == "/p:DeploySmoke=true" ]]; then
        deploy_enabled="true"
    elif [[ "$deploy_arg" != "/p:DeploySmoke=false" && "$config" == "Debug" ]]; then
        deploy_enabled="true"
    fi

    if [[ "$deploy_enabled" == "true" ]]; then
        echo "Smoke deploy path: $VALHEIM_DIR/BepInEx/plugins/ModUtilsSmoke"
    fi
fi
