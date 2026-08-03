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
  PDF generation, CSV profiling and cleaning, PDF extraction, image batch processing,
  cross-platform screen capture, synthetic input, web fetch, web search with a keyless fallback
  chain
- **Agents**: planner, step orchestrator with error recovery, parallel sub-agents,
  auto-compaction with tool-pair-safe boundaries, self-verification, vision tools
- **Integrations**: connector framework + GitHub, Google Drive/Gmail, Notion, Asana, PayPal
- **Desktop**: Avalonia UI, custom design system, Work Tape, organ rail, inline consent cards,
  Activity log, Knowledge, Integrations, Settings; dark/light; English and Bahasa Indonesia
- Installers and uninstallers for Windows, macOS and Linux
- 91 offline tests, including sandbox-escape attempts, OOXML validation and package-relationship
  assertions, plus a live suite that runs a real provider end to end when credentials are
  supplied — up to and including research on the internet becoming a Word report and a
  PowerPoint deck

## v0.2 — Trust and control

Making a long autonomous run something a person is comfortable leaving alone.

- **Run history.** Persist transcripts; reopen a past run, see what it did, re-run it.
- **Pause and steer.** Interrupt mid-run, add a correction, resume — rather than stop and restart.
- **Approval policy rules.** "Always allow writes under ~/Projects", "never allow deletes",
  expressed as rules instead of per-action clicks.
- **Diff preview for writes.** Show what a file write would change before it happens.
- **Restore from recycle.** A UI for the soft-delete folder, which currently only the
  filesystem exposes.
- **Cost and token meter.** Per-run token accounting and, where the provider reports it, spend.

## v0.3 — Reach

- **Streaming responses.** Token-level streaming so long reasoning is visible as it forms.
  Requires the agent loop to handle partial tool calls; deferred until the loop is stable.
- **Scheduled and triggered runs.** "Every Monday, summarise last week's invoices." Runs on a
  schedule or on a folder-watch trigger.
- **Browser extension.** The web-automation use case properly — a real browser session, driven
  by the agent, rather than `web_fetch`.
- **Meeting intelligence.** Audio transcription → action items → follow-ups. Needs a local
  speech model to avoid shipping recordings to a provider by default.
- **More document formats.** ODF, Markdown export, template-driven generation.

## v0.4 — Scale and polish

- **MCP client support.** Speak Model Context Protocol so third-party tool servers plug in
  without AutoWork shipping a connector for each.
- **Local embedding via ML.NET.** Knowledge search with no network round-trip at all.
- **AOT publish.** Sub-second cold start and a much smaller install.
- **Vector store option.** For users whose knowledge bases outgrow a linear scan.
- **Plugin-authored connectors.** Only once there is a sandboxing story for third-party code —
  loading arbitrary assemblies into the process holding the user's API keys is not acceptable
  as it stands.

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
| A refused parameter costs one wasted call | Learned per model profile, not per request, so the cost is paid once — but it is paid. |
| A clean OOXML validation does not mean the file opens | The validator checks schema, not the relationship graph between parts. Document tests assert the wiring explicitly; opening a file in the real application is still the only complete check. |
| Keyless web search is much weaker than Tavily | DuckDuckGo gives titles and snippets but no extracted content, and Wikipedia is one encyclopaedia. Good enough to find sources; not good enough to skip reading them. |
| The DuckDuckGo backend parses markup, not an API | It has no contract to rely on. A markup change turns into "no results" and the chain falls through, rather than into an error. |
| No streaming yet | The Work Tape updates per step and per tool call, not per token. |
| Screen capture on Wayland | Depends on `grim` or a portal-capable tool being installed and permitted. |
| Model ids drift | Presets are starting points; verify the model id against your provider's current list. |
