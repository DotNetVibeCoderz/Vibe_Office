# Configuration

*AutoWork — Gravicode Studios, led by Kang Fadhil*

Three layers, lowest precedence first:

1. **`config.json`** — what the app saves
2. **Environment variables** — applied on top at launch, never written back
3. **The app's UI** — what you change in Settings, which is saved to `config.json`

The middle layer never persists. Exporting `AUTOWORK_ALLOW_SHELL=false` for one launch does not
silently rewrite the policy you saved.

## Models

AutoWork talks to any provider you configure. Nearly all of them speak the OpenAI wire format,
so one code path covers most; Anthropic and Ollama have their own clients because their
protocols genuinely differ.

![Settings › Models](../images/settings-models.png)

### Built-in presets

| Preset | Endpoint | Environment variable |
|---|---|---|
| `openai` | `https://api.openai.com/v1` | `OPENAI_API_KEY` |
| `azure-openai` | your resource URL — see below | `AZURE_OPENAI_API_KEY` |
| `anthropic` | `https://api.anthropic.com/v1` | `ANTHROPIC_API_KEY` |
| `gemini` | `https://generativelanguage.googleapis.com/v1beta/openai/` | `GEMINI_API_KEY` |
| `ollama` | `http://localhost:11434` | `OLLAMA_HOST` |
| `deepseek` | `https://api.deepseek.com/v1` | `DEEPSEEK_API_KEY` |
| `qwen` | `https://dashscope-intl.aliyuncs.com/compatible-mode/v1` | `DASHSCOPE_API_KEY` |
| `moonshot` | `https://api.moonshot.ai/v1` | `MOONSHOT_API_KEY` |
| `openrouter` | `https://openrouter.ai/api/v1` | `OPENROUTER_API_KEY` |
| `lmstudio` | `http://localhost:1234/v1` | — |
| `custom` | you supply it | — |

> **Model ids move faster than this app ships.** The default model id in each preset is a
> starting point, not a supported-model list. Check the id against your provider's current
> catalogue and edit it in Settings. Anything with an OpenAI-compatible endpoint works through
> `custom` even if it is not listed here.

### Azure OpenAI

Azure gives every resource its own hostname and lets you name your own deployments, so it is the
one preset where nothing can be filled in for you.

Paste the endpoint exactly as the Azure portal shows it:

```
https://your-resource.openai.azure.com/
```

AutoWork completes it to `/openai/v1`, Azure's OpenAI-compatible surface. If you prefer, you can
type the full path yourself — including the older `/openai/deployments/…` form — and it is left
as you typed it.

The **model id is your deployment name**, not the underlying model name. They often match, but
they do not have to.

Reasoning deployments — the gpt-5 family, the o-series — reject a custom `temperature` and reject
`max_tokens`, and they spend their output budget thinking before they write anything. AutoWork
detects all three from the provider's own reply and adapts, so no setting is needed. After a
connection test, Settings tells you which of your settings the model would not accept.

### Model roles

Four jobs, each assignable to a different model:

| Role | Used for | Needs |
|---|---|---|
| Planning and reasoning | Building the plan, summarising, verifying | Tool support |
| Doing the work | Executing steps and calling tools | Tool support |
| Reading the screen | `screen_look`, `image_describe` | Vision |
| Knowledge search | Embedding knowledge entries and queries | Embeddings |

Leaving a role unset picks the first enabled model with the right capability. A common setup is
a strong model for planning and a cheap fast one for execution.

### API keys

Keys are **never** written to `config.json`. The file stores a reference:

- a name in the encrypted secret store, or
- `env:VARIABLE_NAME`, read from the environment at call time and never persisted at all

That means `config.json` is safe to copy between machines or commit to a private repo.

To use an external secret manager, type `env:MY_VAR` into the API key box and inject `MY_VAR`
however you like.

## Skills

Skills are `SKILL.md` files — YAML front matter carrying a name and a description of when the
skill applies, followed by instructions in Markdown. AutoWork reads them from GitHub
repositories you list in **Skills › Repositories**, seeded with `anthropics/skills` and
`obra/superpowers`.

Add any public repository as `owner/name` or a GitHub URL; a company's internal skills repository
works the same way. The list is stored in `config.json` under `skillRepositories`, and installed
skills live in `skills/` inside the data folder.

A skill installs as its whole folder, not just the manifest: reference documents, templates,
schemas and scripts all come with it, up to 250 files and 40 MB. Anything skipped for size is
reported rather than dropped quietly.

Only a skill's name and description reach the model on every request. The body is fetched by
`skill_open` when the model judges it relevant, bundled files by `skill_file`, so installing a
dozen skills does not make every request a dozen times more expensive.

When a script needs libraries, AutoWork installs them into a virtual environment inside the
skill's own folder: whatever `requirements.txt` declares, plus imports it can resolve through a
fixed name table. An import it does not recognise is reported rather than guessed at. The package
list appears in the approval card before anything is fetched, and installing happens once per
skill.

Scripts are run by `skill_run`, which exists only when
**Settings › Permissions › Allow running scripts bundled with skills** is on. It is off by
default and asks before every execution — see
[security.md](security.md#skills-instructions-and-sometimes-code).

## MCP servers

**Settings › Permissions › Allow MCP servers** must be on; it is off by default because a stdio
server is a program started with your full rights. See
[security.md](security.md#mcp-servers-are-outside-the-sandbox).

Servers are stored in `config.json` under `mcpServers`, each with a transport, a command line or
URL, and an environment map whose values are secret references rather than secrets:

```jsonc
{
  "id": "a1b2c3d4",
  "name": "Tavily Search",
  "catalogId": "tavily",
  "transport": "Stdio",
  "command": "npx",
  "arguments": ["-y", "tavily-mcp"],
  "environment": { "TAVILY_API_KEY": "mcp.a1b2c3d4.TAVILY_API_KEY" },
  "enabled": false
}
```

The built-in catalogue covers filesystem, memory, sequential thinking, Playwright, Context7,
Tavily, Firecrawl, Notion, the protocol's reference server, and `mcp-remote` for hosted servers.
Anything else can be added by hand. Most need Node.js on PATH, which the gallery states per entry.

Servers are connected once at the start of a run, not at launch — starting `npx` takes seconds
and the window should not wait for it. A server that cannot be reached is logged and skipped; the
run continues without its tools.

## Environment variables

### Provider keys

Export any vendor key and AutoWork creates a matching model on first launch:

```bash
export ANTHROPIC_API_KEY=sk-ant-...
export OPENAI_API_KEY=sk-...
export OLLAMA_HOST=http://localhost:11434
```

A model you have already configured for that provider is never overwritten.

Azure needs three, because a key alone does not say where to send it or what to ask for:

```bash
export AZURE_OPENAI_API_KEY=...
export AZURE_OPENAI_ENDPOINT=https://your-resource.openai.azure.com/
export AZURE_OPENAI_DEPLOYMENT=gpt-5-mini
```

With the endpoint missing, no Azure model is created at all — an uncallable model would be
picked as the default planner and fail every run.

### Web search

```bash
export TAVILY_API_KEY=tvly-...
```

Optional. Search works without it, falling back to DuckDuckGo and then Wikipedia; a key buys
better results and a synthesised answer. Set it here, or in Settings › Permissions, where it is
stored encrypted like any model key.

![The web-search key sits under the network switch in Settings › Permissions](../images/settings-search.png)

### Defining one model outright

For containers and CI:

```bash
export AUTOWORK_MODEL=llama3.3:70b
export AUTOWORK_ENDPOINT=http://gpu-box.internal:11434/v1
export AUTOWORK_PROVIDER=custom          # a preset id, or "custom"
export AUTOWORK_API_KEY=...              # optional
export AUTOWORK_CONTEXT_WINDOW=131072    # optional
```

This model is created and selected as the planner, overriding whatever was saved.

### Permissions

```bash
export AUTOWORK_FOLDERS="$HOME/Documents;$HOME/Downloads"   # read/write grants
export AUTOWORK_FOLDERS_READONLY="$HOME/Reference"          # read-only grants
export AUTOWORK_ALLOW_SHELL=false
export AUTOWORK_ALLOW_DELETE=true
export AUTOWORK_ALLOW_INPUT=false
export AUTOWORK_ALLOW_SCREEN=true
```

Separate multiple folders with `;` or `,`.

### Agent behaviour

The same settings live in **Settings › Agent**:

![Settings › Agent](../images/settings-agent.png)

```bash
export AUTOWORK_MAX_STEPS=40
export AUTOWORK_AUTO_COMPACT=true
export AUTOWORK_COMPACT_THRESHOLD=0.75   # compact at 75% of the context window
```

### Appearance and location

```bash
export AUTOWORK_THEME=Dark        # System | Light | Dark
export AUTOWORK_LANGUAGE=Indonesian   # System | English | Indonesian
export AUTOWORK_HOME=/media/usb/autowork   # move the whole data folder
```

## config.json

Annotated excerpt:

```jsonc
{
  "schemaVersion": 1,
  "models": [
    {
      "id": "a1b2c3d4",
      "displayName": "Anthropic — claude-sonnet-5",
      "preset": "anthropic",
      "kind": "Anthropic",
      "endpoint": "https://api.anthropic.com/v1",
      "modelId": "claude-sonnet-5",
      "apiKeyRef": "env:ANTHROPIC_API_KEY",   // reference, never the key
      "contextWindow": 200000,
      "maxOutputTokens": 8192,
      "temperature": 0.2,
      "capabilities": "Tools, Vision, Reasoning",
      "enabled": true,

      // Optional, and empty by default. Fill both in from your provider's pricing page and
      // each run reports what it cost; leave them and it reports tokens only.
      "inputPricePerMillion": 3.00,
      "outputPricePerMillion": 15.00,
      "currency": "USD"
    }
  ],
  "agent": {
    "plannerModelId": "a1b2c3d4",
    "maxSteps": 40,
    "maxConsecutiveFailures": 3,
    "maxParallelSubAgents": 3,
    "enableSubAgents": true,
    "enableSelfVerification": true,
    "enableAutoCompact": true,
    "autoCompactThreshold": 0.75,
    "compactKeepRecentTurns": 6,
    "toolTimeoutSeconds": 120
  },
  "permissions": {
    "roots": [
      { "path": "/home/fadhil/Documents", "access": "ReadWrite", "includeSubfolders": true }
    ],
    "deniedPatterns": ["**/.ssh/**", "**/*.pem", "**/.env"],
    "allowDelete": true,
    "softDelete": true,
    "allowShell": false,
    "allowNetwork": true,
    "allowScreenCapture": true,
    "allowInputControl": false,
    "maxReadBytes": 33554432,
    "maxBatchSize": 500,

    // Standing answers to consent prompts. Empty by default.
    "approvalRules": [
      { "effect": "Allow", "kind": "WriteFiles", "path": "/home/fadhil/Projects" },
      { "effect": "Deny",  "kind": "DeleteFiles" }
    ]
  },
  "appearance": { "theme": "System", "language": "System", "reduceMotion": false },

  "keepRunHistory": true,          // record each run so it can be reopened later
  "runHistoryRetentionDays": 30    // older runs are removed after each run finishes
}
```

Editing this file by hand is fine — AutoWork reads it at launch. A corrupt file is moved aside
as `config.json.broken-<timestamp>` and defaults are used rather than blocking startup.

## Agent tuning

| Setting | What it does | When to change it |
|---|---|---|
| `maxSteps` | Hard ceiling on steps per run | Raise for very long jobs; lower to bound cost |
| `maxConsecutiveFailures` | Failures in a row before giving up | Lower if you would rather it stopped early |
| `enableSubAgents` | Run independent steps in parallel | Turn off if a provider rate-limits you |
| `enableSelfVerification` | Check the work before reporting success | Costs one extra call; worth it |
| `autoCompactThreshold` | Fraction of the window that triggers compaction | Lower for models that degrade when nearly full |
| `compactKeepRecentTurns` | Turns kept verbatim when compacting | Raise if it loses its footing after a compaction |
| `toolTimeoutSeconds` | Per-tool-call wall clock limit | Raise for slow shell commands |

## Run history

Every run is written to `runs/` under the AutoWork data folder as two files: a small headline
that the History list reads, and the full transcript, loaded only when you open a run. Opening a
past run replays it onto the Work Tape exactly as it looked while it was happening.

| Setting | What it does | When to change it |
|---|---|---|
| `keepRunHistory` | Record each run | Turn off if you would rather nothing was written down |
| `runHistoryRetentionDays` | How long a run is kept | Shorten on a shared machine; lengthen if you refer back |

Transcripts contain whatever the run saw — file contents a tool read, search results, the
model's replies. They are stored in plain JSON alongside your other AutoWork data, protected by
the same file permissions and nothing more. Individual tool results are capped at about 4,000
characters so one run that read a large folder does not leave megabytes behind.

Pruning happens when a run finishes, so turning the retention down takes effect on the next run
rather than immediately. Deleting a run from the History page removes both its files at once.

## Approval rules

Rules answer a class of consent prompt once instead of every time. Each has an `effect`
(`Allow` or `Deny`), an optional `kind`, and a `path`.

```jsonc
"approvalRules": [
  // Stop asking about writes inside one project tree.
  { "effect": "Allow", "kind": "WriteFiles", "path": "/home/fadhil/Projects" },

  // Refuse every delete, everywhere, without asking.
  { "effect": "Deny", "kind": "DeleteFiles" },

  // Refuse anything at all inside one folder.
  { "effect": "Deny", "path": "/home/fadhil/Archive" }
]
```

Four constraints apply, and they are enforced rather than advisory:

- **Deny wins** over allow, in any order, and over an "allow for this run" clicked earlier.
- **A rule never widens what is permitted.** An allowed action still goes through the sandbox, so
  an allow rule outside your granted folders changes nothing.
- **An allow must have both a `path` and a `kind`.** One without them is ignored.
- **Only `WriteFiles` and `DeleteFiles` can be allowed.** `RunCommand`, `ControlInput`,
  `CaptureScreen` and `NetworkAccess` have no folder to be scoped to, so they stay per-action —
  their capability switches in Permissions are where that choice lives. Denies may use any kind.

Paths are matched at a folder boundary, so a rule for `/home/fadhil/Proj` does not cover
`/home/fadhil/Proj-private`.

## Model pricing

`inputPricePerMillion` and `outputPricePerMillion` are empty unless you fill them in, and
AutoWork ships no price table. That is deliberate: prices move faster than model ids, and a stale
built-in figure quietly under-reporting what a run cost is worse than reporting no cost at all.

With both set, each run reports a cost in `currency` alongside its token count. With either
missing, it reports tokens only. If a provider answers some calls without saying what they used,
the total is shown as "at least *n*" rather than as an exact figure.

## Saved jobs

Jobs live in `jobs.json` beside `config.json`. Each has a goal, a trigger, and an on/off switch
that starts off.

```jsonc
[
  {
    "name": "Monday invoices",
    "goal": "Summarise last week's invoices into a Word document",
    "trigger": "Schedule",
    "enabled": true,
    "period": "Weekly",
    "dayOfWeek": "Monday",
    "timeOfDay": "08:30:00"
  },
  {
    "name": "File new scans",
    "goal": "Sort anything new in the Scans folder by date",
    "trigger": "FolderChange",
    "enabled": true,
    "watchFolder": "/home/fadhil/Scans",
    "watchFilter": "*.pdf",
    "quietSeconds": 20
  }
]
```

A job runs in the Work view exactly as if you had typed it, so it asks for the same permissions.
Three things are worth knowing:

- **A folder trigger can only watch a folder you have granted.** One pointing anywhere else is
  reported and ignored rather than watched.
- **Jobs do not queue.** One coming due while another run is going is skipped, and says so.
- **Missed time does not accumulate.** Due times come from the clock, so an app closed over the
  weekend runs once when it next opens, not three times.

## Meetings and recordings

Off until configured, and local unless you say otherwise. AutoWork ships no speech model; point
it at one you have.

```jsonc
"transcription": {
  "mode": "Local",                 // Off | Local | Remote
  "command": "whisper-cli",
  "arguments": "-m {model} -f {audio} --output-txt --no-prints",
  "modelPath": "C:/models/ggml-base.en.bin",
  "language": ""
}
```

`{audio}`, `{model}` and `{language}` are filled in. The template is split into arguments
*before* substitution, so a recording whose path contains a space stays one argument.
whisper.cpp, faster-whisper and openai-whisper all work; each wants its own arguments. Output is
read from the command's own output or from a `.txt`/`.srt` written beside the recording.

`"mode": "Remote"` uploads the recording to a Whisper-compatible endpoint. It is never the
default, it is a separate consent prompt, and it is worth remembering that a meeting recording
contains people who never agreed to anything.

## A signed-in browser

```jsonc
"browser": {
  "enabled": false,
  "executablePath": "",     // empty finds Edge or Chrome
  "headless": false
}
```

Off by default and **separate from `permissions.allowNetwork`** — fetching a public page and
acting as the signed-in user are not the same permission. Both must be on for the browser tools
to appear at all.

The profile lives in `browser-profile/` under the AutoWork data folder, not your real browser
profile: attaching to the browser you have open would fight it for the profile lock. It persists,
so you sign in once and stay signed in. Navigation and clicks follow the same
`permissions.networkAllowList` as the web tools, and each asks first.

## Deleted files

With `permissions.softDelete` on (the default), deletions move into `recycle/` under the AutoWork
data folder along with an index recording where each came from. The Recycle page lists them and
puts them back. Nothing can be restored into AutoWork's own folder, and restoring over an
existing file needs a second, explicit confirmation.

Items recycled by builds before the index existed are listed as unrestorable rather than hidden —
they still take up space, and you may still want to clear them.
