# AutoWork

**Your digital coworker.** An autonomous desktop assistant that organises your files, builds
professional documents, and runs multi-step workflows — on your own machine, inside a sandbox
you define.

Built with .NET 10 and Avalonia UI. Runs on Windows, macOS and Linux.

> 🇮🇩 **Bahasa Indonesia:** [README.id.md](README.id.md) · Dokumentasi lengkap di [docs/id](docs/id)

![AutoWork planning a job and working through it on the Work Tape](docs/images/work-tape.png)

---

## What it does

AutoWork takes a request in plain language, plans it, does it, and then checks its own work.

```
"Read every invoice PDF in ~/Documents/Invoices, pull out the vendor,
 date and total, and build me an Excel summary with a grand total."
```

It reads the PDFs, extracts the figures, writes a real `.xlsx` with live formulas, and tells
you exactly what it produced — while every step is visible as it happens and recorded in a log
you can audit afterwards.

## The architecture

AutoWork is built as three cooperating subsystems. This is not a metaphor — it is how the code
is organised, and every action in the interface is attributed to one of them.

| Subsystem | What it is | What it does |
|---|---|---|
| **Brain** (Think) | `AutoWork.Agents` | Plans multi-step work, reasons, recovers from failures, summarises its own context when the window fills, and verifies the result before claiming success. |
| **Eyes** (See) | `AutoWork.Tools` + vision model | Captures the screen, reads dialogs, tables and small text that are not available as files. |
| **Hands** (Act) | `AutoWork.Tools` | Files, shell, documents, data, images, synthetic input, and the network — everything that touches the machine. |

## Features

**Autonomous execution.** Give it a goal; it produces a plan with checkable success criteria,
works through it step by step, recovers from tool failures, and verifies the outcome. Progress
streams live onto the *Work Tape* — a stamped, numbered timeline of everything it did.

![Every tool call on the Work Tape, with its arguments, result and timing](docs/images/work-tools.png)

Every tool call is shown with the arguments it was given, what it returned and how long it took.
Nothing happens that you cannot see afterwards.

**Direct local file access.** Read, write, move, copy, rename. Batch rename with regular
expressions and organise folders by type, extension or date — always with a dry-run preview
before anything moves.

**Professional documents.** Excel workbooks with live formulas, Word documents, PowerPoint
decks and PDFs. Generated through real OOXML rather than by asking a model to emit XML, and
opened by the real applications — not just accepted by a validator.

Below: one job — *"research retrieval-augmented generation on the web, then write a short report
and a four-slide deck"* — run against DeepSeek, and the two files it produced, opened in Word and
PowerPoint.

![The generated Word report open in Microsoft Word](docs/images/deepseek-report.png)

![The four generated slides, rendered by PowerPoint](docs/images/deepseek-deck.png)

**Data extraction.** PDF text, CSV profiling (column types, ranges, null counts) and cleaning.
Profiling first means the model reasons about a *summary* of 50,000 rows rather than drowning
in them.

**Web research.** Search the internet and read the results. Tavily when you supply a key,
DuckDuckGo and Wikipedia when you do not — so research is not a paid feature.

**Sub-agent coordination.** Genuinely independent steps run in parallel, each with its own
context, so their token use does not compound.

**Auto-compaction.** When a long run approaches the model's context window, older turns are
summarised away automatically — carefully, never splitting a tool call from its result.

**Knowledge bases.** Topic-scoped memory that survives between sessions, searched by embedding
when an embedding model is configured, and by keyword when one is not. Add notes by hand, or
import Word, PowerPoint, Excel, PDF, CSV, HTML and Markdown files — the file's name becomes the
title and its content becomes the note.

![The Knowledge view, with notes added by hand or imported from a file](docs/images/knowledge.png)

**Skills.** Instructions the agent follows for particular kinds of work, installed from GitHub
repositories you choose. Only each skill's name and one-line description sit in the prompt; the
agent opens the full text when the job calls for it, so an unused skill costs almost nothing.

![The Skills gallery, browsing anthropics/skills and obra/superpowers](docs/images/skills-gallery.png)

**MCP servers.** Borrow tools from any Model Context Protocol server — a real browser, a docs
index, a vendor's API. A catalogue of verified servers is built in, and you can add your own.

![The MCP gallery, testing a server before enabling it](docs/images/mcp-gallery.png)

**Any model you like.** OpenAI, Azure OpenAI, Anthropic, Google Gemini, Ollama, DeepSeek, Qwen,
Moonshot, OpenRouter, LM Studio — or any OpenAI-compatible endpoint. Configure in the app, in a
config file, or through environment variables.

**Integrations.** GitHub, Google Drive, Gmail, Notion, Asana and PayPal.

**A real sandbox.** See below — this is the part that matters most.

## Security model

AutoWork's central claim is: **it can only reach the folders you grant it.**

- **Deny by default.** A fresh install can read and write nothing. You grant folders explicitly,
  as read-only or read/write.
- **One chokepoint.** Every filesystem operation passes through `PathGuard`, which canonicalises
  the path, *resolves symlinks*, rejects OS and AutoWork-internal directories, requires
  containment in a granted root, and then applies the denied-pattern list.
- **Symlinks are resolved, not trusted.** A link inside a granted folder pointing at `~/.ssh`
  is refused. This is tested.
- **Credentials are blocked even inside granted folders** — `.ssh`, `.aws`, `.env`, `*.pem`,
  keychains and friends, by default.
- **Dangerous capabilities are opt-in.** Deleting, shell commands, mouse/keyboard control and
  MCP servers are all off until you turn them on, and approval-gated by default when you do.
- **Consent is inline and specific.** Requests appear in the flow of the work with the actual
  command or file list shown — not as a modal you learn to dismiss.

![A consent card asking permission before a file is written](docs/images/consent-card.png)

- **Everything is logged.** Every action lands in an append-only JSONL log, visible in the
  Activity view.

**Honest limits.** Input control (synthetic mouse and keyboard) cannot be sandboxed by this
process — once input is synthesised it goes to whatever window has focus. Shell commands run
with your full user privileges. An MCP server is a program AutoWork starts with those same
rights, and `PathGuard` cannot see inside it — which is why it takes its own switch, and why
each server stays disabled until you enable it. Both are off by default, and the controls around them are
consent and visibility rather than containment. On Windows the secret store is encrypted with
DPAPI; on Linux and macOS it falls back to owner-only file permissions. See
[docs/en/security.md](docs/en/security.md) for the full picture.

## Install

**Windows**

```powershell
git clone https://github.com/gravicode/autowork.git
cd autowork\install
.\install.ps1 -Desktop
```

**macOS / Linux**

```bash
git clone https://github.com/gravicode/autowork.git
cd autowork/install
chmod +x install.sh && ./install.sh
```

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download). Full guide:
[docs/en/installation.md](docs/en/installation.md).

## First run

1. **Settings › Models** — add a provider and paste an API key (or point it at a local Ollama).
2. **Settings › Permissions** — grant AutoWork a folder to work in.
3. **Work** — describe a job and press *Start work*.

![Settings › Models, with an Azure OpenAI model configured](docs/images/settings-models.png)

The interface follows your system theme, and you can pin it to light or dark:

![The same view in the light theme](docs/images/work-tape-light.png)

If a provider key is already in your environment (`ANTHROPIC_API_KEY`, `OPENAI_API_KEY`,
`OLLAMA_HOST`, …), AutoWork picks it up on first launch and step 1 is already done.

## Configuration

Three ways, in increasing precedence: **config file → environment variables → the app's UI.**

```bash
export ANTHROPIC_API_KEY=sk-ant-...      # any vendor key is auto-detected
export AUTOWORK_FOLDERS="$HOME/Documents;$HOME/Downloads"
export AUTOWORK_ALLOW_SHELL=false
```

Config lives at `%APPDATA%\AutoWork\config.json` (Windows), `~/.config/AutoWork/` (Linux),
`~/Library/Application Support/AutoWork/` (macOS). API keys are never written to it — only a
reference to the secret store or an environment variable. Full reference:
[docs/en/configuration.md](docs/en/configuration.md).

## Build from source

```bash
dotnet restore AutoWork.slnx
dotnet build AutoWork.slnx
dotnet test tests/AutoWork.Tests/AutoWork.Tests.csproj
dotnet run --project src/AutoWork.Desktop/AutoWork.Desktop.csproj
```

Run a single test:

```bash
dotnet test --filter "FullyQualifiedName~SandboxTests.A_symlink_pointing_out_of_the_granted_folder_is_refused"
```

## Project layout

```
src/
  AutoWork.Core           Config, secrets, permissions, action log, knowledge bases
  AutoWork.Providers      Multi-provider LLM factory, native Anthropic client, embeddings
  AutoWork.Tools          Files, shell, documents, data, images, screen, input, web
  AutoWork.Agents         Orchestrator, planner, sub-agents, context compaction, vision
  AutoWork.Integrations   Connector framework and connectors
  AutoWork.Desktop        Avalonia UI
tests/AutoWork.Tests      xUnit
install/                  Installers and uninstallers
docs/en · docs/id         Documentation, English and Bahasa Indonesia
```

## Documentation

| | English | Bahasa Indonesia |
|---|---|---|
| Installation | [docs/en/installation.md](docs/en/installation.md) | [docs/id/instalasi.md](docs/id/instalasi.md) |
| Configuration | [docs/en/configuration.md](docs/en/configuration.md) | [docs/id/konfigurasi.md](docs/id/konfigurasi.md) |
| Security | [docs/en/security.md](docs/en/security.md) | [docs/id/keamanan.md](docs/id/keamanan.md) |
| Architecture | [docs/en/architecture.md](docs/en/architecture.md) | [docs/id/arsitektur.md](docs/id/arsitektur.md) |
| Integrations | [docs/en/integrations.md](docs/en/integrations.md) | [docs/id/integrasi.md](docs/id/integrasi.md) |
| Troubleshooting | [docs/en/troubleshooting.md](docs/en/troubleshooting.md) | [docs/id/pemecahan-masalah.md](docs/id/pemecahan-masalah.md) |

Roadmap: [PLAN.md](PLAN.md) · Development status: [Progress.md](Progress.md)

## Credits

**AutoWork is built by [Gravicode Studios](https://github.com/gravicode), led by Kang Fadhil.**

Third-party components: [Avalonia](https://avaloniaui.net),
[Microsoft.Extensions.AI](https://github.com/dotnet/extensions),
[ClosedXML](https://github.com/ClosedXML/ClosedXML),
[Open XML SDK](https://github.com/dotnet/Open-XML-SDK),
[QuestPDF](https://www.questpdf.com) (Community licence),
[PdfPig](https://github.com/UglyToad/PdfPig),
[ImageSharp](https://sixlabors.com/products/imagesharp/),
[OllamaSharp](https://github.com/awaescher/OllamaSharp),
[CsvHelper](https://joshclose.github.io/CsvHelper/).
