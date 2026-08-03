# AutoWork — Development progress / Progres pengembangan

*Gravicode Studios — led by Kang Fadhil*
*Last updated / Terakhir diperbarui: 3 August 2026*

Tracking document for [PLAN.md](PLAN.md). Verified means it is covered by a passing test or was
exercised by running the app — not merely that the code compiles.

*Dokumen pelacakan untuk [PLAN.md](PLAN.md). "Terverifikasi" berarti tercakup oleh tes yang
lulus atau sudah dijalankan langsung di aplikasi — bukan sekadar kode yang berhasil dikompilasi.*

---

## Current state / Kondisi saat ini

**v0.1 Foundation — complete, and proven against live providers through the real UI.**

```
dotnet build AutoWork.slnx     →  0 warnings, 0 errors
dotnet test                    →  91 passed, 0 failed, 9 skipped (live tests, no key set)
dotnet test  (with AUTOWORK_LIVE_*)  →  100 passed, 0 failed
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
| Screen capture — Windows GDI | ✅ Done | Builds; runtime path not yet exercised |
| Screen capture — macOS / Linux helpers | ✅ Done | ⬜ Not verified on those platforms |
| Synthetic input — Windows SendInput | ✅ Done | ⬜ Not exercised at runtime |
| Synthetic input — macOS/Linux via cliclick/xdotool | ✅ Done | ⬜ Not verified |
| Web fetch and download with host allow-list | ✅ Done | Builds |
| `web_search` — Tavily, then DuckDuckGo, then Wikipedia | ✅ Done | 13 offline + 2 live tests; drove the DeepSeek research run |
| Search key in Settings, env var and config | ✅ Done | Screenshotted; stored in the secret store |

### Agents — `AutoWork.Agents`

| Item | Status | Verification |
|---|---|---|
| Planner with JSON plan, lenient extraction | ✅ Done | 4 tests incl. fenced + malformed input |
| Step orchestrator, per-step failure containment | ✅ Done | Builds |
| Error recovery, consecutive-failure abandonment | ✅ Done | — |
| Wave scheduling for parallel steps | ✅ Done | 3 tests incl. cycle handling |
| Sub-agent coordinator with bounded concurrency | ✅ Done | Builds |
| Auto-compaction with tool-pair-safe boundaries | ✅ Done | 6 tests — the boundary invariant is checked at every cut point |
| Token estimation incl. tool schemas and images | ✅ Done | 3 tests |
| Self-verification against success criteria | ✅ Done | Builds |
| Observable tool wrapper feeding the Work Tape | ✅ Done | Verified in the running app |
| Vision tools (screen_look, image_describe) | ✅ Done | Needs a vision model to exercise |
| Knowledge tools | ✅ Done | Builds |

### Integrations — `AutoWork.Integrations`

| Item | Status | Verification |
|---|---|---|
| Connector framework, credential resolution, status test | ✅ Done | Builds |
| GitHub (search, issues, read file, create issue) | ✅ Done | ⬜ Live call not verified |
| Google Drive + Gmail (shared OAuth refresh flow) | ✅ Done | ⬜ Live call not verified |
| Notion (search, read, append) | ✅ Done | ⬜ Live call not verified |
| Asana (projects, tasks, create) | ✅ Done | ⬜ Live call not verified |
| PayPal (transactions, balances — read-only) | ✅ Done | ⬜ Live call not verified |

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

### Install and docs

| Item | Status | Verification |
|---|---|---|
| `install.ps1` / `uninstall.ps1` (Windows) | ✅ Done | `dotnet publish` step verified; full install not run |
| `install.sh` / `uninstall.sh` (macOS/Linux) | ✅ Done | ⬜ Not run on those platforms |
| README in English and Bahasa Indonesia | ✅ Done | — |
| docs/en and docs/id | ✅ Done | — |
| PLAN.md, Progress.md | ✅ Done | — |

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

## What is not yet verified / Yang belum terverifikasi

Listed explicitly so nobody mistakes "it compiles" for "it works".

1. **DuckDuckGo search against the live endpoint.** Its parser is pinned to a captured sample,
   but the host is unreachable from the development machine, so the chain was exercised falling
   through it to Wikipedia rather than landing on it. If DuckDuckGo changes its markup the unit
   test keeps passing while live results stop — which is why an empty parse falls through instead
   of being reported as success.
2. **Anthropic and Ollama end to end.** The live suite is provider-agnostic and passes against
   Azure OpenAI and DeepSeek; the other two clients have not been run with real credentials.
   Point `AUTOWORK_LIVE_*` at them to close this.
3. **Compaction under genuine token pressure.** The boundary invariant is unit-tested at every
   cut point, but no live run has yet been long enough to trigger a real compaction.
4. **Integration connectors against live APIs.** Request shapes follow each vendor's current
   documentation; none has been exercised with real credentials.
5. **macOS and Linux.** The code paths exist and the projects are cross-platform, but nothing
   has been built or run on either. Screen capture and input control are the most likely to
   need adjustment.
6. **Screen capture and synthetic input at runtime.** The Windows interop compiles and follows
   the documented API, but has not been executed.
7. **The installers end to end.** The build step both scripts depend on is verified; the copy,
   shortcut and launcher steps are not.
8. **Sub-agent coordination against a live model.** Parallel waves are unit-tested; no live run
   has yet produced a plan with independent steps to schedule.

## Next up / Berikutnya

1. Point the live suite at Ollama (no key needed) and at Anthropic.
2. Exercise screen capture and `screen_look` with a vision model.
3. Force a long enough run to trigger real compaction, and one with parallel steps.
4. Build and run on Linux; verify capture helpers and fontconfig.
5. Then start v0.2 — run history and pause/steer.
