#!/usr/bin/env bash
set -euo pipefail

# Run from any directory. Uploading is explicit; the default only previews files.
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
selection="${1:-all}"
mode="${2:---dry-run}"
case "$selection" in
  rpg|strategy|all) ;;
  *) echo "Usage: $0 [rpg|strategy|all] [--dry-run|--push]" >&2; exit 2 ;;
esac
case "$mode" in
  --dry-run) flags=(--dry-run) ;;
  --push) flags=() ;;
  *) echo "Expected --dry-run or --push" >&2; exit 2 ;;
esac
if (( $# > 2 )); then echo "Too many arguments" >&2; exit 2; fi
command -v butler >/dev/null || { echo "Install itch.io butler first." >&2; exit 1; }

# Check every selected archive before uploading either game.
archives=()
targets=()
if [[ "$selection" == rpg || "$selection" == all ]]; then
  archives+=("$repo_root/Builds/IdleRpg-itch.zip")
  targets+=("thisisnozaku/idle-rpg:html5")
fi
if [[ "$selection" == strategy || "$selection" == all ]]; then
  archives+=("$repo_root/Builds/IdleStrategy-itch.zip")
  targets+=("thisisnozaku/idle-strategy:html5")
fi
for archive in "${archives[@]}"; do
  [[ -f "$archive" ]] || { echo "Missing $archive. Run SampleGamesBuild.AllWebGL first." >&2; exit 1; }
done
for index in "${!archives[@]}"; do
  echo "${archives[$index]} -> ${targets[$index]} ($mode)"
  butler push "${archives[$index]}" "${targets[$index]}" "${flags[@]}"
done
