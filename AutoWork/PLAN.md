# AutoWork — Development roadmap / Peta jalan pengembangan

*Gravicode Studios — led by Kang Fadhil*

This is the plan of record. It states what is built, what is next, and — importantly — what we
have decided **not** to build and why. Status of each item lives in [Progress.md](Progress.md).

*Dokumen ini adalah rencana resmi. Berisi apa yang sudah dibangun, apa berikutnya, dan — yang
penting — apa yang kami putuskan **tidak** dibangun beserta alasannya.*

---

## Design principles / Prinsip desain

These constrain every decision below.

1. **The sandbox is the product.** An assistant with filesystem access is only useful if the
   user can bound it. Any feature that erodes `PathGuard` is rejected, however convenient.
2. **Never claim work that did not happen.** The verification pass, the action log and the
   "REFUSED/ERROR" tool vocabulary all exist so the agent's report matches reality.
3. **Degrade, do not fail.** No embedding model → keyword search. No planner → single-step run.
   No summariser → mechanical summary. A missing optional piece must never end a run.
4. **Provider-agnostic by construction.** No feature may depend on one vendor. Model ids move
   faster than releases, so presets are seeds the user edits, not a supported-model list.
5. **Fast and light.** Hand-wired composition, no DI container, no reflection-heavy startup.

---

## v0.1 — Foundation ✅ *shipped*

The working skeleton, end to end.

- Solution, central package management, .NET 10 across seven projects
- **Core**: config with environment overlay, encrypted secret store, `PathGuard` sandbox,
  append-only action log, JSON knowledge bases with cosine search
- **Providers**: OpenAI-compatible factory covering nine vendors including Azure OpenAI, native
  Anthropic Messages client, embedding generators, connection testing, model listing,
  automatic recovery from parameters a model refuses
- **Tools**: files (incl. batch rename and organise with dry-run), shell, Excel/Word/PowerPoint/
  PDF generation, CSV profiling and cleaning, PDF/Office document reading, image batch
  processing, cross-platform screen capture, synthetic input, web fetch, web search with a
  keyless fallback chain
- **Agents**: planner, step orchestrator with error recovery, parallel sub-agents,
  auto-compaction with tool-pair-safe boundaries, self-verification, vision tools
- **Integrations**: connector framework + GitHub, Google Drive/Gmail, Notion, Asana, PayPal
- **Skills**: an open-format skill store that installs the whole bundle — references, templates
  and scripts — a gallery over GitHub repositories, progressive disclosure so an unused skill
  costs almost nothing, gated execution of a skill's own scripts, and per-skill dependency
  environments
- **MCP**: a client for stdio and HTTP servers, a catalogue of verified ones, and a capability
  switch that keeps them off by default
- **Desktop**: Avalonia UI, custom design system, Work Tape, organ rail, inline consent cards,
  Activity log, Knowledge, Integrations, Settings; dark/light; English and Bahasa Indonesia
- Installers and uninstallers for Windows, macOS and Linux
- 327 offline tests, including sandbox-escape attempts, OOXML validation and package-relationship
  assertions, plus a live suite that runs a real provider end to end when credentials are
  supplied — research on the internet becoming a Word report and a PowerPoint deck, and a model
  choosing of its own accord to open a skill, run its script and call an MCP tool

## v0.2 — Trust and control

Making a long autonomous run something a person is comfortable leaving alone.

- ✅ **Run history.** Persist transcripts; reopen a past run, see what it did, re-run it.
  Shipped: a History section, one JSON pair per run under `runs/`, retention honoured, and the
  saved events replayed through the same Work Tape that showed them live.
- ✅ **Pause and steer.** Interrupt mid-run, add a correction, resume — rather than stop and
  restart. Shipped: pause takes effect at the next step boundary, and the correction is put to
  the model before the step it is meant to change.
- ✅ **Approval policy rules.** "Always allow writes under ~/Projects", "never allow deletes",
  expressed as rules instead of per-action clicks. Shipped with two invariants: deny always
  beats allow, and a rule changes what is *asked*, never what the sandbox *permits* — so an
  allow rule cannot reach outside the granted folders.
- ✅ **Diff preview for writes.** Show what a file write would change before it happens.
  Shipped: a line diff in the consent card, capped so a card stays readable, and absent for a
  new file — creating one and rewriting one should not look the same.
- ✅ **Restore from recycle.** A UI for the soft-delete folder, which currently only the
  filesystem exposes. Shipped with the index that was missing: deletions now record where they
  came from, which is what makes "recoverable" true rather than nominal.
- ✅ **Cost and token meter.** Per-run token accounting and, where the provider reports it,
  spend. Shipped: usage read from the provider's own numbers, per-model pricing the user enters,
  and no built-in price table — see the note below.

**Why no price table.** Publishing one would make the meter show money out of the box, and would
also make it wrong within months: prices move faster than model ids, and the failure mode is a
confident under-report of what a run cost. Prices are per-model fields the user fills in from
their provider's pricing page. Empty means the meter reports tokens, which are true whatever the
price is. This follows the same reasoning as provider presets being seeds rather than a
supported-model list.

## v0.3 — Reach

- ✅ **Streaming responses.** Token-level streaming so long reasoning is visible as it forms.
  Shipped: the step loop streams and accumulates, tool calls are handled underneath by the
  function-invocation layer, and a provider that cannot stream falls back to waiting rather than
  failing the run.
- ✅ **Scheduled and triggered runs.** "Every Monday, summarise last week's invoices." Shipped as
  saved jobs with a schedule or a folder watch. Due times come from the clock, so a closed app
  wakes owing nothing; a folder trigger waits for the copying to stop; and a job can only watch a
  folder the sandbox already grants.
- ✅ **A real browser session.** Shipped — see the note below on why this is not an extension.
- ✅ **Meeting intelligence.** Audio transcription → action items → follow-ups. Shipped local-first
  and off by default: AutoWork drives a speech model you already have, and uploading a recording
  is a separate, deliberate choice.
- ✅ **More document formats.** ODF (`.odt`, `.ods`), Markdown export from any readable document,
  and template filling for `.docx` and text.

**Why a browser session and not a browser extension.** The goal was "the web-automation use case
properly — a real browser session, rather than `web_fetch`", and the reason `web_fetch` falls
short is that it sees the page a stranger sees while the work people want automated is behind a
login. An extension is one way to reach a signed-in session; it is also a separate build artefact,
a native-messaging host, and a store listing, none of which can be verified from this repository.
Driving a browser that is already installed, over the DevTools protocol, against a profile that
stays signed in, reaches the same outcome with nothing to install and is testable end to end.
If a extension is still wanted later — for acting inside the user's *existing* window rather than
a second profile — this is the layer it would sit on.

## v0.4 — Scale and polish

- ✅ **Local embedding.** Knowledge search with no network round-trip at all. Shipped as a hashed
  lexical embedder rather than an ML.NET pipeline or a bundled ONNX model: it needs no download,
  no key and no network, which is what "no round-trip at all" actually requires. It matches word
  forms and survives typos; it does not know that "bill" means "invoice". A hosted model remains
  the better choice for meaning, and stays the default.
- ✅ **Sub-second cold start.** Shipped via ReadyToRun, measured at **878 ms** to a visible window
  against **1,821 ms** before — see the note below on why this is not AOT and why the install got
  bigger rather than smaller.
- ✅ **Vector store option.** Measured rather than built — see below.
- **Plugin-authored connectors.** Still not built, and still for the reason given: loading
  arbitrary assemblies into the process holding the user's API keys is not acceptable without a
  sandboxing story, and there is not one. Recorded here as a decision, not an omission.

**Why not AOT, and why the install grew.** NativeAOT and trimming both need every type reachable
statically. AutoWork's stores serialise with reflection-based `System.Text.Json` throughout — the
polymorphic run transcript especially — and the document libraries (ClosedXML, OpenXML, QuestPDF)
are reflection-heavy by design. A trimmed publish fails outright rather than producing something
that works. Making it viable means source-generated JSON contexts across every store and probably
losing the polymorphic transcript format, which is a large change to buy startup that ReadyToRun
already delivers. So: **ReadyToRun, sub-second, +55 MB**. The installer takes `-Startup Small` for
anyone who would rather have the disk space than the second.

**Why no vector store.** The claim it rests on is "knowledge bases outgrow a linear scan", so the
scan was measured first: **1,000 entries in 1 ms, 10,000 in 17 ms, 50,000 in 73 ms**. A knowledge
search runs inside an agent step already waiting on a provider round trip of several hundred
milliseconds to several seconds. At 50,000 notes the search is still noise next to it, and a
desktop assistant's knowledge base does not reach 50,000 notes. Adding a vector database would be
a large dependency for a problem that does not exist at this scale — against the "fast and light"
principle, and against "no feature may depend on one vendor". The real limit is memory rather than
CPU: 50,000 entries is roughly 100 MB of vectors held while searching. That is the number to act
on if it ever needs acting on. `KnowledgeScaleTests` keeps the measurement honest.

---

## Deliberately not planned / Sengaja tidak direncanakan

- **A hosted or cloud mode.** AutoWork's value is that your files never leave your machine.
  Adding a server undermines the one thing it promises.
- **Payment or fund-movement tools.** The PayPal connector is read-only and stays that way. An
  LLM in the loop must not be able to move money.
- **Auto-approving destructive actions.** "Allow for this run" is deliberately unavailable for
  deletes and shell commands. A standing grant is the wrong affordance for an irreversible act.
- **Silently recording everything into knowledge bases.** An agent that remembers everything it
  sees is a privacy problem. Saving stays a deliberate act.
- **Bundling a default API key.** Users bring their own credentials, scoped to their own account.

---

## Known limitations / Batasan yang diketahui

Stated plainly because they affect what a user should trust.

| Limitation | Detail |
|---|---|
| Input control is not sandboxable | Synthetic mouse/keyboard reaches whatever window has focus. Off by default, approval-gated. |
| Shell runs with full user rights | The working directory is pinned inside a granted folder; the command itself is not confined. |
| Secret store strength varies by OS | DPAPI on Windows; owner-only file permissions on Linux/macOS. Use `env:VAR` with a real secret manager if you need more. |
| Token counts are estimates | Compaction thresholds use a conservative heuristic, not each provider's tokenizer. |
| Reasoning models cost more than they appear to | They bill private reasoning against the output cap. AutoWork raises a cap that proved too small rather than returning an empty reply, so a run can spend more than the configured limit implies. |
| A refused parameter costs one wasted call | Learned per model profile, not per request, so the cost is paid once — but it is paid. Concurrent callers each pay it the first time. |
| Compaction only happens between steps | A model that does all its work inside a single step never triggers it, however large that step's conversation grows. |
| Compaction can decline to act | If the summary would be no smaller than the turns it replaces, the attempt is discarded — the model call is still paid for, and the context stays full. |
| A clean OOXML validation does not mean the file opens | The validator checks schema, not the relationship graph between parts. Document tests assert the wiring explicitly; opening a file in the real application is still the only complete check. |
| Keyless web search is much weaker than Tavily | DuckDuckGo gives titles and snippets but no extracted content, and Wikipedia is one encyclopaedia. Good enough to find sources; not good enough to skip reading them. |
| The DuckDuckGo backend parses markup, not an API | It has no contract to rely on. A markup change turns into "no results" and the chain falls through, rather than into an error. |
| MCP servers run outside the sandbox | A stdio server is a separate process with your full rights; `PathGuard` cannot reach inside it. Off by default, and each server is separately enabled. |
| Skills are trusted text, and sometimes trusted code | The instructions grant no capability, but a skill may bundle scripts. Running one runs someone else's code with your rights — its own switch, off by default, approval before every run. |
| Installing a skill's dependencies is itself code execution | `pip install` runs the package's own `setup.py`, before the script you approved. The package list is shown in the consent card because reviewing it is the only control there is. |
| Import names are resolved from a fixed table, not guessed | `fitz` → PyMuPDF and so on. An unrecognised import is reported rather than installed under an invented name, so an unusual dependency fails deliberately rather than fetching something that merely sounds right. |
| System tools are not provisioned | `pdf2image` needs poppler, `pytesseract` needs tesseract. Named up front; still yours to install. |
| The skills gallery reads GitHub unauthenticated | Rate limits apply. Browsing is a deliberate click rather than something that happens on every visit. |
| A signed-in browser is not sandboxable | `PathGuard` bounds the filesystem; on a site you are logged in to, the agent is you. Off by default, its own profile, and every navigation and click approved. A page that talks the model into clicking something is not prevented by any of that. |
| The browser has no page-load signal | Navigation settles on a fixed pause, because a single-page app finishes loading long before it finishes rendering. Fine on most pages; wrong on a slow one. |
| AutoWork ships no speech model | Transcription drives whichever one you already have. Nothing is installed for you, and nothing works until you point it at one. |
| Streaming falls back silently | A provider that refuses to stream is retried without it and the run continues. The Work Tape then updates per step, as before, with only a line in the action log to say why. |
| A job's outcome is only as reliable as an unattended run | A scheduled run raises the same consent prompts as any other. Left alone with prompts pending, it waits — which is the safe direction, but it does mean "ran overnight" is not the same as "finished overnight" unless standing rules cover what it needs. |
| Screen capture on Wayland | Depends on `grim` or a portal-capable tool being installed and permitted. |
| Model ids drift | Presets are starting points; verify the model id against your provider's current list. |
