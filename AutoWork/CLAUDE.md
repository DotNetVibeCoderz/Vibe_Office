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
dotnet test tests/AutoWork.Tests/AutoWork.Tests.csproj        # 53 tests
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

**API keys never go in `config.json`.** It stores a secret-store name or `env:VAR`. Preserve that
when touching config or Settings code.

**OOXML element order is schema-fixed.** In a Word `rPr`, `w:b` must precede `w:sz`. A wrong order
produces a file Word opens but the validator rejects. The document tests run the real OpenXML
validator for exactly this reason.

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
