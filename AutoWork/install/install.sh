#!/usr/bin/env bash
#
# AutoWork installer for Linux and macOS.
#
# Builds from source, installs under ~/.local/share/autowork, and puts a launcher on PATH.
# Re-running upgrades in place; your data in ~/.config/AutoWork is never touched.
#
# AutoWork — Gravicode Studios, led by Kang Fadhil.

set -euo pipefail

INSTALL_DIR="${AUTOWORK_INSTALL_DIR:-$HOME/.local/share/autowork}"
BIN_DIR="${AUTOWORK_BIN_DIR:-$HOME/.local/bin}"
SKIP_LAUNCHER=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --prefix)         INSTALL_DIR="$2"; shift 2 ;;
    --bin-dir)        BIN_DIR="$2"; shift 2 ;;
    --skip-launcher)  SKIP_LAUNCHER=1; shift ;;
    -h|--help)
      cat <<'USAGE'
AutoWork installer

  --prefix DIR         Where to install (default: ~/.local/share/autowork)
  --bin-dir DIR        Where to put the launcher (default: ~/.local/bin)
  --skip-launcher      Do not create a launcher or desktop entry
  -h, --help           Show this help
USAGE
      exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 1 ;;
  esac
done

BOLD=$'\033[1m'; DIM=$'\033[2m'; GREEN=$'\033[32m'; CYAN=$'\033[36m'; YELLOW=$'\033[33m'; RESET=$'\033[0m'

step() { printf '%s==>%s %s\n' "$CYAN" "$RESET" "$1"; }
ok()   { printf '    %s%s%s\n' "$GREEN" "$1" "$RESET"; }
warn() { printf '    %s%s%s\n' "$YELLOW" "$1" "$RESET"; }
die()  { printf '\n%sError:%s %s\n\n' "$YELLOW" "$RESET" "$1" >&2; exit 1; }

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
PROJECT="$ROOT_DIR/src/AutoWork.Desktop/AutoWork.Desktop.csproj"

printf '\n  %sAutoWork installer%s\n' "$BOLD" "$RESET"
printf '  %sGravicode Studios — led by Kang Fadhil%s\n\n' "$DIM" "$RESET"

# ── Prerequisites ────────────────────────────────────────────────────────────────────────
step "Checking prerequisites"

command -v dotnet >/dev/null 2>&1 || \
  die "The .NET SDK was not found. Install .NET 10 from https://dotnet.microsoft.com/download and run this again."

if ! dotnet --list-sdks | grep -q '^10\.'; then
  die "AutoWork needs the .NET 10 SDK. Installed:
$(dotnet --list-sdks)"
fi
ok ".NET 10 SDK found"

[[ -f "$PROJECT" ]] || die "Could not find $PROJECT. Run this from the repository's install directory."

case "$(uname -s)" in
  Linux)
    # Avalonia needs an ICU and fontconfig present; missing fonts is the usual first-run gripe.
    command -v fc-list >/dev/null 2>&1 || warn "fontconfig was not found. Install it if text renders as boxes."
    RID="linux-$( [[ "$(uname -m)" == "aarch64" ]] && echo arm64 || echo x64 )"
    ;;
  Darwin)
    RID="osx-$( [[ "$(uname -m)" == "arm64" ]] && echo arm64 || echo x64 )"
    ;;
  *) die "Unsupported platform: $(uname -s). AutoWork supports Windows, Linux and macOS." ;;
esac
ok "Target runtime: $RID"

# ── Build ────────────────────────────────────────────────────────────────────────────────
step "Building AutoWork (this takes a minute on a first run)"

STAGING="$(mktemp -d)"
trap 'rm -rf "$STAGING"' EXIT

dotnet publish "$PROJECT" \
  --configuration Release \
  --runtime "$RID" \
  --self-contained false \
  --output "$STAGING" \
  --nologo \
  --verbosity quiet || die "The build failed. See the output above."

ok "Build complete"

# ── Install ──────────────────────────────────────────────────────────────────────────────
step "Installing to $INSTALL_DIR"

pkill -f "$INSTALL_DIR/AutoWork" 2>/dev/null && warn "Closed a running AutoWork" || true

mkdir -p "$INSTALL_DIR"
rm -rf "${INSTALL_DIR:?}"/*
cp -R "$STAGING"/. "$INSTALL_DIR"/
chmod +x "$INSTALL_DIR/AutoWork" 2>/dev/null || true

[[ -f "$INSTALL_DIR/AutoWork" ]] || die "The build finished but the AutoWork binary is missing from $INSTALL_DIR."
ok "Files installed"

# ── Launcher ─────────────────────────────────────────────────────────────────────────────
if [[ "$SKIP_LAUNCHER" -eq 0 ]]; then
  step "Creating launcher"

  mkdir -p "$BIN_DIR"
  cat > "$BIN_DIR/autowork" <<EOF
#!/usr/bin/env bash
exec "$INSTALL_DIR/AutoWork" "\$@"
EOF
  chmod +x "$BIN_DIR/autowork"
  ok "Launcher at $BIN_DIR/autowork"

  case ":$PATH:" in
    *":$BIN_DIR:"*) : ;;
    *) warn "$BIN_DIR is not on your PATH. Add this to your shell profile:"
       printf '        export PATH="%s:$PATH"\n' "$BIN_DIR" ;;
  esac

  if [[ "$(uname -s)" == "Linux" ]]; then
    APPS_DIR="$HOME/.local/share/applications"
    mkdir -p "$APPS_DIR"
    cat > "$APPS_DIR/autowork.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=AutoWork
Comment=Your digital coworker
Exec=$INSTALL_DIR/AutoWork
Terminal=false
Categories=Office;Utility;
StartupWMClass=AutoWork
EOF
    update-desktop-database "$APPS_DIR" 2>/dev/null || true
    ok "Desktop entry created"
  fi
fi

# ── Done ─────────────────────────────────────────────────────────────────────────────────
DATA_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/AutoWork"
[[ "$(uname -s)" == "Darwin" ]] && DATA_DIR="$HOME/Library/Application Support/AutoWork"

printf '\n  %sInstalled.%s\n\n' "$GREEN" "$RESET"
printf '  Program:   %s\n' "$INSTALL_DIR"
printf '  Your data: %s\n\n' "$DATA_DIR"
printf '  Next: open AutoWork, go to Settings > Models and add a provider key,\n'
printf '        then Settings > Permissions and grant it a folder to work in.\n\n'
printf '  %sNothing on your computer is reachable until you grant a folder.%s\n\n' "$DIM" "$RESET"

if [[ -t 0 ]]; then
  read -r -p "  Start AutoWork now? [Y/n] " answer
  case "$answer" in
    ""|[Yy]*) "$INSTALL_DIR/AutoWork" >/dev/null 2>&1 & disown ;;
  esac
fi
