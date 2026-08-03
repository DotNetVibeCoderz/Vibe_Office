# Troubleshooting

*AutoWork — Gravicode Studios, led by Kang Fadhil*

## Start here

Two places tell you most of what you need:

- **Activity** — every action AutoWork took, including refused ones, with the reason.
- `logs/actions.jsonl` in your data folder — the same thing, greppable.

An action with outcome `Denied` means a permission stopped it, and the `error` field says which.

---

## The app will not start

**Nothing happens when launching.** Run it from a terminal to see the error:

```bash
dotnet run --project src/AutoWork.Desktop/AutoWork.Desktop.csproj
```

**"You must install .NET to run this application."** Install the
[.NET 10 runtime or SDK](https://dotnet.microsoft.com/download).

**Text renders as boxes (Linux).** Install `fontconfig`.

**It opens on Settings every time.** That is deliberate when no model is configured. Add one and
it will open on Work.

---

## Models

**"No model is configured yet."** Settings › Models → Add model → pick a provider → paste a key.

**"The API key was rejected."** The key is wrong, expired, or for a different provider. If the
box shows `••••••••`, a key is stored — retype it to replace it.

**"The endpoint responded 404."** Usually the model id rather than the URL. Preset model ids are
starting points and vendors retire them; check the id against your provider's current list. Also
confirm the endpoint includes the version segment where the vendor expects it, e.g.
`https://api.openai.com/v1`.

**"Could not reach the endpoint."** For local providers, confirm the server is running:

```bash
curl http://localhost:11434/api/tags      # Ollama
```

**Ollama connects but the agent does nothing.** The model must support tool calling. Many small
models do not. Try a model documented as supporting tools, and confirm **Tools** is ticked under
Capabilities.

**"Rate limited by the provider."** Wait, or lower `maxParallelSubAgents` to 1.

---

## Permissions

**Everything is refused.** No folders are granted. Settings › Permissions → Add folder.

**"…is outside every granted folder."** The path is not inside a grant. Note that grants are
compared by path segment, so `~/Documents` does not cover `~/Documents-backup`.

**"…matches a blocked pattern."** The default denied list blocks credential-shaped files
(`.env`, `*.pem`, `.ssh/**`). Edit the list in Settings if a legitimate file is caught.

**"…is granted as read-only."** Change that folder to read/write.

**"Deleting is turned off."** Enable *Allow deleting files*. Leave *soft delete* on so deletions
stay recoverable.

**A symlink inside a granted folder is refused.** Working as designed — links are resolved to
their target, and a target outside your grants is out of reach. Grant the target folder if you
meant to allow it.

**AutoWork cannot see its own data folder.** Also by design. That folder holds the secret store
and is permanently off-limits.

---

## Files

**"already exists."** File writes do not overwrite by default. Tell the agent to replace it and
it will pass `overwrite=true`.

**Batch rename refuses with a collision message.** Two files would end up with the same name.
Nothing was moved — a half-applied batch rename is worse than a refused one. Adjust the pattern.

**"…is over the read limit."** Default is 32 MB. Raise `maxReadBytes` in `config.json`, or have
the agent use the data tools, which stream instead of loading whole files.

**"looks like a binary file."** Use `data_extract_pdf`, `data_read_csv`, `doc_read_excel` or the
image tools instead of `files_read`.

**Where did my deleted files go?** `recycle/<date>/` in your data folder, when soft delete is on.

---

## Documents

**Excel formulas show as text.** Cell values starting with `=` become formulas automatically.
If a value was meant to be literal text, it should not start with `=`.

**The generated file will not open.** Please report it — the test suite runs the official OpenXML
validator over generated Word and PowerPoint files, so a malformed file is a genuine bug.

**PDF extraction returns almost nothing.** The PDF is a scan with no text layer. AutoWork says so
explicitly. Capture it with the screen tools and read it with a vision model instead.

---

## Screen and input

**"Screen capture is turned off."** Enable it in Settings › Permissions.

**Screen capture fails on Linux.** Install a helper: `grim` (Wayland), `gnome-screenshot`,
`spectacle`, `imagemagick` or `scrot`. On Wayland the compositor may also need to grant
permission.

**Screen capture fails on macOS.** Grant **Screen Recording** under System Settings › Privacy &
Security, then restart AutoWork.

**"No vision-capable model is configured."** `screen_look` needs a vision model. Add one, tick
**Vision** under its capabilities, and select it as the vision model.

**Input control does nothing.** It is off by default. On macOS, install `cliclick` and grant
**Accessibility**. On Linux, install `xdotool` (X11). On Windows, note that synthetic input
cannot reach a window running with higher privileges than AutoWork.

---

## Agent behaviour

**It stops early.** Either `maxSteps` was reached or three steps failed in a row. The Activity
log shows which.

**It says it did something it did not do.** Enable *Check my own work before reporting success*
in Settings › Agent. The verification pass judges on tool results rather than the model's account.

**It keeps asking for approval.** Use *Allow for this run* where offered. It is deliberately not
offered for deletes or shell commands.

**"Context compacted" appears mid-run.** Normal — the conversation approached the window limit
and older turns were summarised. If it loses its footing afterwards, raise
`compactKeepRecentTurns`, or lower `autoCompactThreshold` so compaction happens earlier and more
gently.

**Runs are slow.** Enable sub-agents, use a faster model for the executor role while keeping a
strong one for planning, or run locally with Ollama.

---

## Data and recovery

**Reset everything.** Close AutoWork and delete the data folder — `%APPDATA%\AutoWork`,
`~/.config/AutoWork` or `~/Library/Application Support/AutoWork`. Your API keys go with it.

**Settings will not save.** Check the data folder is writable and has space.

**A corrupt config.** AutoWork moves it aside as `config.json.broken-<timestamp>` and starts with
defaults rather than refusing to launch.

**Move everything elsewhere.** Set `AUTOWORK_HOME` to a new path.

---

## Reporting a bug

Include:

1. What you asked AutoWork to do
2. What happened instead
3. The relevant lines from Activity or `logs/actions.jsonl`
4. Your OS, and the provider and model id
5. `dotnet --list-sdks`

Please do not paste API keys. Log entries do not contain them, but the config path might appear.
