# AutoWork — Development progress / Progres pengembangan

*Gravicode Studios — led by Kang Fadhil*
*Last updated / Terakhir diperbarui: 3 August 2026*
*Latest / Terbaru: v0.3 complete — documents, jobs, streaming, meetings, browser*

Tracking document for [PLAN.md](PLAN.md). Verified means it is covered by a passing test or was
exercised by running the app — not merely that the code compiles.

*Dokumen pelacakan untuk [PLAN.md](PLAN.md). "Terverifikasi" berarti tercakup oleh tes yang
lulus atau sudah dijalankan langsung di aplikasi — bukan sekadar kode yang berhasil dikompilasi.*

---

## Current state / Kondisi saat ini

**v0.1 Foundation — complete, and proven against live providers through the real UI.**
**v0.2 Trust and control — all six items complete, and driven through the app.**
**v0.3 Reach — all five items complete; documents opened in Office, a real browser driven.**
**v0.4 Scale and polish — local embedding and sub-second start shipped; two items measured and
deliberately not built.**

```
dotnet build AutoWork.slnx     →  0 warnings, 0 errors
dotnet test                    →  369 passed, 0 failed, 43 skipped (live tests, no key set)
dotnet test  (with AUTOWORK_LIVE_*)  →  362+ passed, 0 failed (opt-in; v0.4 additions are offline)
```

What has actually been run, not merely compiled:

- **Azure OpenAI (`gpt-5-mini`)** — six live tests, including a run that put a real file on a
  real disk and a run that failed to talk its way out of the sandbox.
- **DeepSeek (`deepseek-v4-flash`)** — researched a topic on the internet and produced a Word
  report and a four-slide PowerPoint deck, both passing the OpenXML validator. The whole suite
  passes 94/94 against either provider, which is the closest thing there is to proof that
  nothing has quietly become vendor-specific.
- **The desktop UI itself** — a run driven through the Work view end to end: goal typed, Start
  pressed, a consent card raised and approved, the file written, the action log attributing every
  step to Brain or Hands.

The live suite is skipped unless `AUTOWORK_LIVE_API_KEY` and `AUTOWORK_LIVE_MODEL` are set, so
the default `dotnet test` stays offline and free. See `LiveProviderTests` for the variables.

---

## v0.1 Foundation

### Core — `AutoWork.Core`

| Item | Status | Verification |
|---|---|---|
| Config model, JSON store, atomic save, corrupt-file quarantine | ✅ Done | `ConfigurationTests` |
| Environment overlay, vendor-key auto-seeding | ✅ Done | 7 tests in `ConfigurationTests` |
| Provider preset catalogue (10 presets) | ✅ Done | Used by Settings; exercised in tests |
| Encrypted secret store (AES-GCM; DPAPI-wrapped key on Windows) | ✅ Done | Manual — see limitation below |
| `PathGuard` sandbox: canonicalisation, symlink resolution, containment, denied patterns | ✅ Done | 18 tests in `SandboxTests`, incl. escape attempts |
| Protected system + AutoWork-internal directories | ✅ Done | `SandboxTests` |
| Glob matcher (`*`, `**`, `?`) | ✅ Done | 7 theory cases |
| Append-only JSONL action log with rotation and live events | ✅ Done | Streams into the Activity view |
| Approval broker with per-run standing grants | ✅ Done | Exercised via tools |
| Knowledge bases, cosine search, keyword fallback | ✅ Done | Exercised via Knowledge view |
| Skill store: open-format parsing, install, remove | ✅ Done | 21 tests, incl. hostile skill names and escaping bundle paths |
| Skill bundles: templates, references and scripts installed with the manifest | ✅ Done | Live: `anthropics/skills/pdf` installs with its scripts and reference |
| MCP server settings and verified catalogue | ✅ Done | 5 tests; every package checked against its registry |

### Providers — `AutoWork.Providers`

| Item | Status | Verification |
|---|---|---|
| OpenAI-compatible client factory (9 vendors + custom) | ✅ Done | Live against Azure OpenAI |
| Azure OpenAI preset, endpoint completion, env seeding | ✅ Done | 9 tests + live run |
| Native Anthropic Messages client (text, images, tool use) | ✅ Done | Builds; live call needs a key |
| Ollama client | ✅ Done | Builds |
| Embedding generators | ✅ Done | Builds |
| Client caching keyed on resolved credentials | ✅ Done | Invalidated on config change |
| Connection test with human-readable error mapping | ✅ Done | Live; empty reply now reported as failure |
| Model listing from the provider | ✅ Done | Azure `/openai/v1/models` returns 200 |
| Sampling-parameter compatibility (drop and retry) | ✅ Done | 6 offline tests + live gpt-5-mini |
| Reasoning-headroom recovery on empty capped replies | ✅ Done | 3 offline tests + live gpt-5-mini |
| **Live end-to-end run against a real provider** | ✅ Done | 6 live tests, Azure `gpt-5-mini` |

### Tools — `AutoWork.Tools`

| Item | Status | Verification |
|---|---|---|
| File ops: list, info, read, search, write, append, mkdir, move, copy, delete | ✅ Done | `FileToolTests` |
| Batch rename with collision refusal | ✅ Done | 4 tests incl. collision + bad regex |
| Organise by extension / date / type | ✅ Done | `FileToolTests` |
| Soft delete into a protected recycle folder | ✅ Done | — |
| Shell tool, allow-list, timeout, approval gate | ✅ Done | Hidden-when-forbidden verified |
| Excel with live formulas | ✅ Done | `DocumentGenerationTests` — formula survives round-trip |
| Word (.docx) | ✅ Done | Validator-clean; opened in Microsoft Word |
| PowerPoint (.pptx) built from scratch | ✅ Done | Validator-clean, relationship graph asserted, opened and rendered by PowerPoint |
| PDF via QuestPDF | ✅ Done | Valid header, non-trivial size |
| CSV read / profile / clean | ✅ Done | Builds; profiling used by the agent |
| PDF text extraction, scanned-PDF detection | ✅ Done | Builds |
| Image resize / convert / batch | ✅ Done | Builds |
| Screen capture — Windows GDI | ✅ Done | Executed: a real image, checked for size and for more than one colour |
| Screen capture — macOS / Linux helpers | ✅ Done | ⬜ Not verified on those platforms |
| Synthetic input — Windows SendInput | ✅ Done | Pointer movement executed and read back; keyboard path not asserted — see below |
| Synthetic input — macOS/Linux via cliclick/xdotool | ✅ Done | ⬜ Not verified |
| Web fetch and download with host allow-list | ✅ Done | Builds |
| `web_search` — Tavily, then DuckDuckGo, then Wikipedia | ✅ Done | 13 offline + 2 live tests; drove the DeepSeek research run |
| `data_read_document` — Word, PowerPoint, Excel, PDF, HTML, text → Markdown | ✅ Done | 10 tests over files written by AutoWork's own generators |
| Search key in Settings, env var and config | ✅ Done | Screenshotted; stored in the secret store |

### Agents — `AutoWork.Agents`

| Item | Status | Verification |
|---|---|---|
| Planner with JSON plan, lenient extraction | ✅ Done | 4 tests incl. fenced + malformed input |
| Step orchestrator, per-step failure containment | ✅ Done | Builds |
| Error recovery, consecutive-failure abandonment | ✅ Done | — |
| Wave scheduling for parallel steps | ✅ Done | 3 tests incl. cycle handling, plus live planners on both providers producing a 3-wide wave |
| Sub-agent coordinator with bounded concurrency | ✅ Done | Live: three agents ran concurrently against one provider and each produced its artefact |
| Auto-compaction with tool-pair-safe boundaries | ✅ Done | 6 tests, plus a live run where it fired mid-run and the provider kept accepting the conversation |
| Token estimation incl. tool schemas and images | ✅ Done | 3 tests |
| Self-verification against success criteria | ✅ Done | Builds |
| Observable tool wrapper feeding the Work Tape | ✅ Done | Verified in the running app |
| Vision tools (screen_look, image_describe) | ✅ Done | Needs a vision model to exercise |
| Knowledge tools | ✅ Done | Builds |

### Integrations — `AutoWork.Integrations`

| Item | Status | Verification |
|---|---|---|
| Connector framework, credential resolution, status test | ✅ Done | Builds |
| Skills gallery over GitHub repositories | ✅ Done | Browsed `anthropics/skills` and `obra/superpowers` live; installed a real skill |
| MCP client: stdio + HTTP, connect, list, probe | ✅ Done | Live: added and tested a server through the UI, 13 tools returned |
| MCP catalogue entries actually start | ✅ Done | 7 of 10 launched and listed tools; the other 3 need credentials this machine has none for |
| GitHub (search, issues, read file, create issue) | ✅ Done | Endpoint reached live; a bad token maps to the documented message. ⬜ No successful call — needs an account |
| Google Drive + Gmail (shared OAuth refresh flow) | ✅ Done | Token endpoint reached live and refused a bad refresh token. ⬜ No successful call |
| Notion (search, read, append) | ✅ Done | Endpoint reached live and refused a bad token. ⬜ No successful call |
| Asana (projects, tasks, create) | ✅ Done | Endpoint reached live and refused a bad token. ⬜ No successful call |
| PayPal (transactions, balances — read-only) | ✅ Done | OAuth form POST reached live and refused bad client credentials. ⬜ No successful call |

### Desktop — `AutoWork.Desktop`

| Item | Status | Verification |
|---|---|---|
| Design tokens, dark + light theme dictionaries | ✅ Done | Both themes screenshotted |
| Control styles, tab restyling, nav rail | ✅ Done | Screenshotted |
| Organ rail (Think / See / Act) | ✅ Done | ACT observed lit during a live run |
| Work Tape with numbered stamps and nested tool rows | ✅ Done | Screenshotted against live Azure traffic |
| Inline consent cards | ✅ Done | Raised, screenshotted and approved in a live run |
| Activity log view with live streaming | ✅ Done | Builds; the underlying JSONL verified after a UI run |
| Knowledge view | ✅ Done | Builds |
| Integrations view | ✅ Done | Builds |
| Settings: models, permissions, agent, appearance, about | ✅ Done | Screenshotted, incl. the search-key field |
| First-run routing to Settings when no model exists | ✅ Done | Verified by launching with a clean environment |
| English + Bahasa Indonesia interface | ✅ Done | ~112 keys per language |
| App launches without error | ✅ Done | Verified on Windows 11 |
| **A whole run driven through the Work view** | ✅ Done | Goal typed, Start pressed, consent approved, file written |
| Automation ids on nav, composer and consent buttons | ✅ Done | Drives the UI test; also names the controls for screen readers |
| Knowledge: import Word/PowerPoint/Excel/PDF/CSV/HTML/text as notes | ✅ Done | 10 conversion tests; button screenshotted |
| Skills gallery view | ✅ Done | Driven live: browsed, filtered, installed with bundle, removed |
| `skill_file` / `skill_run` tools, gated script execution | ✅ Done | 13 tests; a bundled Python script really runs and returns its output |
| **A model using a skill when asked to** | ✅ Done | Live on Azure and DeepSeek: opened a skill and applied a rule written nowhere else |
| **A model choosing to run a skill's script** | ✅ Done | Live on both: called `skill_run` and reported the value only the script produces |
| **A model choosing to call an MCP tool** | ✅ Done | Live on both: called a tool served by `server-everything` |
| Dependency provisioning into a per-skill environment | ✅ Done | 10 planning tests + a live install: PyYAML fetched, script then ran |
| **Skill script approval, clicked by a person** | ✅ Done | Card raised in the app showing interpreter, resolved path and working folder; approved; script ran |
| MCP gallery view | ✅ Done | Driven live: searched, added, tested — "answered with 13 tools" |

### Install and docs

| Item | Status | Verification |
|---|---|---|
| `install.ps1` / `uninstall.ps1` (Windows) | ✅ Done | Full round trip: 134 files installed, both shortcuts created and resolved, installed build launched, uninstall removed everything and kept user data |
| `install.sh` / `uninstall.sh` (macOS/Linux) | ✅ Done | ⬜ Not run on those platforms |
| README in English and Bahasa Indonesia | ✅ Done | — |
| docs/en and docs/id | ✅ Done | — |
| PLAN.md, Progress.md | ✅ Done | — |

---

## v0.2 Trust and control — complete

The first two items of [PLAN.md](PLAN.md)'s v0.2 list. Both were config that already existed and
did nothing: `KeepRunHistory`, `RunHistoryRetentionDays` and `AppPaths.RunsDirectory` were
declared, created on startup, and referenced by nothing at all.

### Run history

| Item | Status | Verification |
|---|---|---|
| `FileRunHistoryStore` — one headline file plus one transcript file per run | ✅ Done | 13 tests |
| Polymorphic run events, so a transcript reads back as the same events | ✅ Done | Round-tripped event by event; a separate test fails if any `RunEvent` subtype lacks a discriminator |
| Retention pruning driven by `RunHistoryRetentionDays` | ✅ Done | Old run deleted, recent kept, both files gone; a retention of 0 keeps everything rather than deleting it |
| Tool results capped before they are stored | ✅ Done | A 250,000-character result stored under 6,000, file under 20 KB |
| Run ids sanitised before becoming file names | ✅ Done | `../../escape` and friends stay inside the folder |
| A damaged transcript still shows its headline | ✅ Done | Truncated JSON loads as headline + no events |
| The orchestrator writes every run, including cancelled ones | ✅ Done | Cancelled run written with its steps; `KeepRunHistory=false` writes nothing |
| History section: open, re-run, delete | ✅ Done | Builds and loads; opening replays through the live Work Tape rather than a second renderer |
| Settings switch and retention field | ✅ Done | Added — the History page's "turned off in Settings" now refers to a control that exists |
| **A real run's transcript, reloaded** | ✅ Done | Live on Azure `gpt-5-mini`: every event the run emitted came back in order and as the same type |
| **A past run reopened in the app** | ✅ Done | Fresh launch, History → Open: goal, plan, Work Tape and tool timings rebuilt from the JSON on disk |

### Pause and steer

| Item | Status | Verification |
|---|---|---|
| `RunController` — pause, resume, steer, abandon | ✅ Done | 8 tests, including that cancelling releases a parked run |
| Pause held at step boundaries, never mid-step | ✅ Done | A paused run demonstrably does not request the next step until resumed |
| Correction put to the model *before* the step it should change | ✅ Done | Asserted on the actual message order sent to the provider |
| `RunPausedEvent` / `RunResumedEvent` on the Work Tape | ✅ Done | Emitted, rendered, and persisted to history |
| Steering without pausing first | ✅ Done | Queued and applied at the next boundary |
| Pause button in the header, correction box in the Work view | ✅ Done | Driven through the app: paused, corrected, resumed, finished |
| **A real model changing course mid-run** | ✅ Done | Live on Azure `gpt-5-mini`: paused after step 1, told to rename the remaining outputs, and the files on disk carry the new prefix while the old names never appear |
| **The whole loop through the desktop UI** | ✅ Done | Goal typed, consent approved, Pause clicked, correction typed, Resume clicked; `draft-one.txt`, `final-two.txt`, `final-three.txt` written; `run.paused` and `run.resumed` in the saved transcript; the run reopened from disk in a fresh launch |

### Restore from recycle

| Item | Status | Verification |
|---|---|---|
| An index recording where each deleted item came from | ✅ Done | 12 tests. Without it "recoverable" only ever meant "still on disk somewhere" |
| Put a file or a whole folder back where it was | ✅ Done | Contents and nested structure checked after restore |
| Restoring over something that exists needs a second, explicit click | ✅ Done | First attempt refuses and leaves both files untouched |
| A file whose parent folder has since gone is still restorable | ✅ Done | Parent recreated on restore |
| Nothing can be restored into AutoWork's own folder | ✅ Done | A doctored index line aimed at `config.json` is refused and the item survives |
| Items recycled by older builds are listed but not restorable | ✅ Done | Shown as unrestorable rather than hidden — they still take up space |
| Delete for good, and empty the bin | ✅ Done | Files and index both cleared |
| Recycle page in the app | ✅ Done | Builds and loads |

### Diff preview for writes

| Item | Status | Verification |
|---|---|---|
| Line diff with trimmed head and tail, and collapsed runs | ✅ Done | 11 tests; a one-line edit in a 500-line file stays under 12 rows |
| Output capped so a consent card stays readable | ✅ Done | A 600-line rewrite reaches the UI as ≤201 rows |
| Very large changes summarised instead of diffed | ✅ Done | 4,000 lines each way returns a note, not a table |
| Shown in the consent card | ✅ Done | Rendered with styling classes, so the colours follow a live theme switch |
| **The preview obeys the sandbox** | ✅ Done | A write outside the granted folders produces no preview, and the file's contents never reach the card |
| A new file carries no diff | ✅ Done | Creating and rewriting must not look the same |
| Binary and oversized files are described, not diffed | ✅ Done | 5 tests driving the real `files_write` and `files_append` |

### Cost and token meter

| Item | Status | Verification |
|---|---|---|
| Usage read from the provider's own numbers | ✅ Done | 10 tests |
| Counted outside the function-invocation loop | ✅ Done | Every provider round trip a step makes is counted, not just the outermost |
| Per-model pricing entered by the user; no built-in table | ✅ Done | An unpriced model reports tokens and stays silent about money |
| Half a price is not enough to quote a cost | ✅ Done | Input-only pricing yields no figure |
| Two models in one run priced separately | ✅ Done | Planner and executor rates are not blended |
| A provider that reports nothing makes the total a floor | ✅ Done | Shown as "at least N", never as an exact figure |
| Live meter in the header, totals in the result card and history | ✅ Done | Builds and loads |
| **Real usage from a real provider** | ✅ Done | Live on Azure `gpt-5-mini`: input and output tokens both non-zero, several calls counted for one run, cost matching the entered price, and the same figures in the saved run |

### Approval policy rules

| Item | Status | Verification |
|---|---|---|
| Standing allow/deny rules, scoped by kind and folder | ✅ Done | 21 tests |
| **Deny always beats allow, whichever order they are in** | ✅ Done | Asserted both ways round |
| **A rule changes what is asked, never what is permitted** | ✅ Done | An allow rule pointed at an ungranted folder gets past the prompt and is still refused by `PathGuard`; the file is not created |
| An allow with no folder is refused at creation and ignored by the engine | ✅ Done | The one shape that would undo the consent model |
| Only writing and deleting can be allowed by a rule | ✅ Done | Shell, input, screen and network cannot be scoped to a folder, so they stay per-action |
| An allow applies only when it covers every path in the request | ✅ Done | A mixed batch still asks |
| Segment-boundary matching | ✅ Done | A rule for `Proj` does not cover `Proj-private` |
| A deny outlives an "allow for this run" clicked earlier | ✅ Done | The more deliberate statement wins |
| Every rule decision is logged with the rule quoted | ✅ Done | An automatic decision that leaves no trace is the bad version of this feature |
| Rules editor in Settings › Permissions | ✅ Done | Invalid rules are flagged live and dropped on save rather than stored inert |

---

## v0.3 Reach — complete

### More document formats

| Item | Status | Verification |
|---|---|---|
| OpenDocument text and spreadsheet | ✅ Done | 14 tests |
| `mimetype` first in the archive and stored uncompressed | ✅ Done | Asserted on the raw ZIP entry, not on the XML |
| **The files open in a real application** | ✅ Done | Word opens the `.odt` with all seven paragraphs; Excel opens the `.ods` and computes `SUM` to 220 and 165 |
| A1 formulas rewritten into OpenFormula | ✅ Done | 7 cases, including quoted text left alone |
| Markdown export from Word, Excel, PowerPoint, PDF, CSV, HTML | ✅ Done | Round trip via the existing converter; an unsupported type is refused rather than writing nothing |
| Template filling for `.docx` and text | ✅ Done | Placeholders split across two and three runs are filled; the result still passes the OpenXML validator |
| Unfilled placeholders and unused values reported | ✅ Done | A document shipped with `{{amount}}` in it is worse than a refusal |

### Scheduled and triggered runs

| Item | Status | Verification |
|---|---|---|
| Saved jobs: hourly, daily, weekly, or folder-watch | ✅ Done | 17 tests against a clock the test moves |
| Due times read from the clock, not a countdown | ✅ Done | Three days closed produces one run, not three |
| A folder trigger waits for the copying to stop | ✅ Done | Twenty files written produce one run |
| **A job can only watch a folder the sandbox grants** | ✅ Done | An ungranted folder is refused at load and never watched |
| One run at a time | ✅ Done | A job coming due mid-run is skipped and says so, rather than queued |
| A broken jobs file schedules nothing rather than stopping the app | ✅ Done | — |
| Jobs page, and a job runs through the Work view like any other | ✅ Done | Same tape, same consent cards, same history |

### Streaming responses

| Item | Status | Verification |
|---|---|---|
| The reply arrives in fragments while the step runs | ✅ Done | 3 offline tests |
| Each fragment carries the running total for its step | ✅ Done | A consumer assigns rather than appends |
| Turning it off changes nothing but the fragments | ✅ Done | Same closing message, no streaming call made |
| A provider that cannot stream falls back to waiting | ✅ Done | A refusal before any content retries without streaming |
| **Streaming with tools, against a real provider** | ✅ Done | Live on Azure `gpt-5-mini`: fragments arrived, `files_write` still ran, the file was written |

### Meeting intelligence

| Item | Status | Verification |
|---|---|---|
| Local-first speech to text, off until configured | ✅ Done | 16 tests |
| Nothing advertised to the model while it is off | ✅ Done | Same rule the shell tools follow |
| Uploading a recording is a heavier ask than running locally | ✅ Done | `System` risk and a network consent prompt, versus `Write` |
| Argument templates split before substitution | ✅ Done | A recording at `a b.wav` stays one argument |
| Sidecar `.txt` and `.srt` output both read | ✅ Done | Subtitle timings stripped to prose |
| A long transcript is saved whole and returned short | ✅ Done | The context window is not swallowed by a two-hour meeting |
| **A real command started and its output used** | ✅ Done | Run against a real process; a missing command reports plainly |
| ⬜ A real speech model | Not run | AutoWork ships none and this machine has none — see below |

### A real browser session

| Item | Status | Verification |
|---|---|---|
| Drives a browser already installed, no download | ✅ Done | Finds Edge or Chrome; a configured path that does not exist is not silently replaced |
| Its own profile, which stays signed in | ✅ Done | **Live: `localStorage` set in one session was read back in the next** |
| Off by default and separate from network access | ✅ Done | 9 offline tests |
| The same outbound host allow-list as the web tools | ✅ Done | Label-boundary matching, so `example.com.evil.net` does not match |
| Reading is `Safe`; navigating and clicking are `System` | ✅ Done | Acting as the signed-in user is not the same as reading a page |
| **A real browser opened, read, typed into and clicked** | ✅ Done | Live: navigated, read the text, listed the outline, typed into a field, clicked by visible text, and saw the DOM change |

### MCP catalogue and manual entry

| Item | Status | Verification |
|---|---|---|
| 12 new entries: Figma, Canva, Blender, GitHub, Atlassian, Linear, Asana, Chrome DevTools, Microsoft Learn, Azure DevOps, MarkItDown, AWS documentation, AWS knowledge | ✅ Done | Each read off the vendor's own documentation page, not a directory listing or a search result |
| Hosted entries point at the vendor's own domain | ✅ Done | Asserted per entry — a lookalike host would be a way to send someone through an OAuth flow at an address that is not theirs |
| Community entries are labelled and explain their setup | ✅ Done | Blender is the only one, and says the add-on must be installed first |
| Deprecated official servers stay out | ✅ Done | slack, postgres, brave-search and the old github server are asserted absent |
| Links to the Microsoft, Google, AWS and official collections | ✅ Done | Shown beside the gallery rather than scraped in |
| **Adding a server by hand** | ✅ Done | Did not exist before — only the catalogue and a generic remote entry. Command or URL, arguments split shell-style, `NAME=value` environment lines, tokens to the secret store |
| **The two new keyless entries start for real** | ✅ Done | Live: Microsoft Learn and Chrome DevTools both connected and offered tools |
| ⬜ The three uv-based entries | Not run | `uv` is not installed on this machine; skipped rather than reported as broken |
| ⬜ The five OAuth entries | Not run | Figma, GitHub, Atlassian, Linear and Asana open a browser to sign in and cannot be probed unattended |

**Not added, deliberately.** **Unreal Engine** has no canonical server — eight competing community
projects, each needing its own C++ plugin built, and shipping one would be blessing a stranger's
repository. **Unity** ships an official integration, but it is an in-Editor relay that writes the
client configuration for you rather than a command anyone can paste. Both are exactly what the
new manual-entry form is for, and the form says so.

---

## v0.4 Scale and polish — complete

| Item | Status | Verification |
|---|---|---|
| Local embedding, no network and no download | ✅ Done | 17 tests |
| **The hash is stable across processes and machines** | ✅ Done | FNV-1a pinned against known values. `string.GetHashCode` is randomised per process, so using it would have turned every stored knowledge base into noise on the next restart |
| Word forms and typos still match | ✅ Done | invoice/invoices/invoicing all score above the unrelated baseline, via a five-letter prefix feature and character trigrams |
| **Switching embedding model no longer empties the knowledge base** | ✅ Done | Vectors now record which model made them; a mismatch falls back to keywords instead of scoring zero and dropping the entry from every result |
| Sub-second cold start | ✅ Done | Measured: **878 ms** to a visible window with ReadyToRun, against **1,821 ms** without. First truly-cold run 2.1 s against 6.5 s |
| Installer exposes the trade | ✅ Done | `-Startup Fast` (137 MB, default) or `-Startup Small` (82 MB) |
| Vector store | ✅ Measured, not built | 1,000 entries in 1 ms; 10,000 in 17 ms; 50,000 in **73 ms**. A dependency for a problem that does not exist at this scale — reasoning in PLAN.md |
| ⬜ NativeAOT / trimming | Not viable | A trimmed publish fails outright: reflection-based JSON across every store, plus reflection-heavy document libraries. Would need source-generated contexts throughout and probably the loss of the polymorphic transcript format |
| Plugin-authored connectors | ⬜ Deliberately not built | Unchanged: loading third-party assemblies into the process holding the user's API keys needs a sandboxing story that does not exist |

---

## What the live runs exposed / Temuan dari run live

Recorded because they are the reason several defaults changed, and because they will recur with
every reasoning model.

1. **`gpt-5-mini` rejects `temperature` at anything but its default, and rejects `max_tokens`
   outright** — with a 400, not by ignoring the field. AutoWork sets both on every request, so
   before the fix such a model could not complete one step. `ParameterCompatibilityChatClient`
   now reads the refusal, drops the named parameter and retries, remembering it per profile.
   A capability table was rejected: model ids drift, and a table would be *wrong* for the next
   model rather than merely incomplete.
2. **A reasoning model bills its private deliberation against the output cap and spends it
   first.** The connection test asked for one word with a cap of 16 and got HTTP 200, an empty
   string, and a bill for 16 tokens — a success that delivered nothing. Every short call in the
   codebase (verifier 700, summariser 400, compactor 900) was sized for models that do not do
   this. The client now detects "finished on length, said nothing" and retries with room.
3. **A connection test that returns no text is now reported as a failure.** Showing a green tick
   for a silent model sends the user off to debug their prompt instead of their configuration.
4. **An Azure key alone must not seed a model.** The endpoint is per-resource, so a key-only
   seed produced a profile pointing at a placeholder hostname — which, being first in the list,
   became the default planner and failed every run with a confusing 404.
5. **A search backend must not depend on the caller's HttpClient.** The Wikipedia fallback
   answered 403 whenever it was handed a client without a User-Agent, and swallowed it as "no
   results". It sets its own header now. A last resort that only works when configured correctly
   elsewhere is not a last resort.
6. **Every generated PowerPoint deck was unopenable — and the validator said they were fine.**
   The slide layout had no relationship back to its slide master, so PowerPoint rejected the
   package outright with a "file is corrupted" error that names nothing. `OpenXmlValidator` does
   not check cross-part references, so the deck test passed throughout. Found only by opening a
   deck in PowerPoint via COM; fixed with `layoutPart.AddPart(masterPart)`, and now covered by a
   test that asserts the relationship graph part by part rather than trusting a clean validation.
7. **The default workspace was a folder the sandbox always refused.** The starter policy granted
   `AppPaths.WorkspaceDirectory`, which lived inside AutoWork's own state directory — a protected
   location `PathGuard` rejects unconditionally. A fresh install could therefore not write a
   single file, and because the working directory is the first writable grant, every bare
   filename resolved into it. The workspace now lives at `Documents/AutoWork`, outside the
   protected root and outside anything an uninstall removes, and `ResolveWorkingDirectory` steps
   over a grant the guard would refuse. Every test built its own policy, so nothing caught it;
   two tests now assert the shipped default is usable.

## What verifying the rest exposed / Temuan dari memverifikasi sisanya

Three defects that only a real execution could show, each hidden behind something that looked
verified.

1. **Concurrent sub-agents could not recover from a rejected parameter.** `gpt-5-mini` refuses a
   custom `temperature`, which the compatibility client handles by dropping it and retrying — but
   sub-agents share one client and their first requests race. Whichever thread recorded the
   parameter retried; the others read "already known" as "nothing learned" and rethrew the raw
   400. Two of three sub-agents died while their sibling succeeded. The retry decision now asks
   "would retrying send something different?" against the attempt that failed, rather than "was I
   the one who learned it?".
2. **A config.json written the documented way was silently ignored.** The app serialised
   PascalCase, deserialisation was case-sensitive, and `docs/en/configuration.md` showed
   camelCase. A hand-edited config parsed cleanly into all-defaults — and the first thing lost was
   the user's folder grants. Saving is camelCase now and reading accepts either, so files from
   earlier builds still load. Found by writing a config from the documentation and watching the
   agent report it had no write permission.
3. **Compaction could make the context bigger.** Summarising a handful of terse tool calls into
   careful prose, plus the wrapper explaining what the summary is, came out longer than what it
   replaced: 5,102 tokens in, 5,184 out — four times in one run, because the threshold stayed
   crossed. A model call paid for each time, to make things worse. Compaction now measures the
   result and discards it if it did not shrink. Only visible under real token pressure, which is
   why the unit tests never saw it.
4. **`install.ps1` exited non-zero after a successful install.** Its closing "Start AutoWork now?"
   prompt threw under `-NonInteractive`, so CI or a provisioning script would read a completed
   install as a failure. `install.sh` already guarded this with `[[ -t 0 ]]`; the PowerShell one
   did not.

## What building v0.2 exposed / Temuan dari membangun v0.2

1. **A test that reacted to run events lost the race about half the time.** `Progress<T>` hands
   the callback off to be run later, so "pause when step 1 finishes" arrived after the
   orchestrator had already checked the boundary and started step 2. The first version of the
   pause test failed on exactly that, and the fix was a progress implementation that reports on
   the calling thread. Worth recording because the *product* is right to use `Progress<T>` — a UI
   click is asynchronous anyway, and the gate re-checks at the next boundary — but a test that
   asserts on ordering cannot be.
2. **Three settings that did nothing.** `KeepRunHistory`, `RunHistoryRetentionDays` and
   `RunsDirectory` were declared, documented, and created on every startup; grep found no other
   reference to any of them. The directory was made empty on first run and stayed empty forever.
   They mean something now, and the Settings tab has controls for the two that are choices.
3. **An obsolete API in the skill gallery.** `Uri.EscapeUriString` was warning on every build and
   would corrupt a skill path containing a `#`. Replaced with per-segment escaping, so separators
   stay separators.
5. **Soft delete never recorded where a file came from.** The delete tool moved files into a
   dated folder under AutoWork's own directory and told the model "it can be restored from
   there" — but nothing anywhere held the original path. Restoring meant a person working it out
   themselves from a timestamped filename. The claim had been in the tool's own description
   since v0.1. There is an append-only index now, and files recycled by earlier builds are listed
   as unrestorable rather than quietly hidden.
6. **A torn line in an append-only log cost the *next* record too.** Killing a process mid-append
   leaves a line with no terminator; the next `AppendAllText` then concatenates onto the fragment
   and both records are lost. Found because a test written to prove "one tear costs one entry"
   failed. Appends now close a torn line first, so a tear costs exactly the entry it happened to.
7. **The meter's prices depended on a separate registration call.** `Record(model, usage)` took
   the model but priced it from a dictionary filled by a different method, so usage recorded
   without registering first reported no cost — silently, and only for money. Two tests caught
   it. `Record` now registers what it is given; the separate call is gone.
4. **A correction does not update the plan's success criteria — so verification can fail a run
   that did exactly what was asked.** Driving the app for the screenshots, the run was paused and
   told to rename its remaining outputs. It obeyed: `draft-one.txt`, `final-two.txt`,
   `final-three.txt` on disk. The verifier then checked the *original* criteria, did not find
   `draft-two.txt`, and marked the run Failed — which is why the History screenshot shows a red
   run that in fact succeeded. Not a defect in pause and steer, and not silently wrong either:
   the summary says plainly what it did. But steering and verification do not yet know about each
   other, and the honest fix is for a correction to amend the criteria it invalidates. Left as it
   is rather than guessed at.

## What building v0.3 exposed / Temuan dari membangun v0.3

1. **The ODF files validated, parsed, and would not open — because of the XML declaration.**
   `XmlWriter` takes the encoding for its declaration from the writer it is given, not from
   `XmlWriterSettings.Encoding`, and a plain `StringWriter` reports UTF-16 because that is what a
   .NET string is. Every part therefore said `encoding="utf-16"` while the bytes in the package
   were UTF-8. `XDocument.Parse` ignores the declaration when given a string, so eleven tests
   passed on a file Word called corrupt. Found by opening it in Word, and fixed with a
   `StringWriter` that admits to being UTF-8.
2. **Word only accepts ODF 1.2.** Writing `office:version="1.3"` — the current version — produced
   the same "corrupted" message. Established by having Word save its own `.odt` and comparing.
3. **Excel opened the spreadsheet and silently dropped every formula.** ODF formulas need the
   OpenFormula namespace declared *and* references in `[.B2:.C2]` form; with the prefix but plain
   A1 references the cell opens as an empty number. Nothing warns — the file just quietly loses
   its arithmetic. Established the same way, by having Excel save one.
4. **The transcription tool ignored the transcriber it was given.** `GetTools` built a fresh
   instance from the context, discarding the injected one, so four tests were exercising the real
   local runner rather than the stub and failing for the wrong reason.
5. **Substituting before splitting broke any path with a space.** A recording at
   `C:\Recordings\a b.wav` substituted into the argument template was then split on its own
   space, and the speech model received two arguments that were each half a path. The template is
   split first now.
6. **The DevTools connection from `/json/version` cannot drive a page.** It is a browser-level
   endpoint that speaks `Target` and `Browser` but not `Page` or `Runtime`, so navigation and
   evaluation succeeded and did nothing. The page targets are in `/json/list`.
7. **A browser holds its profile lock after being killed.** Two sessions back to back against the
   same profile — which is exactly what "stays signed in between runs" means — had the second
   fail to start. Only reproduced under the full suite, where everything is slower. Disposal now
   waits for the process to actually exit, and a browser that quits on its own reports that the
   profile is in use rather than timing out with a vague message.

## What is not yet verified / Yang belum terverifikasi

Listed explicitly so nobody mistakes "it compiles" for "it works".

1. **DuckDuckGo search against the live endpoint.** Re-checked: still unreachable, and now with
   a diagnosis. DNS on this network resolves `duckduckgo.com` to `36.86.63.185`, an ISP address
   rather than DuckDuckGo's — an ISP-level block, not a code defect. Wikipedia resolves and
   answers normally from the same machine. The parser stays pinned to a captured sample, and an
   empty parse falls through to the next backend rather than being reported as success.
2. **Anthropic and Ollama end to end.** The live suite is provider-agnostic and passes against
   Azure OpenAI and DeepSeek; the other two clients have never been run. Anthropic needs a key
   nobody here has. Ollama needs no key, but installing it and pulling a tool-capable model is
   several gigabytes on the development machine, and the decision was taken not to — so this is
   a deliberate gap, not an oversight. Both close by pointing `AUTOWORK_LIVE_*` at them:

   ```bash
   AUTOWORK_LIVE_KIND=Ollama  AUTOWORK_LIVE_ENDPOINT=http://localhost:11434  AUTOWORK_LIVE_MODEL=qwen2.5:7b
   AUTOWORK_LIVE_KIND=Anthropic  AUTOWORK_LIVE_MODEL=claude-sonnet-5  AUTOWORK_LIVE_API_KEY=sk-ant-…
   ```
3. **Compaction across a wide range of shapes.** One live run now compacts and continues, but it
   took a 5,000-token window and a goal that forced five separate steps to get there. Compaction
   is checked *between* steps only, so a model that does everything inside one step never
   triggers it however large the conversation grows — worth knowing, and arguably worth changing.
4. **Integration connectors against live APIs.** Request shapes follow each vendor's current
   documentation; none has been exercised with real credentials.
5. **macOS and Linux.** The code paths exist and the projects are cross-platform, but nothing
   has been built or run on either. Screen capture and input control are the most likely to
   need adjustment.
6. **Screen capture and synthetic input at runtime.** The Windows interop compiles and follows
   the documented API, but has not been executed.
7. **The installers end to end.** The build step both scripts depend on is verified; the copy,
   shortcut and launcher steps are not.
8. **A successful call to any integration connector.** All five now reach their vendor's real
   endpoint and map a rejected credential to the message the UI shows — which proves the URL,
   the auth scheme and the error path. What no test can reach without an account is a call that
   succeeds, so the response parsing remains unexercised.
9. **A model reaching for a skill unprompted.** Asked to use a named skill, it does — reliably,
   on both providers. Left to notice a relevant skill from its description alone, it did so twice
   and then, on a third run, listed the folder instead and never opened it. Whether the prompt's
   one-line skill catalogue is a strong enough nudge is unmeasured, and the test now names the
   skill rather than pretending the question is settled.
10. **How often a planner marks work parallel.** Settled that it does: given "produce three
    outputs from one input, independently", both Azure OpenAI and DeepSeek emitted five steps with
    three sharing `dependsOn=[1]`, which the scheduler grouped into a wave of three. Reworded
    slightly, the same providers collapsed the job into one or two steps instead. So the parallel
    path is reachable but sensitive to phrasing, and no attempt has been made to measure the rate.
11. **Three MCP catalogue entries.** Seven of the ten now start and list tools — filesystem,
   memory, sequential-thinking, context7, everything, Tavily and Playwright. Firecrawl and Notion
   need keys this machine has none for, and the remote proxy needs a hosted URL.
12. **Imports outside the name table.** Declared requirements install as written, and known
    imports resolve through a fixed table. Anything else is reported rather than guessed at, so a
    skill importing an unusual SDK still fails — deliberately. Widening the table is a change to
    make with evidence, one entry at a time.
13. **System tools pip cannot install.** `pdf2image` needs poppler, `pytesseract` needs tesseract.
    Both are named up front, neither is installed.
14. **Synthetic keyboard input.** Pointer movement is executed and read back, which exercises
    `SendInput` and its struct layout. `input_type` and `input_key` are deliberately left alone:
    keystrokes go to whatever window has focus, and typing into the terminal running the tests to
    prove typing works is a hazard rather than diligence.

## Next up / Berikutnya

v0.2 and v0.3 are complete. What remains needs something this machine does not have: a vendor
account, a provider key, another operating system, or software that would be a large install
nobody asked for.

1. **A real speech model.** The transcription plumbing is proven against real processes, but no
   Whisper build is installed here, so no actual audio has been transcribed. Closing it is one
   `winget install` and a model file away, and needs no code change.
2. Exercise `screen_look` with a vision model — capture is proven, understanding it is not.
3. Build and run on Linux; verify capture helpers and fontconfig.
4. Point the live suite at Ollama or Anthropic when either is available.
5. Then v0.4 — local embedding via ML.NET, AOT publish, a vector store option.

Two pieces of earlier work are worth doing before v0.4 rather than after:

- **A correction should amend the plan's success criteria it invalidates.** Steering and
  verification still do not know about each other, so a run that does exactly what the user asked
  mid-flight can be marked Failed for not doing what they originally asked.
- **The browser tools have no page-load signal.** Navigation settles on a fixed pause because a
  single-page app finishes loading long before it finishes rendering. It works, and it will be
  wrong on a slow page.
