# Installation

*AutoWork — Gravicode Studios, led by Kang Fadhil*

## Requirements

| | Minimum |
|---|---|
| OS | Windows 10 1809+, macOS 12+, or a Linux desktop with X11 or Wayland |
| .NET | [.NET 10 SDK](https://dotnet.microsoft.com/download) |
| Disk | ~250 MB for the app, plus whatever your knowledge bases grow to |
| RAM | 4 GB. Running models locally with Ollama needs considerably more. |
| Network | Only for hosted providers and integrations. With Ollama, AutoWork works fully offline. |

Check your SDK:

```bash
dotnet --list-sdks
```

You need a line starting with `10.`.

## Windows

```powershell
git clone https://github.com/gravicode/autowork.git
cd autowork\install
.\install.ps1 -Desktop
```

Options:

| Flag | Effect |
|---|---|
| `-Desktop` | Also create a Desktop shortcut |
| `-InstallDir <path>` | Install somewhere other than `%LOCALAPPDATA%\Programs\AutoWork` |
| `-Runtime win-arm64` | Build for ARM64 |
| `-SkipShortcuts` | Do not create shortcuts |

If PowerShell blocks the script:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

That relaxes the policy for this one session only.

## macOS and Linux

```bash
git clone https://github.com/gravicode/autowork.git
cd autowork/install
chmod +x install.sh
./install.sh
```

Options:

| Flag | Effect |
|---|---|
| `--prefix <dir>` | Install somewhere other than `~/.local/share/autowork` |
| `--bin-dir <dir>` | Put the launcher somewhere other than `~/.local/bin` |
| `--skip-launcher` | Do not create a launcher or desktop entry |

**Linux extras.** Install `fontconfig` if text renders as boxes. For screen capture, install one
of `grim` (Wayland), `gnome-screenshot`, `spectacle`, `imagemagick` or `scrot`. For input
control, install `xdotool` (X11).

**macOS extras.** Screen capture and input control both require permission under
**System Settings › Privacy & Security**: *Screen Recording* and *Accessibility* respectively.
For input control also install `cliclick` (`brew install cliclick`).

## Running without installing

```bash
dotnet run --project src/AutoWork.Desktop/AutoWork.Desktop.csproj
```

## Where things go

| | Windows | macOS | Linux |
|---|---|---|---|
| Program | `%LOCALAPPDATA%\Programs\AutoWork` | `~/.local/share/autowork` | `~/.local/share/autowork` |
| Your data | `%APPDATA%\AutoWork` | `~/Library/Application Support/AutoWork` | `~/.config/AutoWork` |

Inside the data folder:

```
config.json        Settings. Contains no API keys — only references to them.
secrets.json       Encrypted API keys, plus secrets.json.key.
logs/              Action log as JSON Lines, rotated at 8 MB.
knowledge/         One JSON file per knowledge base.
screenshots/       Captures taken by the Eyes subsystem.
recycle/           Soft-deleted files, recoverable.
```

Set `AUTOWORK_HOME` to move all of this — useful for a portable install on a USB drive.

**Your working folder is separate**, at `Documents\AutoWork` (`~/Documents/AutoWork` on macOS and
Linux). It is what the starter permission policy grants and where the agent puts files when a job
names no path. It sits outside the data folder on purpose: everything under the data folder is an
AutoWork-internal location that `PathGuard` refuses outright, and uninstalling must not take your
work with it. Move it with `AUTOWORK_WORKSPACE`.

## Upgrading

Re-run the installer. It stops a running copy, replaces the program files, and leaves your data
folder untouched.

## Uninstalling

```powershell
.\install\uninstall.ps1              # keeps your data
.\install\uninstall.ps1 -PurgeData   # deletes it as well
```

```bash
./install/uninstall.sh
./install/uninstall.sh --purge-data
```

Your API keys live in the data folder, so the default keeps them. Pass the purge flag only when
you mean it.

## After installing

1. **Settings › Models** — add a provider and paste a key. See
   [configuration.md](configuration.md).
2. **Settings › Permissions** — grant a folder. Until you do, AutoWork can reach nothing at all;
   this is intentional, and explained in [security.md](security.md).
3. **Work** — describe a job.

If a vendor key is already exported in your environment, AutoWork detects it on first launch and
step 1 is already done for you.
