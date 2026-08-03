# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

AutoWork — an autonomous desktop work assistant built by Gravicode Studios, led by Kang Fadhil.
.NET 10, Avalonia UI, cross-platform. `requirements.md` is the original Indonesian spec;
[PLAN.md](PLAN.md) is the plan of record and [Progress.md](Progress.md) tracks what is actually
verified versus merely compiling.

## Commands

```bash
dotnet build AutoWork.slnx                                    # whole solution
dotnet test tests/AutoWork.Tests/AutoWork.Tests.csproj        # 369 offline; live tests skip
dotnet run --project src/AutoWork.Desktop/AutoWork.Desktop.csproj
```

Run a single test:

```bash
dotnet test --filter "FullyQualifiedName~SandboxTests.A_symlink_pointing_out_of_the_granted_folder_is_refused"
dotnet test --filter "FullyQualifiedName~ContextCompactionTests"   # a whole class
```

Note the solution is `AutoWork.slnx` (the .NET 10 XML format), not `.sln`. `global.json` pins the
SDK to 10.0.x. Packages are centrally managed in `Directory.Packages.props` — add versions there,
not in individual csproj files.

Launch with a clean state for testing first-run behaviour:

```bash
AUTOWORK_HOME=/tmp/aw-test AUTOWORK_THEME=Light dotnet run --project src/AutoWork.Desktop/AutoWork.Desktop.csproj
```

## Architecture

Seven projects, layered so `AutoWork.Core` depends on nothing and the UI depends on everything.
Full detail in [docs/en/architecture.md](docs/en/architecture.md).

The product is organised as three subsystems — **Brain** (`AutoWork.Agents`), **Eyes** (screen
capture in Tools + vision in Agents), **Hands** (`AutoWork.Tools`). This is not framing: every
`ToolDescriptor` is tagged with an `AgentOrgan`, every log line and tape row is attributed to
one, and the UI colours itself accordingly. Keep the attribution accurate when adding tools.

### Things that will bite you

**`PathGuard` is the product's central security claim.** Every filesystem operation goes through
it. It canonicalises, *resolves symlinks*, rejects protected directories, requires containment
with a segment-boundary check, then applies denied globs. `SandboxTests` is written as a series
of attempted escapes — if you change `PathGuard`, those tests are what tell you whether you broke
the promise. Never add a filesystem path that bypasses it.

**Context compaction must not split a tool call from its result.** Providers reject a
conversation containing an orphaned `FunctionResultContent`. `ContextCompactor.FindPreserveBoundary`
walks the cut point backwards to a clean turn edge, and the test checks that invariant at every
possible cut point. This is a bug class that only appears on long runs in production.

**Tools return strings, never throw.** `REFUSED: …` for policy, `ERROR: …` for mechanical
failure. The string goes straight to the model, which can then adapt; an exception just ends the
turn. `ToolSetBase.GuardedAsync` does the conversion and the logging — use it.

**A forbidden capability yields no tools at all.** `ShellTools.GetTools` returns nothing when
`AllowShell` is false, rather than returning tools that refuse. Advertising a tool and refusing
every call wastes tokens and makes the model keep trying.

**A saved run must read back as the same events.** `RunEvent` is written polymorphically with a
short fixed discriminator per subtype. Adding an event without a `[JsonDerivedType]` makes every
transcript containing it unreadable; `RunHistoryTests` fails if you do. The discriminators are
part of the on-disk format — renaming a class is fine, changing its discriminator is not.

**An approval rule must never widen what is permitted.** `ApprovalRuleEngine` runs at the
*approval* layer; `PathGuard` runs inside the tool body afterwards. That ordering is the entire
safety argument for standing rules — a rule can only suppress a question about something the
sandbox was going to allow anyway. Deny always beats allow, an allow must name both a folder and
a kind, and only `WriteFiles`/`DeleteFiles` may be allowed. `ApprovalRuleTests` and
`ApprovalRuleSandboxTests` are where those claims live.

**Pause only ever takes effect between steps.** `RunController` is checked at step boundaries,
never inside a step, because a tool call already sent to the provider cannot be un-sent. Stopping
a run must also call `Abandon()`, or a paused agent thread waits forever for a resume that is not
coming — the same failure mode as an unanswered consent prompt.

**API keys never go in `config.json`.** It stores a secret-store name or `env:VAR`. Preserve that
when touching config or Settings code.

**OOXML element order is schema-fixed.** In a Word `rPr`, `w:b` must precede `w:sz`. A wrong order
produces a file Word opens but the validator rejects. The document tests run the real OpenXML
validator for exactly this reason.

**A document format is only proven by opening it in the application.** ODF taught this twice over:
the package validated and parsed while saying `encoding="utf-16"` over UTF-8 bytes (because
`XmlWriter` takes the declaration from the writer, and a `StringWriter` reports UTF-16), and Excel
opened the spreadsheet while silently dropping every formula (OpenFormula needs its namespace and
`[.B2:.C2]` references). Word also rejects ODF 1.3 and accepts 1.2. When in doubt, have Word or
Excel save the format itself and compare.

**Word splits a placeholder across runs.** `{{name}}` typed as one word is routinely stored as
`{{na` + `me}}`, so searching run by run finds nothing and reports a template with no
placeholders. `DocumentTemplate` flattens the paragraph, substitutes, and writes back into the
first run.

**Streaming deltas carry the running total per step, not per run.** Each step gets a fresh
accumulator, so a consumer assigns rather than appends — and an invariant written across the whole
run will fail at the first step boundary.

**The DevTools endpoint from `/json/version` cannot drive a page.** It is the browser target: it
speaks `Target` and `Browser` but not `Page` or `Runtime`, so navigation and evaluation succeed
and do nothing. Connect to a page target from `/json/list`. A browser also holds its profile lock
after being killed, so disposal waits for the process to actually exit.

**A stored vector must outlive the process that wrote it.** `LocalEmbeddingGenerator` hashes with
FNV-1a, never `string.GetHashCode` — .NET randomises string hashing per process, so a per-process
seed would silently turn every saved knowledge base into noise on the next restart. The pinned
hash values in `LocalEmbeddingTests` exist to stop that change slipping through. Vectors also
carry the model that made them; comparing two models' vectors returns a meaningless number, or
zero on a dimension mismatch, which drops the entry from every search.

**The meter never invents a number.** There is no built-in price table — both prices must be set
on the `ModelProfile` or no cost is reported, and half a price is not enough. A provider that
answers without reporting usage increments `CallsWithoutUsage`, and the UI then says "at least".
`TokenEstimator` is for compaction only; do not let it feed the meter.

**`$parent[ItemsControl]` inside a `ComboBox` finds the ComboBox.** A `ComboBox` is itself an
`ItemsControl`, so that binding written in one of its items resolves to the wrong ancestor, the
cast fails silently and the control renders blank. Put the data on the row's own view model.

**Organ colours use styling classes, not converters.** `Classes.organ-think="{Binding IsThink}"`
with `DynamicResource` in `Controls.axaml`. An imperative brush lookup would not repaint on a
live theme switch.

## Conventions

- Comments explain *why*, not what. The codebase is commented at the level of design decisions
  and non-obvious trade-offs; match that rather than narrating syntax.
- UI copy is written for the person, in active voice, and lives in `Localization/Strings.cs` with
  English and Indonesian side by side. Add both when adding a key.
- Provider presets are seeds users edit, not a supported-model list — model ids drift.
- Docs are bilingual: `docs/en` and `docs/id` stay in sync.
- Attribution to Gravicode Studios / Kang Fadhil appears in the app's About page, the build props
  and the docs. Keep it.
