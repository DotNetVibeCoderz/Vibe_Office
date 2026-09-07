# Samples

Five applications, each showing a different shape of document work.

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

| Sample | Kind | What it shows |
|---|---|---|
| [`BatchConverter.Console`](BatchConverter.Console) | CLI | Converting a folder to PDF, and surviving the one bad file in it |
| [`EtlPipeline.Console`](EtlPipeline.Console) | CLI | CSV in; workbook, Word report and slide deck out |
| [`OfficeNet.Dashboard`](OfficeNet.Dashboard) | Blazor Server | Upload, preview, and an x-ray of the OPC container |
| [`OfficeNet.Gallery`](OfficeNet.Gallery) | Avalonia | Every feature running beside the code that produced it, plus an assistant |
| [`OfficeNet.Editor`](OfficeNet.Editor) | Avalonia | A light Word/Excel editor with a live rendered page |

Build everything first:

```bash
dotnet build OfficeNet.sln -c Release
```

---

## BatchConverter.Console

```bash
dotnet run --project samples/BatchConverter.Console -- ./documents -r -o ./output -t
```

Converts `.docx`, `.xlsx`, `.pptx` and `.pdf` to PDF, optionally writing a PNG thumbnail beside
each one.

The point of the sample is not the conversion — that is one call — it is everything around it: a
malformed file is caught, recorded and reported at the end rather than taking the run down, and the
exit code is `0` when everything converted, `1` when anything failed, and `2` for a bad command
line, so a scheduler can act on it.

`--parallel` is opt-in rather than the default. Conversion is CPU-bound and each document holds a
full object graph, so one per core is fast but multiplies peak memory by the core count — which is
how a batch job that worked on a laptop dies in a small container.

## EtlPipeline.Console

```bash
dotnet run --project samples/EtlPipeline.Console -- ./output
```

Generates 500 rows of Indonesian-format CSV, aggregates them, and writes:

- `02-laporan.xlsx` — a formatted workbook with live `SUM` formulas and a colour scale
- `03-laporan.docx` — a narrative report with a shaded table and page-number fields
- `04-deck.pptx` — a deck with native column, line and pie charts

…each alongside its PDF. Four libraries in one pipeline, which is what document automation actually
looks like.

![The Word report the pipeline produces](../docs/screenshots/wordnet-document.png)

## OfficeNet.Dashboard

```bash
dotnet run --project samples/OfficeNet.Dashboard
```

Then open the URL it prints. Add `?demo=1` to load three generated documents immediately.

![The dashboard, with a deck loaded](../docs/screenshots/sample-dashboard.png)

Upload a document and it shows the rendered pages, the metadata, the extracted text — and the
**package anatomy**: every part in the OPC container with its content type and size, and the whole
relationship graph. That last panel is the reason the sample exists. A `.docx`, `.xlsx` and `.pptx`
are the same ZIP-of-XML container with different contents, and seeing that is the fastest way to
understand what these libraries are doing.

A PDF shows no anatomy, on purpose: it is not an OPC package, and inventing a structure it does not
have would teach the wrong thing.

## OfficeNet.Gallery

```bash
dotnet run --project samples/OfficeNet.Gallery
```

![The gallery: catalog, output, and the source that produced it](../docs/screenshots/sample-gallery.png)

Ten demos across the four libraries. Each runs when you select it and shows its output beside its
source — and that source is **read out of the embedded demo file at runtime**, so it is literally
the code that just executed rather than a snippet someone copied and forgot to update.

### The assistant

![The assistant, with sessions and example prompts](../docs/screenshots/sample-gallery-chat.png)

A Semantic Kernel chat that knows the four libraries and their traps. It supports several sessions
at once — add, remove and reset from the left rail — and the right column offers example questions
grouped by library, so there is always something to click rather than a blank box to fill.

**Providers** are configured entirely through environment variables. Nothing is stored in the
repository and nothing is typed into the app:

| Provider | Variables | Tool calling |
|---|---|---|
| OpenAI | `OPENAI_API_KEY` | yes |
| Azure OpenAI | `AZURE_OPENAI_API_KEY`, `AZURE_OPENAI_ENDPOINT` | yes |
| Anthropic | `ANTHROPIC_API_KEY` | no — see below |
| Google Gemini | `GEMINI_API_KEY` | yes |
| DeepSeek | `DEEPSEEK_API_KEY` | yes |

Web search additionally needs `TAVILY_API_KEY`. Without it the search function says so rather than
failing silently.

**Kernel functions** the assistant can reach: `web_search` (Tavily), `fetch_page` (retrieve a URL
and reduce it to readable text), `current_datetime` and `days_between`, `calculate` and
`percentage_change`.

Verified against live models: Azure OpenAI `gpt-5-mini` and DeepSeek `deepseek-v4-flash`, with the
maths, date and search functions all firing.

Two things this sample learned the hard way, both recorded in the code:

- **Anthropic has no official SK connector**, so `AnthropicChatCompletionService` speaks the
  Messages API directly. Its system prompt is a top-level field rather than a message,
  `max_tokens` is required, and authentication is `x-api-key` plus `anthropic-version`. Tool
  calling is deliberately not implemented there rather than advertised and quietly ignored.
- **Reasoning-tuned models reject a non-default `temperature`.** `gpt-5-mini` answers a request
  carrying `Temperature = 0.2` with `HTTP 400 unsupported_value`, so the sample sets no temperature
  at all.

## OfficeNet.Editor

```bash
dotnet run --project samples/OfficeNet.Editor
```

Open a `.docx` or `.xlsx`, edit the text on the left, and see the real rendered page on the right —
rendered through the same PDF exporter the libraries ship, not an approximation drawn by the app.
In a workbook, a line starting with `=` is a formula.

Formulas are recalculated on **Refresh preview** rather than on every keystroke: the engine walks
the dependency graph, and doing that per character makes typing feel heavy.

---

## What the samples are not

None of them is a product. The dashboard holds uploaded documents in the circuit's memory and
base64-encodes page images into the page; the editor exposes one flat list of lines rather than a
real editing surface; the gallery saves to a fixed folder. Each is shaped to show one idea clearly,
and each says in its code where it would need to change for real use.
