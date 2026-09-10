# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Current state

**v1.1.0 is published to NuGet** (tag `officenet-v1.1.0`). **v1.2 (performance) and v1.3 (renderer)
are both complete, and 1.3.0 is on NuGet**; v2.0 is in progress — the HTML engine now lives in
`OfficeNet.Core.Html`, and `WordNet.Import.HtmlToWord` and `WordNet.Export.WordToHtml` exist. All six
libraries build clean with zero warnings: `OfficeNet.Core`, `PdfNet`, `WordNet`, `ExcelNet`,
`PowerPointNet`, `OfficeNet` (meta) and `OfficeNet.Rendering`. **729 tests pass** across seven
test projects.

Everything the plan listed through v1.1 exists: the libraries, tests with independent structural
validators, `docs/` and its `docs/id/` mirror, `notebooks/`, `benchmarks/`, the five `samples/`
apps, `../.github/workflows/`, ExcelNet charts and pivot tables, the format registry in
`src/OfficeNet/OfficeFormats.cs`, and `tools/ScreenshotGen`. Video export is deliberately not done,
and the reason is recorded in `Plan.md`.
[Progress.md](Progress.md) is the authoritative checklist; [Plan.md](Plan.md) is the roadmap.

An earlier version of this section listed most of the above as unwritten. If something here reads as
stale, trust `Progress.md`.

`Requirements.md` is the specification of record and is tracked — it holds no credentials. Local
SDK is **10.0.400**; target is **.NET 10**.

**This project lives at `Vibe_Office/OfficeNet` in a monorepo.** GitHub reads workflows only from
the repository root, so OfficeNet's CI and release workflows are at
`../.github/workflows/officenet-*.yml`, not beside this file. They set
`defaults.run.working-directory: OfficeNet`, filter on `paths: OfficeNet/**` so a sibling project's
commit does not build this one, and the release trigger is the namespaced tag `officenet-v1.2.3`.

The spec's only publish target is **NuGet**. There is no npm or PyPI deliverable; an earlier note in
this file claimed otherwise and was wrong.

## Architecture

```
OfficeNet.Core ──┬── PdfNet ──┬── WordNet
                 │            ├── ExcelNet ── Gravicode.Science.GraviFrame (NuGet)
                 │            └── PowerPointNet
                 │
                 └── OfficeNet.Rendering (SkiaSharp — the only native dependency)
```

`OfficeNet.Rendering` is a **separate package on purpose**. It is the one component that needs a
native dependency, and keeping SkiaSharp out of the other five is what makes "identical output on
every platform" true. Do not move it into the core to save a package.

`PdfNet` is the leaf and must stay that way — Word, Excel and PowerPoint all export *through* it.
`OfficeNet.Core` owns the OPC container, which is genuinely identical across .docx/.xlsx/.pptx, so
packaging, relationships, units, colour, image sniffing and metadata are written once there.

**ExcelNet does not reimplement pandas.** `DataFrames/DataFrameBridge.cs` is the entire integration
surface with `Gravicode.Science.GraviFrame` (on NuGet, 1.0.0; source at
`C:\Users\mifma\Documents\CodeSandbox\GravicodeScience`). Read that repo's `CLAUDE.md` before
touching the data layer.

### Two different models, on purpose

- **WordNet edits the XML tree live.** A `Paragraph` holds its own `w:p` element. Two handles to one
  paragraph cannot disagree, and parts the library does not understand survive a round trip byte for
  byte.
- **ExcelNet parses into a model and writes it back.** A worksheet's XML is a flat list of rows with
  nothing worth preserving, and a dictionary makes random cell access O(1) instead of a scan.

Don't "unify" these. The trade is different in each case.

### Naming

`PdfNet` was already taken on NuGet, so per the spec **every** package carries the prefix:
`Gravicode.OfficeNet.Core`, `.PdfNet`, `.WordNet`, `.ExcelNet`, `.PowerPointNet`. Assembly names
match the package ids; root namespaces do not (`WordNet`, `ExcelNet`, `PdfNet`, `OfficeNet.Core`).

## Things that will bite you

Every item here cost real debugging time. None are hypothetical.

### OOXML in general

- **Element order inside a properties element is a schema *sequence*, not a choice.** Word rejects a
  `w:rPr` that lists `w:sz` before `w:b` as unreadable content — it does not reorder or ignore.
  Every setter goes through `XmlUtil.SetOrdered` with the order list from `RunFormat.Order` /
  `ParagraphFormat.Order`. Same for `w:sectPr` (header/footer references must come *first*) and for
  `worksheet` (`mergeCells` after `sheetData`, never before).
- **`SetOrdered`'s order list must name the *successors*, not just the element being written.**
  `GetOrCreate(row, w:trPr, [w:trPr])` finds no element that must come after `w:trPr`, so it appends
  — and the row properties land after the last `w:tc`, which Word reports as repairable. The
  container sequences matter as much as the property bags: `w:tbl` is `tblPr, tblGrid, tr…`, `w:tr`
  is `tblPrEx?, trPr?, tc…`, `w:tc` is `tcPr?` then blocks. `WordTests.Validate` now checks all
  three; it previously checked only `w:rPr` and `w:pPr`, which is how this got shipped.
- **A `w:gridCol` with no `w:w` is legal and useless.** Consumers guess, and this library's own PDF
  exporter reads the grid to lay columns out. Tables get equal shares of the section's content width.
- **A toggle property is tri-state.** `null` = inherit, `false` = explicitly off. Collapsing them
  into `bool` makes it impossible to write an unbolded word inside a bold heading.
- **`w:sz` means half-points on a run and eighths of a point on a border.** Same attribute name,
  two units.
- **`xml:space="preserve"` or leading/trailing spaces are lost** — `XmlUtil.TextElement` handles it
  for `w:t` and `SharedStrings.Write` for `t`.
- **Strict-conformance files use different namespace URIs.** Without
  `XmlUtil.NormalizeStrictNamespaces` at load, a strict .docx opens with zero paragraphs, silently.

### ExcelNet

- **`new CellFont()` on a record struct does NOT run the primary constructor.** It zeroes the
  struct; parameter defaults never apply. `CellFont.Name`/`SizePoints` are therefore backed by
  fields with normalising accessors, `CellFont` has hand-written value equality (or dedup breaks),
  `FillPattern.Solid` and `VerticalAlignment.Bottom` are the zero values, and `CellStyle.Locked` is
  stored inverted. Undo any of that and either the stylesheet throws on a null font name or the
  style table fills with duplicates.
- **`XmlReader.ReadElementContentAsString()` advances *past* the end tag.** The caller's next
  `Read()` then skips the following sibling. In a cell that sibling is the `<v>` after an `<f>`, so
  every formula's cached result reads back empty. `SheetXml.ReadElementText` leaves the reader on
  the end tag instead — do not "simplify" it back.
- **Fill index 0 must be `none` and index 1 `gray125`.** Excel hard-codes both.
- **`applyFont`/`applyFill`/`applyBorder`/`applyNumberFormat` must be set** or Excel ignores the
  format entirely. This is the top reason hand-written styles "do nothing".
- **A solid fill's colour goes in `fgColor`, not `bgColor`.** Writing `bgColor` renders white.
- **A date is a number plus a number format.** Telling them apart requires the stylesheet, which is
  why styles are read before any sheet.
- **The date epoch is 1899-12-30** and serial 60 is the phantom 29 Feb 1900.
- **Excel's `ROUND` is away-from-zero**, not banker's; `MOD` takes the divisor's sign; `-2^2` is 4
  (unary minus binds tighter than `^`).
- **Column width is "characters of the digit zero"**, not a length — see `ExcelToPdf.ColumnPoints`.

### PdfNet

- **`/Length` is routinely an indirect reference,** and routinely wrong. The parser verifies that
  `endstream` really follows before trusting it.
- **`/P` must be hashed raw for the legacy key derivation.** Masking the reserved high bits before
  `ComputeLegacyKey` produces a key that is wrong for every RC4 and AES-128 document. The masked
  form is only for the `Permissions` property.
- **Objects inside an object stream are not individually encrypted** — the container already was.
  Decrypting twice yields garbage that still parses.
- **An XRef stream is never encrypted.**
- **`/Predictor` on a Flate stream is not optional.** Ignoring it inflates cleanly and gives
  completely wrong bytes; in an xref stream that means every object offset is garbage.
- **A composite font with no `/ToUnicode` genuinely cannot be decoded.** Emitting the raw code
  produces CJK-looking noise; emitting nothing is honest.
- **Render mode 3 is invisible text and must still be extracted** — it is a scanner's OCR layer.
- **Highlight quad points go upper-left, upper-right, lower-left, lower-right.** Clockwise draws a
  bowtie.
- **A JPEG needs no conversion (DCTDecode *is* the file), and neither does a PNG** — Flate with
  `/Predictor 15` is exactly PNG's own compression, so IDAT transfers byte for byte. Only alpha
  needs real work.

### Word → PDF export

- **Style inheritance is where a naive converter loses everything.** A Heading 1 paragraph carries
  no direct formatting; a run reading only its own `w:rPr` renders body text.
- **A field's cached result is stale.** A generated document's `PAGE` field says "1" on every page,
  so the exporter substitutes markers and fills the real numbers in per page.
- **A header/footer part is created empty and given its mandatory paragraph at save time.** Shipping
  the paragraph in the template puts a blank line above every header a caller writes.

### Charts (PowerPointNet)

- **`ChartXml.Read` must read `c:pt` by its `idx`, not in document order.** The points are allowed to
  be sparse; reading them in order shifts every value after a gap, turning a missing measurement into
  wrong data. Blanks come back as `NaN`, matching the writer's `dispBlanksAs="gap"`.
- **Label flags and the number format live on the chart group and the value cache, not on `c:ser`.**
  A reader that only walks the series loses `ShowDataLabels` and `ValueFormat`, and every exported
  chart comes out unlabelled with raw numbers.

### Documentation

`tests/OfficeNet.Docs.Tests/DocSamples.cs` **is** the code in `docs/`. Every sample compiles and
runs there. When an API is renamed that project stops building, which is the point — it caught 16
wrong API names in the first draft of the docs. Update the sample and the page together.

Screenshots in `docs/screenshots/` are generated by `tools/ScreenshotGen`, never captured from a
viewer, so they cannot drift from what the code produces:

```powershell
dotnet run --project tools/ScreenshotGen -c Release
```

## Commands

```powershell
dotnet build OfficeNet.sln -c Release
dotnet build src/ExcelNet/ExcelNet.csproj -v q --nologo    # one project, quiet
dotnet test                                                # once tests exist
dotnet test tests/WordNet.Tests --filter "FullyQualifiedName~Tables"
```

## Verifying document output

A .docx this library writes and this library reads is **not** evidence that Word can open it. Every
format change must be checked against an independent reader. The structural checks used so far —
content-type coverage, relationship targets resolving, schema child order, `w:sectPr` placement,
non-empty `w:tc`, Excel's reserved fill indices, shared-string counts — are currently ad-hoc python
scripts and should be ported into the test projects (see Progress.md).

## Attribution

Source headers and documentation carry: *Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.*

## Packaging

Metadata lives once in `Directory.Build.props`. `PublishRepositoryUrl` must stay unset, or SourceLink
overwrites `RepositoryUrl` with the git origin and loses the deep link to the OfficeNet
subdirectory. `IsPackable` is false by default and opted into per project under `src/`.
