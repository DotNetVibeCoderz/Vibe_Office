#!/usr/bin/env bash
#
# Removes AutoWork from Linux or macOS.
#
# Program files and launchers go; your configuration, API keys, knowledge bases and logs stay
# unless you pass --purge-data. Losing an API key to an uninstall is a nasty surprise.

set -euo pipefail

INSTALL_DIR="${AUTOWORK_INSTALL_DIR:-$HOME/.local/share/autowork}"
BIN_DIR="${AUTOWORK_BIN_DIR:-$HOME/.local/bin}"
PURGE=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --prefix)      INSTALL_DIR="$2"; shift 2 ;;
    --bin-dir)     BIN_DIR="$2"; shift 2 ;;
    --purge-data)  PURGE=1; shift ;;
    -h|--help)     echo "Usage: uninstall.sh [--prefix DIR] [--bin-dir DIR] [--purge-data]"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 1 ;;
  esac
done

GREEN=$'\033[32m'; DIM=$'\033[2m'; RESET=$'\033[0m'

printf '\n  Removing AutoWork\n\n'

pkill -f "$INSTALL_DIR/AutoWork" 2>/dev/null || true

if [[ -d "$INSTALL_DIR" ]]; then
  rm -rf "$INSTALL_DIR"
  printf '    %sRemoved %s%s\n' "$GREEN" "$INSTALL_DIR" "$RESET"
else
  printf '    %sNothing installed at %s%s\n' "$DIM" "$INSTALL_DIR" "$RESET"
fi

[[ -f "$BIN_DIR/autowork" ]] && rm -f "$BIN_DIR/autowork" && \
  printf '    %sRemoved launcher%s\n' "$GREEN" "$RESET"

DESKTOP_ENTRY="$HOME/.local/share/applications/autowork.desktop"
[[ -f "$DESKTOP_ENTRY" ]] && rm -f "$DESKTOP_ENTRY" && \
  printf '    %sRemoved desktop entry%s\n' "$GREEN" "$RESET"

if [[ "$(uname -s)" == "Darwin" ]]; then
  DATA_DIR="$HOME/Library/Application Support/AutoWork"
else
  DATA_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/AutoWork"
fi

if [[ "$PURGE" -eq 1 ]]; then
  [[ -d "$DATA_DIR" ]] && rm -rf "$DATA_DIR" && \
    printf '    %sRemoved %s (config, keys, knowledge, logs)%s\n' "$GREEN" "$DATA_DIR" "$RESET"
elif [[ -d "$DATA_DIR" ]]; then
  printf '\n  Your data is still at %s\n' "$DATA_DIR"
  printf '  %sRun again with --purge-data to delete it as well.%s\n' "$DIM" "$RESET"
fi

printf '\n  %sDone.%s\n\n' "$GREEN" "$RESET"
