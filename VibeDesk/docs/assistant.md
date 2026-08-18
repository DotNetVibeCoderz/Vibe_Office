# Mr Clippy

[← README](../README.md) · [Architecture](architecture.md) · [Configuration](configuration.md) · [Apps](apps.md) · [Scripts](scripting.md) · [Development](development.md)

An assistant panel present in all five apps, grounded in whatever the user currently has open.

---

## What the user gets

- Multiple conversations, created, renamed, reset or deleted independently
- Provider, model, temperature and system prompt overridable **per conversation**
- Image and document attachments
- Markdown replies rendered to HTML — tables, code, media
- A badge for every tool the assistant actually invoked, so "I searched the web" is auditable rather
  than a claim in the prose
- Hide/show, and it degrades to an explanation of what to configure when no provider is set up

---

## Two backends, one kernel

```
IClippyService (ClippyService)
   ├── SemanticKernelBackend  → OpenAI · Google Gemini · Ollama
   └── AnthropicBackend       → Claude
              ↕
        one Kernel, one set of KernelFunctions
```

Semantic Kernel abstracts chat completion, but its automatic function invocation is implemented **per
connector**, and there is no Anthropic connector at all. Rather than pretend one abstraction covers
both, the two backends share the same `Kernel` — the tools a model can call are literally the same
`KernelFunction` objects either way — and differ only in how the tool loop is driven.

- **`SemanticKernelBackend`** lets the connector run the loop (`FunctionChoiceBehavior.Auto()`) and
  relays chunks. Tool calls are captured with an `IFunctionInvocationFilter`, because the invocation
  happens inside the connector and there is nowhere else to see it.
- **`AnthropicBackend`** is written on the official `Anthropic` SDK and runs its own loop.

### Why the Anthropic loop is not streamed

Reconstructing a tool call from a token stream means accumulating partial JSON across
`input_json_delta` events. A half-parsed tool call is a silently *wrong* action rather than a visibly
failed one, so the loop runs non-streaming and the finished reply is handed back in small slices — the
panel still renders progressively.

### Provider quirks that are handled for you

- **No `temperature` for Anthropic.** Removed from every model after Claude Opus 4.6; sending it now
  returns 400. The configured value still applies to the other three providers.
- **Refusals arrive as HTTP 200.** `stop_reason: "refusal"` is checked before anything reads the
  content list, so a declined request produces a clear message instead of an index-out-of-range.
- Execution settings are built as base `PromptExecutionSettings` with `ExtensionData`, so each
  connector deserialises what it understands and the project never references an alpha connector's
  option classes.

---

## The kernel functions

| Plugin | Function | Does |
|---|---|---|
| `time` | `current_datetime` | Now, in the user's zone |
| | `date_add` | Shift a date by N days |
| | `days_between` | Whole days between two dates |
| `math` | `calculate` | Evaluate a spreadsheet expression |
| `web` | `web_search` | Tavily search |
| | `read_web_page` | Fetch a page, strip scripts/nav, return text |
| | `read_file_from_url` | Download a file and return its text |
| `workspace` | `search_drive` | Search the user's Drive |
| | `read_document` | Read a Doc/Sheet/Deck as plain text |
| | `get_open_document` | What the user is looking at right now |
| | `list_calendar_events` | Events in a date window |
| `authoring` | `create_document` | New document, body as HTML |
| | `append_to_document` | Add to the end, leaving the rest alone |
| | `replace_document_content` | Rewrite the body; the old text stays in version history |
| | `create_spreadsheet` | New sheet from CSV-style rows |
| | `append_spreadsheet_rows` | Add rows below what is already there |
| | `create_presentation` | New deck; slides separated by a blank line |
| | `add_slides` | Append slides to an existing deck |
| | `create_folder` | New folder |
| | `rename_item` | Rename a file or folder |
| | `move_item` | Move a file or folder |

`time` exists because without it the model dates everything from its training cutoff — wrong in a
calendar app in a way users notice immediately.

`math.calculate` **reuses `FormulaEngine`** — the same parser and the same ~120 functions that back
Sheets. `ROUND`, `SUMPRODUCT` and the date functions therefore behave in chat exactly as they do in a
cell, and there is only one place for a bug to live.

### The authoring plugin can create and change, never destroy

`authoring` is registered separately from `workspace` and behind `Assistant:AllowWorkspaceWrites`
(default `true`); set it false to keep the assistant strictly read-only.

**There is no delete, trash or empty-trash function at all** — not gated, not confirmed, simply absent.
A model that misreads "clear out the old drafts" can at worst leave a stray file. Renaming and moving
are offered because both are reversible; destroying is not offered, so there is no call to get wrong.

Everything else the read plugin guarantees still holds: no function takes a user id, and every call
goes through the same permission service the UI uses, so the assistant can only write where its user
could already write.

> **Nullable is not optional.** A parameter declared `string?` with no default is **required** in the
> generated tool schema, so a model that sensibly omits it gets its call refused and has to retry.
> That cost `create_presentation` two round trips before it was found. Every optional parameter now
> carries `= null`, and `KernelFunctionShapeTests` fails the build if a new one does not.
---

## Security boundaries

### The web tools are the SSRF surface

Every URL here originates from the model, which means it originates from whatever text the model has
read — including a document a stranger shared with the user. So each fetch is gated on **the resolved
address**, not the hostname: a name that looks external can still resolve to `127.0.0.1` or a cloud
metadata endpoint, and that is precisely the request an attacker plants.

Rejected: non-http(s) schemes, loopback, RFC1918 (`10/8`, `172.16/12`, `192.168/16`), `169.254/16`
(link-local and cloud metadata), and the IPv6 equivalents.

### The workspace tools cannot reach another user's files

`WorkspacePlugin` is read-only and takes **no user-id parameter at all** — that is the point. Every
call goes through `IDriveService` / `IDocumentContentService`, which resolve permissions for the
signed-in user, so there is no argument the model could set to reach someone else's files.

### Kernels are built per request, never cached

The plugins close over the signed-in user's services and the document currently open. Caching a kernel
would cache one user's access — the one bug in this area that actually matters.

### Attachments

Stored under `chat/{userId:N}/…` and served by a route restricted to that prefix. Images are inlined
to the model as bytes so vision-capable models can see them; documents carry extracted text so no
second fetch is needed. Markdown rendering runs with raw HTML disabled and a URL scheme allow-list.

---

## Limits

- `MaxToolIterations` (default 6) caps tool round trips; on the last iteration the tools are withheld
  so the model has to produce prose rather than looping.
- `MaxToolResultChars` (default 12 000) truncates what any tool can pull into context.
- `MaxHistoryTurns` (default 20) replays recent turns; older ones are dropped, not summarised.
- Function-calling support varies by provider — the Ollama and Gemini connectors are still marked
  experimental by Semantic Kernel. Where a provider ignores tools, the assistant simply answers
  without them.
