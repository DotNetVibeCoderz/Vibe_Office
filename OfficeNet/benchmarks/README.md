# Benchmarks

BenchmarkDotNet suites for all four libraries, plus the OPC container they share.

Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

```bash
dotnet run --project benchmarks/OfficeNet.Benchmarks -c Release -- --filter '*'
dotnet run --project benchmarks/OfficeNet.Benchmarks -c Release -- --filter '*Word*' --job short
```

`--job short` finishes in a couple of minutes and is right while iterating. Leave it off for a
number you intend to publish.

One project with a class per library rather than four projects: the alternative was four
near-identical `.csproj` files differing only in a `ProjectReference`, and a filter does the same
job.

Every benchmark serialises to a `MemoryStream`. A benchmark that touches the file system measures
the file system.

## What running them found

Benchmarks earn their keep by finding things, and these found two quadratic paths and six wasteful
ones that no test could see — a test asserts a result, and every one of these produced perfectly
correct output.

### Splitting copied the whole document into every part

`Split` makes one document per page by importing that page's dictionary. A page dictionary carries
`/Parent`, and that parent's `/Kids` names **every page in the source** — so importing one page
dragged the entire document in behind it.

The output was correct: each part held the right page and rendered identically. It also held every
*other* page as an orphan, so a 100-page file split into 100 parts gave 100 files the size of the
original, and the split itself was quadratic.

| Pages | Time before | Time after | Allocated before | Allocated after |
|---|---|---|---|---|
| 10 | 337 us | 127 us | 417 KB | 141 KB |
| 100 | 39,759 us | 1,129 us | 29.9 MB | 1.3 MB |

Ten times the work now costs about nine times the time instead of a hundred and eighteen. A
single-page part of a 100-page document went from 305 objects and 67.5 KB to **6 objects and
1.1 KB**.

The fix is one line: do not follow `/Parent` when importing a page. The old parent is meaningless in
the new document anyway — `PdfPageCollection.Flush` sets `/Parent` to the new page tree.

**The trap in testing it.** A page only gains its `/Parent` when the page tree is written, so a
document built in memory and never saved has no parent link to follow, and the bug is invisible.
The first version of the regression test built its document in memory and passed against the broken
code. It now round-trips through bytes first, which is what a caller does before splitting anything.

### Word to PDF drew one word at a time

Line breaking works word by word, so the exporter had a list of words and drew each one. Every word
therefore carried its own font, colour and text-positioning operators.

A probe that isolated the canvas — same document, same words, only the number of `DrawText` calls
changed — put a number on it:

| 24 000 words | Allocated | Per word |
|---|---:|---:|
| One `DrawText` each | 16.3 MB | 713 B |
| One `DrawText` per twelve | 3.4 MB | 147 B |

So the canvas was 713 of the ~1 350 bytes the export spent per word. Consecutive pieces that would
be drawn identically are now drawn as one:

| Paragraphs | Time before | After | Allocated before | After | PDF before | After |
|---|---:|---:|---:|---:|---:|---:|
| 100 | 4.4 ms | 2.6 ms | 2.61 MB | 1.78 MB | 12,636 B | 7,298 B |
| 1 000 | 47 ms | 20 ms | 24.1 MB | 16.0 MB | 116,750 B | 64,038 B |
| 10 000 | 449 ms | 253 ms | 241 MB | 159 MB | 1,161,639 B | 634,591 B |

The file is 45% smaller, which the caller sees whether or not they care about the export's speed.

**Why it is exact rather than close enough.** `SplitWords` keeps each word's trailing space
attached, so concatenating consecutive pieces reproduces the text character for character; and a
piece's width is the sum of its glyphs' advances, so a merged run puts every glyph where the
per-word loop put it. Abutting highlight rectangles are one rectangle and abutting underlines one
line, for the same reason. Justified lines are the exception and are left alone — their spacing is
stretched, so their words are not contiguous.

That claim was checked rather than argued: the same document was exported by both versions across
all four alignments with bold, italic, underline, strike, colour, highlight and superscript
changing mid-line, and every line's start, end and text came back identical.

**A measurement that lied.** One `--job short` run reported the new code allocating 578 MB against
the old code's 241 MB — the opposite of the truth. A direct `GC.GetAllocatedBytesForCurrentThread`
probe said 158 MB, and re-running the benchmark said 159 MB. The first run was an artefact, and the
only reason it was caught is that two tools were asked the same question.

### Every line allocated a list that grew from four

With the drawing fixed, 140 of the export's remaining 159 MB was layout. Instrumenting the phases
rather than guessing at them said where:

| Phase | Allocated | Per unit |
|---|---:|---:|
| Building the line filler | 35.1 MB | 3.5 KB / paragraph |
| Taking the next line | 29.3 MB | 1.4 KB / line |
| Starting a page | 16.1 MB | 35 KB / page |
| Building the segments | 15.3 MB | 1.5 KB / paragraph |
| Drawing the line | 15.1 MB | 740 B / line |

The top two are the same mistake in two places. `List<T>` starts at four entries and doubles,
copying and abandoning the array each time, so a list built to hold a dozen words allocates four
arrays totalling twenty-eight slots to store twelve — and both the filler's word list and the
per-line piece list were built that way, once per paragraph and once per line.

The line list is now handed to the filler by the caller, so the paragraph loop reuses one for the
whole document; the filler's word list is sized up front from the text length. Nothing else changed.

| Paragraphs | Time | Allocated |
|---|---:|---:|
| 100 | 2.6 → 2.4 ms | 1.78 → 1.42 MB |
| 1 000 | 20 → 17 ms | 15.96 → 12.38 MB |
| 10 000 | 253 → 235 ms | 159 → 123 MB |

**Reuse needs an argument, not a hope.** The line list is safe to share because it is live only
from the moment the words are taken to the moment they are drawn. The filler is *not*: it stays
live across `EnsureSpace`, and starting a page draws that page's footnotes and deferred floats,
which lay out text of their own. Sharing the filler would have corrupted a document that paginates
while leaving every small test green.

The guard for that had to be sharpened before it worked. The first version asserted every
paragraph's text was present in the output, which a stale buffer passes trivially — it repeats
words rather than losing them. It now counts occurrences, and a deliberately removed `Clear()`
fails it.

**Together with the drawing fix**, against where this started: 10 000 paragraphs went from 449 ms
and 241 MB to 235 ms and 123 MB, and the file from 1,161,639 bytes to 634,591.

### Resolving a paragraph's style rescanned the whole style part

With the lists fixed, re-instrumenting moved the largest remaining item to a place the first pass
had lumped into "everything else": `Resolve`, which works out a run's effective formatting, cost
1.1 KB per call and is called once per paragraph and again for every run.

Two things inside it. Walking a style's `basedOn` chain allocated a set to catch cycles, an
iterator, and a buffer for the reversal — and each link called the style indexer, which is a linear
scan of every style in the part that wraps the element it finds. A document has a handful of styles
and a great many paragraphs, so the same three or four chains were rebuilt tens of thousands of
times.

They are now walked once each and cached, root first so no caller has to reverse anything.
**10 000 paragraphs: 123 → 111 MB, 235 → 228 ms.**

Caching a chain is only sound if it is the chain the walk would have produced, so
`WordNet.Tests.StyleChainTests` pins the two properties that are easy to lose: inheritance applies
root first, and a chain that points back at itself terminates rather than hanging. Dropping the
reversal fails the first; keying the cache on a constant fails the second.

### A page's content was copied four times on the way out

The canvas builds a page's operators in a `StringBuilder`, then handed them over as
`Encoding.Latin1.GetBytes("q
" + _content + "Q
")`. That concatenation asks the builder for a
string, builds a second string around it, and encodes that — three copies of the page's content
before the byte array that is actually kept.

It now narrows the builder's own chunks straight into the destination array. Content-stream
operators are Latin-1 by definition, so narrowing each chunk *is* the encoding.

| One A4 page | Allocated before | After |
|---|---:|---:|
| Blank canvas, opened and closed | 9.7 KB | 9.7 KB |
| One line of text | 13.0 KB | 12.7 KB |
| Forty-five lines of text | 52.8 KB | 37.7 KB |

The saving is proportional to what is on the page, which is worth saying plainly: dense pages gain,
sparse ones barely move. Exporting 10 000 Word paragraphs went from 111 to 104 MB; exporting 200
slides went from 20.6 to 20.1 MB, because a slide carries little text and the fixed cost of the
builder dominates. The PowerPoint timings moved around by more than the change could account for —
the error bars on that run were wider than the difference — so only the allocation figure is
reported.

**Verified by hashing, not by reasoning.** The point of the change is that the bytes are the same
and only their assembly differs, so a document covering all four alignments and every character
format was exported both ways and each page's decoded content stream hashed. All six matched.

`PdfNet.Tests.CanvasContentTests` guards the two ways hand-assembly goes wrong: content spanning
many builder chunks must survive every seam, and Latin-1 characters must be narrowed rather than
encoded as UTF-8. Writing every chunk at the same offset fails the first; swapping the encoder
fails the second.

### Laying out a line copied the text it was laying out

Line breaking works word by word, so the filler held a list of words. Each entry was the word cut
out as its own string, plus a copy of the resolved formatting, plus the font and the width — about
2.8 KB per paragraph, and the largest single item left in the export.

A piece now names a range: which segment, where it starts, how long it is, and how wide. The text
is fetched only when it is drawn, and by then neighbouring pieces have usually been merged, so one
substring comes out per run instead of one per word.

| Paragraphs | Allocated before | After |
|---|---:|---:|
| 100 | 1.23 MB | 1.01 MB |
| 1 000 | 10.45 MB | 8.26 MB |
| 10 000 | 104 MB | 82 MB |

Merging got simpler as well as cheaper. Neighbouring words of one segment are one unbroken range by
construction, so the test for "can these be drawn together" is `same segment && contiguous` — no
comparison of fonts, colours and decorations, and no possibility of that comparison being
incomplete.

**Byte-identical output, checked by hashing.** Every page's decoded content stream was hashed before
and after, across all four alignments with bold, italic, underline, strike, colour, highlight and
superscript changing mid-line. All six pages matched.

**A branch that could not run.** Trimming a leading space off a piece looked necessary, and it had
been there since the string version. It is unreachable: words are split so a space is only ever the
*last* character of a piece, so a piece beginning with a space holds nothing else and is dropped
whole. Putting a `throw` in the branch and running the suite proved it rather than argued it, and
the invariant is now stated where the code relies on it.

**Two guards that guarded nothing.** The first version of `LinePieceRangeTests` asserted that no
line starts with a space, using text with doubled spaces — but a single space almost always fits at
the end of the line before it, so nothing ever landed at a line start and deleting the check that
prevents it still passed. Sixty consecutive spaces cannot fit on one line, and with those the test
fails as it should. Mutation testing is what surfaced this; the tests read fine.

### Extracting text rebuilt the model once per paragraph

Measuring the three items still marked hypothesis put a number on each. Opening a 34 KB, ten
thousand paragraph .docx and extracting its text allocated **39.7 MB — 1 205 times the file**.

Taking that apart:

| | Allocated | Ratio to file |
|---|---:|---:|
| Open, touch nothing | 10.2 MB | 311x |
| Open + `Paragraphs.Count` | 12.6 MB | 383x |
| Open + `Paragraphs.Count` five times | 22.2 MB | 673x |
| Open + `ExtractText` | 39.7 MB | 1 205x |
| (unzip + `XDocument.Load` alone) | 4.5 MB | 135x |

The middle rows are the finding. `Paragraphs` costs **2.4 MB every time it is read** — it
materialises a fresh list and a fresh wrapper per paragraph on each access, the same shape as the
table-indexer bug fixed earlier, in the library's most-used property. `ExtractText` walked it, then
called `Paragraph.Text`, which walks `Runs` — another fresh list and wrapper per run — and returned
a string that was immediately appended to another builder and thrown away.

Extraction now walks the body's elements directly into one builder. `Paragraph.Text` and `Run.Text`
share the same walk, so a caller assembling a document pays no string per run.

**`ExtractText`'s own cost: 29.5 MB → 10.5 MB.** What is left is close to inherent — the document's
text is about 3 MB as UTF-16, and the result is one string.

Walking elements means restating which of them count, which is where such a rewrite goes quietly
wrong: a run inside a hyperlink is text, a tab is a tab, a break is a newline, and both hyphens are
characters rather than markup. Prose exercises none of those, so `WordNet.Tests.TextExtractionTests`
pins the output character for character on a document that contains all of them. Dropping hyperlink
runs fails three of its four cases; ignoring tabs fails two.

### Two hypotheses the measurements rejected

**`Span<T>` in the PdfNet lexer.** Opening a PDF allocates 13.6x its file size at 50 pages and 13.7x
at 200 — small and flat. The lexer is not a hot path, and the expensive part of reading a PDF is
text extraction at a flat 242 KB per page, which is a different code path. Not worth doing on these
numbers.

**Buffer pooling when writing a package.** Writing a workbook allocates 152x the output at 10 000
rows and 146x at 50 000 — linear, with no scaling problem to fix. Whether pooling would help is a
separate question from whether the current cost is wrong, and nothing here says it is.

Both stay in the plan as ideas rather than as work, which is what measuring them was for.

### Appending a paragraph was O(number of blocks)

`InsertBlock` kept the body's final `w:sectPr` last by finding it and calling `AddBeforeSelf`. LINQ
to XML stores children as a **singly linked list**, so inserting *before* a node means walking from
the front to find its predecessor. Building an N-paragraph document was therefore O(N²).

Measured per 1 000 paragraphs appended into a document that already held *n*:

| Already in the body | Before | After |
|---|---|---|
| 1 000 | 6.4 ms | 1.6 ms |
| 4 000 | 28.5 ms | 1.4 ms |
| 8 000 | 69.8 ms | 1.1 ms |
| 12 000 | 109.5 ms | 1.1 ms |

The fix remembers the block appended last and uses `AddAfterSelf`, which needs only the node's own
`next` pointer and is O(1). The guards re-verify the shape on every call, so anything else editing
the body falls back to the slow path rather than corrupting the order.

**`WordDocument` create + save, 10 000 paragraphs: 1 576 ms → 99 ms.**

### The table indexer allocated a wrapper per row and per cell

`table[r, c]` went through `Rows[row].Cells[column]`, and both properties materialise a fresh list
of wrappers for the whole row or table on every access. Filling a 500-row table cell by cell
allocated 55 MB of throwaway objects.

**500-row table, cell by cell: 92.4 ms → 9.3 ms; 55 490 KB → 7 209 KB allocated.** It is now on par
with the bulk `AddTable(string[][])` overload rather than 7× slower.

Both are guarded by `WordNet.Tests.ScalingTests`, which compares the time for N against 4N rather
than asserting a wall-clock budget — a ratio near 4 is linear, and the quadratic versions gave 16
and climbing.

## Results

Intel Core i7-8650U @ 1.90 GHz, 4 physical cores, Windows 11, .NET 10.0.11, `--job short`.

Treat these as orders of magnitude, not as a spec. A short job trades statistical confidence for
speed, and this is a laptop CPU that throttles.

### WordNet

| Operation | 100 paragraphs | 1 000 | 10 000 |
|---|---:|---:|---:|
| Create + save | 1.2 ms | 9.0 ms | 99 ms |
| Open | 0.3 ms | 2.3 ms | 23 ms |
| Extract text | 0.4 ms | 2.7 ms | 26 ms |
| Export to PDF | 2.2 ms | 15 ms | 190 ms |

PDF export is still the expensive one, and reasonably so: it resolves style inheritance, measures
every run against the font metrics, breaks lines and paginates. It allocates 82 MB for the
10 000-paragraph case, down from 241, and no longer reaches generation 2 at all — that column was
1 000 to 2 000 collections per thousand operations when this started.

Timings at this point are dominated by the machine rather than by the code: the short job reports
190 ms with an error of 374 ms on the same measurement that reported 181 ms before. Allocation is
the number to read here.

| Table, 4 columns | 50 rows | 500 rows |
|---|---:|---:|
| `AddTable(string[][])` | 1.4 ms | 7.1 ms |
| Cell by cell | 1.4 ms | 9.3 ms |

### ExcelNet

| Operation | 1 000 rows | 10 000 | 100 000 |
|---|---:|---:|---:|
| Write values | 12 ms | 179 ms | 1 174 ms |
| Write + recalculate formulas | 12 ms | 245 ms | 1 979 ms |
| Open | 6.9 ms | 99 ms | 698 ms |
| Open + sum a column | 6.0 ms | 73 ms | 575 ms |

Roughly linear across two orders of magnitude. Recalculating 100 000 formulas costs about 800 ms on
top of writing them.

| 10 000 styled cells | Time | Allocated |
|---|---:|---:|
| One shared style | 27.1 ms | 12.8 MB |
| A distinct style per 100 cells | 27.6 ms | 13.2 MB |

The stylesheet's deduplication means a caller who does not reuse styles pays almost nothing extra.

### PowerPointNet

| Operation | 20 slides | 200 slides |
|---|---:|---:|
| Create + save | 4.4 ms | 44 ms |
| Open | 2.4 ms | 12 ms |
| Export to PDF | 5.8 ms | 91 ms |

| Chart | 10 points | 100 points |
|---|---:|---:|
| Write the chart part | 1.9 ms | 4.5 ms |
| Read it back | 1.8 ms | 2.3 ms |

### PdfNet

| Operation | 10 pages | 100 pages |
|---|---:|---:|
| Draw + save | 2.4 ms | 38 ms |
| Open | 0.08 ms | 0.9 ms |
| Extract text | 2.5 ms | 34 ms |
| Merge two copies | 0.6 ms | 5.8 ms |
| Split into single pages | 0.7 ms | 69 ms |
| Encrypt (AES-256) | 10 ms | 12 ms |

Opening is nearly free because the parser is lazy — it reads the xref and materialises objects on
demand rather than parsing the whole file.

**`Split` is superlinear** (0.7 ms → 69 ms for 10× the pages) and is the next thing worth looking at:
each part currently re-imports the shared resource graph rather than importing it once. It has not
been fixed because nothing measured yet depends on it; see [Plan.md](../Plan.md).

### The container itself

| Operation | Time | Allocated |
|---|---:|---:|
| Open a minimal `.docx` | 160 μs | 76 KB |
| Detect format from bytes | 3.7 μs | 10.5 KB |
| Round trip with no edits | 881 μs | 173 KB |

Worth isolating: when opening a small document feels slow, this says whether the cost is in
WordprocessingML or in the ZIP and relationship graph underneath.

## Reading the output

BenchmarkDotNet writes `BenchmarkDotNet.Artifacts/results/` — Markdown, CSV and HTML per class. The
directory is gitignored; re-run rather than committing results, and update the tables above when a
number changes materially.
