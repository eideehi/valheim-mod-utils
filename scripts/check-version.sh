#!/usr/bin/env bash
#
# Check that version fields agree and CHANGELOG tracks the latest release tag.
#
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &>/dev/null && pwd)"
repo_root="$(cd -- "$script_dir/.." &>/dev/null && pwd)"

assembly_info="$repo_root/ModUtils/Properties/AssemblyInfo.cs"
changelog="$repo_root/CHANGELOG.md"

read_required_value() {
    local label="$1"
    local value="$2"

    if [[ -z "$value" ]]; then
        echo "error: could not read $label" >&2
        exit 1
    fi

    if [[ "$value" == *$'\n'* ]]; then
        echo "error: found multiple values for $label" >&2
        exit 1
    fi

    printf '%s' "$value"
}

read_latest_release_tag() {
    local tag

    tag="$(git -C "$repo_root" tag --list '[0-9]*.[0-9]*.[0-9]*' --sort=version:refname | tail -n 1)"
    if [[ -z "$tag" ]]; then
        echo "error: could not read latest release tag" >&2
        exit 1
    fi

    printf '%s' "$tag"
}

assembly_version="$(
    sed -nE 's/^\[assembly: AssemblyVersion\("([0-9]+\.[0-9]+\.[0-9]+)\.0"\)\]/\1/p' "$assembly_info"
)"
file_version="$(
    sed -nE 's/^\[assembly: AssemblyFileVersion\("([0-9]+\.[0-9]+\.[0-9]+)\.0"\)\]/\1/p' "$assembly_info"
)"
changelog_version="$(
    sed -nE 's/^#### v([0-9]+\.[0-9]+\.[0-9]+) \[[0-9]{4}-[0-9]{2}-[0-9]{2}\]$/\1/p' "$changelog" | head -n 1
)"

assembly_version="$(read_required_value "AssemblyVersion" "$assembly_version")"
file_version="$(read_required_value "AssemblyFileVersion" "$file_version")"
changelog_version="$(read_required_value "latest CHANGELOG version" "$changelog_version")"
latest_release_tag="$(read_latest_release_tag)"

if [[ "$assembly_version" != "$file_version" ]]; then
    echo "error: AssemblyVersion ($assembly_version) does not match AssemblyFileVersion ($file_version)" >&2
    exit 1
fi

if [[ "$changelog_version" != "$latest_release_tag" ]]; then
    echo "error: latest CHANGELOG version ($changelog_version) does not match latest release tag ($latest_release_tag)" >&2
    exit 1
fi

echo "Version metadata matches $assembly_version. CHANGELOG uses latest release tag $latest_release_tag."
