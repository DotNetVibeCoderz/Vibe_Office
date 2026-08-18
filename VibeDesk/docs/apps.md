# The five apps

[← README](../README.md) · [Architecture](architecture.md) · [Configuration](configuration.md) · [API](api.md) · [Mr Clippy](assistant.md) · [Scripts](scripting.md) · [Development](development.md)

---

## Drive

![Drive](screenshots/04-drive-light.png)

The storage hub. Docs, Sheets, Slides and uploaded files are all `DriveItem` rows, so everything below
is shared by all four rather than reimplemented per app.

- Folder tree with breadcrumbs, grid and list views, sorting, and full-text search across names and
  extracted document text
- Starred, Recent, Shared with me, and Trash (soft delete, restorable)
- Sharing by email with **viewer / commenter / editor** roles, or by link with a scope and a link role
- Version history with one-click restore — the restore itself snapshots the current state first, so a
  restore is undoable and no work is ever lost
- Storage quota, enforced on upload; if the quota check fails the uploaded blob is rolled back rather
  than orphaned
- Move, copy (deep, including subtrees), rename, colour-tag

A grant on a folder is inherited by everything beneath it, resolved through the materialised ancestor
path rather than a recursive walk.

---

## Docs

![Docs](screenshots/05-docs-light.png)

Rich text over a `contenteditable` surface, stored as HTML.

- Formatting, headings, lists, links, tables, images; page setup for size, orientation, margins, and
  base typography
- **Comments** anchored to a text range, threaded, resolvable
- **Suggestions** — a comment that carries replacement text and can be accepted straight into the
  document body, which then bumps the revision so other editors rebase

The editor's central constraint: **Blazor never re-renders the contenteditable's content.** Assigning
`innerHTML` collapses the caret, so content goes in through JS on first render and on an explicit
revision change only, and the component has no dynamic children at all.

---

## Sheets

![Sheets](screenshots/02-sheets-light.png)

A real spreadsheet, not a styled table.

- **~110 functions** across maths, statistics, text, logic, lookup and dates
- Formula parsing by a hand-written lexer and recursive-descent parser with spreadsheet precedence:
  comparison → `&` → `+ -` → `* /` → `^` (right-associative) → unary → postfix `%`
- Cross-sheet references (`Data!A1`), absolute markers (`$C$5`), named ranges, and empty arguments
  (`IF(A1,,"no")`)
- **Cycle detection** — a dependency loop yields `#CIRCULAR!` rather than a stack overflow
- Charts (7 kinds, inline SVG), pivot tables, conditional formatting including colour scales
- Frozen rows and columns, number formats, cell notes

Evaluation is demand-driven with memoisation, so recalculation costs what the formulas cost, not what
the grid costs. The grid itself is virtualised — a 200-row default sheet renders only what is visible.

Charts ship with a legend **and** a table view. The data-series palette is a validated reference
palette rather than the brand colours: the brand teal cannot reach the required chroma at categorical
lightness in sRGB, and legibility of data outranks palette continuity.

Cell styles store fixed background colours while text uses a theme token, which in dark mode can put
white on white. `ContrastInk()` computes WCAG relative luminance per cell and picks the ink, so a
styled Total row stays readable in both themes.

---

## Slides

![Slides](screenshots/06-slides-light.png)

- Six themes — aurora, paper, midnight, mint, sunrise, mono
- Element types: text, image, video, audio, shape, chart, table, embed
- Per-slide transitions and per-element animations, speaker notes, hidden slides
- Presenter view and full-screen presentation
- Charts rendered live from a source spreadsheet, so a deck never carries a stale screenshot

Geometry is stored as **percentages** and font sizes in container-query units (`cqw`). One component
therefore renders the editor canvas, the thumbnail rail and the presentation surface identically, with
no layout recomputation per view.

---

## Calendar

![Calendar](screenshots/07-calendar-light.png)

- Month, week, day and agenda views
- Recurring events — daily, weekly by day, monthly, yearly, with interval and count/until
- Reminders, attendees with responses, all-day events, colour overrides, visibility
- Multiple calendars per user, shareable with the same role model as Drive
- Attach a Drive item to an event

**Recurrence is expanded on read**, and only *exceptions* are materialised. A series is one row plus a
row per divergence, rather than a row per occurrence — which is what keeps a daily standup from
becoming thousands of rows.

Times are stored in UTC and rendered in the viewer's zone. That distinction caused the one seeding bug
worth remembering: sample events built working hours from a *UTC* date, so a 09:30 standup landed at
02:00 local. The calendar was right; the data was wrong.
