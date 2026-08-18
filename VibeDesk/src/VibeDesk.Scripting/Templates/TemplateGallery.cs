using VibeDesk.Application.Scripting;
using VibeDesk.Domain;

namespace VibeDesk.Scripting.Templates;

/// <summary>
/// The starting points. A blank editor is the reason most automation never gets written, so the
/// gallery covers each app and each language with something that runs as-is against seeded data.
/// </summary>
/// <remarks>
/// Naming differs by language and that is deliberate rather than an oversight: JavaScript sees the
/// host API in camelCase (<c>api.drive.list()</c>), while Python and C# see the CLR spelling
/// (<c>api.Drive.List()</c>, <c>Api.Drive.List()</c>) because both call .NET objects directly.
/// </remarks>
public sealed class TemplateGallery : IScriptTemplateGallery
{
    public IReadOnlyList<ScriptTemplateDto> All { get; } = Build();

    public IReadOnlyList<string> Categories => [.. All.Select(t => t.Category).Distinct().Order()];

    public ScriptTemplateDto? Find(string id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ScriptTemplateDto> Search(
        string? keyword, string? category, ScriptLanguage? language)
    {
        IEnumerable<ScriptTemplateDto> query = All;

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        if (language is { } lang) query = query.Where(t => t.Language == lang);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(t =>
                t.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                t.Summary.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                t.Tags.Any(tag => tag.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
        }

        return [.. query];
    }

    private static ScriptTemplateDto T(
        string id,
        string name,
        string summary,
        string category,
        ScriptLanguage language,
        ScriptScope scopes,
        string code,
        string? hosts = null,
        params string[] tags) =>
        new(id, name, summary, category, language, scopes, hosts, code.Trim(), tags);

    private static List<ScriptTemplateDto> Build() =>
    [
        // ──────────────────────────────── Drive ────────────────────────────────

        T("drive-batch-rename", "Batch rename files", "Adds a prefix to every file in a folder.",
            "Drive", ScriptLanguage.JavaScript, ScriptScope.DriveRead | ScriptScope.DriveWrite,
            """
            // Set folderId and prefix, then run.
            var folderId = input.folderId || '';
            var prefix = input.prefix || '2026 — ';

            var files = api.drive.list(folderId, 500);
            var renamed = 0;

            for (var i = 0; i < files.length; i++) {
                var file = files[i];
                if (file.type === 'Folder') continue;
                if (file.name.indexOf(prefix) === 0) continue;   // already done; runs are idempotent

                api.drive.rename(file.id, prefix + file.name);
                renamed++;
            }

            console.log('Renamed ' + renamed + ' of ' + files.length + ' items.');
            renamed;
            """, null, "rename", "batch", "folder"),

        T("drive-auto-backup", "Auto-backup a folder", "Copies a folder into a dated backup folder. Pair with a nightly schedule.",
            "Drive", ScriptLanguage.CSharp, ScriptScope.DriveRead | ScriptScope.DriveWrite,
            """
            // Backs up one folder each run. Give it a daily trigger and forget about it.
            var sourceId = Input.TryGetValue("folderId", out var f) ? f : "";
            if (string.IsNullOrWhiteSpace(sourceId)) throw new Exception("Set folderId in the inputs.");

            var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var backupRoot = Api.Drive.CreateFolder($"Backup {stamp}");

            var items = Api.Drive.List(sourceId, 500);
            var copied = 0;

            foreach (var item in items)
            {
                Api.Drive.Copy(item.Id, backupRoot.Id, item.Name);
                copied++;
            }

            Log($"Backed up {copied} items into '{backupRoot.Name}'.");
            return new { folder = backupRoot.Name, copied };
            """, null, "backup", "schedule", "copy"),

        T("drive-usage-report", "Storage usage report", "Writes a breakdown of storage by file type into a new spreadsheet.",
            "Drive", ScriptLanguage.Python,
            ScriptScope.DriveRead | ScriptScope.SheetsWrite,
            """
            # Summarises what is taking up space, then leaves the numbers in a sheet.
            usage = api.Drive.Usage()
            files = api.Drive.Search("", None, 2000)

            by_type = {}
            for f in files:
                by_type[f.Type] = by_type.get(f.Type, 0) + f.Size

            rows = [[k, v, round(v / 1048576.0, 2)] for k, v in sorted(by_type.items())]

            sheet = api.Sheets.Create("Storage report " + api.Now()[:10])
            api.Sheets.SetRange(sheet.Id, "A1", [["Type", "Bytes", "MB"]] + rows)
            api.Sheets.SetCell(sheet.Id, "A" + str(len(rows) + 3), "Quota used %")
            api.Sheets.SetCell(sheet.Id, "B" + str(len(rows) + 3), round(usage.UsedPercent, 1))

            api.Log("Report written to " + sheet.Name)
            result = {"sheet": sheet.Id, "types": len(rows)}
            """, null, "report", "usage", "quota"),

        T("drive-find-large-files", "Find the largest files", "Lists the biggest files so you know what to clear out.",
            "Drive", ScriptLanguage.JavaScript, ScriptScope.DriveRead,
            """
            var files = api.drive.search('', null, 1000);

            files.sort(function (a, b) { return b.size - a.size; });

            var top = [];
            for (var i = 0; i < Math.min(20, files.length); i++) {
                var f = files[i];
                top.push({ name: f.name, mb: Math.round(f.size / 1048576 * 10) / 10, owner: f.owner });
                console.log((i + 1) + '. ' + f.name + ' — ' + top[i].mb + ' MB');
            }

            top;
            """, null, "cleanup", "storage"),

        // ──────────────────────────────── Sheets ────────────────────────────────

        T("sheets-sales-analysis", "Sales analysis", "Groups a sales sheet by region and writes the summary back.",
            "Sheets", ScriptLanguage.Python,
            ScriptScope.SheetsRead | ScriptScope.SheetsWrite,
            """
            # DataFrame covers what pandas would do here — IronPython cannot load C extensions,
            # so grouping and pivoting live in the host API instead.
            sheet_id = input["sheetId"]

            df = api.Sheets.Read(sheet_id)
            api.Log("Loaded %d rows: %s" % (df.Count, ", ".join(df.Columns)))

            by_region = df.GroupBy("Region", "Revenue", "sum").SortBy("sum(Revenue)", True)
            api.Log(by_region)

            total = df.Sum("Revenue")
            best = by_region.Cell(0, "Region") if by_region.Count > 0 else "n/a"

            api.Sheets.WriteFrame(sheet_id, by_region, "Summary")
            result = {"total": total, "bestRegion": best, "groups": by_region.Count}
            """, null, "analysis", "groupby", "pandas"),

        T("sheets-validate", "Validate rows", "Checks a sheet for missing fields and bad numbers, and reports what is wrong.",
            "Sheets", ScriptLanguage.CSharp, ScriptScope.SheetsRead | ScriptScope.Notify,
            """
            // Validation is the case C# is best at here: explicit rules, explicit messages.
            var sheetId = Input["sheetId"];
            var df = Api.Sheets.Read(sheetId);

            var problems = new List<string>();
            var row = 2;   // row 1 holds the headers

            foreach (var record in df.Rows)
            {
                foreach (var required in new[] { "Region", "Revenue" })
                {
                    if (!record.TryGetValue(required, out var value) || string.IsNullOrWhiteSpace(value?.ToString()))
                        problems.Add($"Row {row}: '{required}' is empty.");
                }

                if (record.TryGetValue("Revenue", out var revenue)
                    && revenue is not null
                    && !double.TryParse(revenue.ToString(), out _))
                {
                    problems.Add($"Row {row}: Revenue '{revenue}' is not a number.");
                }

                row++;
            }

            foreach (var problem in problems.Take(50)) Log(problem);

            if (problems.Count > 0)
                Api.Notify($"{problems.Count} validation problems", problems[0]);
            else
                Log("No problems found.");

            return new { checkedRows = df.Count, problems = problems.Count };
            """, null, "validation", "quality"),

        T("sheets-dedupe", "Remove duplicate rows", "Keeps the first row for each value of a key column.",
            "Sheets", ScriptLanguage.JavaScript,
            ScriptScope.SheetsRead | ScriptScope.SheetsWrite,
            """
            var sheetId = input.sheetId;
            var keyColumn = input.keyColumn || 'Email';

            var df = api.sheets.read(sheetId);
            var deduped = df.distinct(keyColumn);

            console.log('Before: ' + df.count + ' rows, after: ' + deduped.count + ' rows.');

            if (deduped.count < df.count) {
                api.sheets.writeFrame(sheetId, deduped);
            }

            ({ removed: df.count - deduped.count });
            """, null, "dedupe", "cleanup"),

        T("sheets-monthly-pivot", "Monthly pivot", "Cross-tabulates a sheet by month and category.",
            "Sheets", ScriptLanguage.Python,
            ScriptScope.SheetsRead | ScriptScope.SheetsWrite,
            """
            sheet_id = input["sheetId"]
            df = api.Sheets.Read(sheet_id)

            pivot = df.Pivot("Month", "Category", "Amount", "sum")
            api.Log(pivot)

            api.Sheets.WriteFrame(sheet_id, pivot, "Pivot")
            result = {"rows": pivot.Count, "columns": len(pivot.Columns)}
            """, null, "pivot", "report"),

        // ──────────────────────────────── Docs ────────────────────────────────

        T("docs-mail-merge", "Mail merge", "Creates one document per spreadsheet row from a template document.",
            "Docs", ScriptLanguage.JavaScript,
            ScriptScope.SheetsRead | ScriptScope.DocsWrite | ScriptScope.DriveWrite,
            """
            // The template document should contain placeholders like {{Name}} and {{Amount}}.
            var templateId = input.templateId;
            var sheetId = input.sheetId;
            var folderId = input.folderId || null;

            var df = api.sheets.read(sheetId);
            var made = [];

            var rows = df.rows;
            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];
                var name = String(row['Name'] || ('Row ' + (i + 1)));

                var doc = api.docs.createFromTemplate(templateId, name, row, folderId);
                made.push(doc.name);
                console.log('Created ' + doc.name);
            }

            ({ created: made.length, names: made });
            """, null, "merge", "template", "letters"),

        T("docs-invoice", "Generate an invoice", "Builds an invoice document from one spreadsheet row.",
            "Docs", ScriptLanguage.CSharp,
            ScriptScope.SheetsRead | ScriptScope.DocsWrite | ScriptScope.DriveWrite,
            """
            var sheetId = Input["sheetId"];
            var df = Api.Sheets.Read(sheetId);
            if (df.Count == 0) throw new Exception("That sheet has no rows.");

            var row = df.Rows[0];
            var client = row.GetValueOrDefault("Client")?.ToString() ?? "Client";
            var amount = row.GetValueOrDefault("Amount");

            var doc = Api.Drive.CreateDocument("document", $"Invoice — {client}");

            var html =
                $"<h1>Invoice</h1>" +
                $"<p><strong>To:</strong> {client}</p>" +
                $"<p><strong>Date:</strong> {DateTime.UtcNow:yyyy-MM-dd}</p>" +
                $"<p><strong>Amount due:</strong> {amount}</p>" +
                "<p>Payment is due within 30 days.</p>";

            Api.Docs.Write(doc.Id, html);
            Log($"Invoice created: {doc.Name}");

            return new { id = doc.Id, client };
            """, null, "invoice", "finance", "generate"),

        T("docs-meeting-notes", "Meeting notes from the calendar", "Creates a notes document for each of today's meetings.",
            "Docs", ScriptLanguage.Python,
            ScriptScope.CalendarRead | ScriptScope.DocsWrite | ScriptScope.DriveWrite,
            """
            from datetime import datetime, timedelta

            start = api.Now()
            end = api.Now()[:10] + "T23:59:59Z"

            events = api.Calendar.Events(start, end)
            created = []

            for e in events:
                doc = api.Drive.CreateDocument("document", "Notes — " + e.Title)
                html = ("<h1>" + e.Title + "</h1>"
                        + "<p>" + str(e.Start) + " · " + (e.Location or "no location") + "</p>"
                        + "<h2>Attendees</h2><p>" + ", ".join(e.Attendees) + "</p>"
                        + "<h2>Notes</h2><p></p>"
                        + "<h2>Actions</h2><ul><li></li></ul>")
                api.Docs.Write(doc.Id, html)
                created.append(doc.Name)
                api.Log("Prepared " + doc.Name)

            result = {"created": len(created)}
            """, null, "meeting", "notes", "calendar"),

        // ──────────────────────────────── Slides ────────────────────────────────

        T("slides-from-dataset", "Deck from a dataset", "Turns each row of a spreadsheet into a slide.",
            "Slides", ScriptLanguage.Python,
            ScriptScope.SheetsRead | ScriptScope.SlidesWrite | ScriptScope.DriveWrite,
            """
            sheet_id = input["sheetId"]
            df = api.Sheets.Read(sheet_id)

            deck = api.Slides.Create("Generated deck — " + api.Now()[:10])
            api.Slides.AddSlide(deck.Id, "Generated deck", "From " + str(df.Count) + " rows", "title")

            count = api.Slides.AddSlidesFromFrame(deck.Id, df.Head(20), df.Columns[0])

            api.Log("Built %d slides in %s" % (count + 1, deck.Name))
            result = {"deck": deck.Id, "slides": count + 1}
            """, null, "deck", "generate", "dataset"),

        T("slides-weekly-report", "Weekly report deck", "Builds a short deck from this week's calendar and Drive activity.",
            "Slides", ScriptLanguage.JavaScript,
            ScriptScope.CalendarRead | ScriptScope.DriveRead | ScriptScope.SlidesWrite | ScriptScope.DriveWrite,
            """
            var now = new Date();
            var weekAgo = new Date(now.getTime() - 7 * 24 * 3600 * 1000);

            var events = api.calendar.events(weekAgo.toISOString(), now.toISOString());
            var files = api.drive.search('', null, 200);

            var deck = api.slides.create('Weekly report — ' + now.toISOString().slice(0, 10));

            api.slides.addSlide(deck.id, 'Weekly report', now.toISOString().slice(0, 10), 'title');
            api.slides.addSlide(deck.id, 'Meetings', 'Held: ' + events.length);

            var recent = [];
            for (var i = 0; i < Math.min(8, files.length); i++) recent.push(files[i].name);
            api.slides.addSlide(deck.id, 'Recent files', recent.join('\n'));

            console.log('Deck ready: ' + deck.name);
            ({ deck: deck.id, meetings: events.length });
            """, null, "report", "weekly", "deck"),

        // ──────────────────────────────── Calendar ────────────────────────────────

        T("calendar-from-project-sheet", "Events from a project plan", "Creates a calendar event for each task row.",
            "Calendar", ScriptLanguage.CSharp,
            ScriptScope.SheetsRead | ScriptScope.CalendarWrite,
            """
            // Expects columns: Task, Start, End (ISO dates).
            var df = Api.Sheets.Read(Input["sheetId"]);
            var created = 0;

            foreach (var row in df.Rows)
            {
                var title = row.GetValueOrDefault("Task")?.ToString();
                var start = row.GetValueOrDefault("Start")?.ToString();
                var end = row.GetValueOrDefault("End")?.ToString();

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(start)) continue;

                Api.Calendar.Create(new EventDraft
                {
                    Title = title,
                    Start = start,
                    End = string.IsNullOrWhiteSpace(end) ? start : end,
                    Description = "Created from the project plan.",
                });

                created++;
                Log($"Scheduled '{title}'.");
            }

            return new { created };
            """, null, "project", "schedule", "import"),

        T("calendar-weekly-digest", "Weekly digest", "Summarises next week's meetings and sends a notification.",
            "Calendar", ScriptLanguage.JavaScript,
            ScriptScope.CalendarRead | ScriptScope.Notify,
            """
            var now = new Date();
            var next = new Date(now.getTime() + 7 * 24 * 3600 * 1000);

            var events = api.calendar.events(now.toISOString(), next.toISOString());
            var lines = [];
            var hours = 0;

            for (var i = 0; i < events.length; i++) {
                var e = events[i];
                var minutes = (new Date(e.end) - new Date(e.start)) / 60000;
                hours += minutes / 60;
                lines.push(String(e.start).slice(0, 16).replace('T', ' ') + '  ' + e.title);
            }

            console.log(lines.join('\n'));

            api.notify(
                events.length + ' meetings next week',
                Math.round(hours * 10) / 10 + ' hours booked.');

            ({ meetings: events.length, hours: Math.round(hours * 10) / 10 });
            """, null, "digest", "weekly", "notify"),

        T("calendar-block-focus-time", "Block focus time", "Finds gaps in the week and books focus blocks in them.",
            "Calendar", ScriptLanguage.Python,
            ScriptScope.CalendarRead | ScriptScope.CalendarWrite,
            """
            from datetime import datetime, timedelta

            now = datetime.utcnow().replace(hour=0, minute=0, second=0, microsecond=0)
            week = now + timedelta(days=7)

            busy = api.Calendar.Events(now.isoformat() + "Z", week.isoformat() + "Z")
            taken = [(b.Start, b.End) for b in busy]

            created = 0
            for day in range(1, 6):                     # Monday to Friday of the coming week
                slot = now + timedelta(days=day, hours=9)
                end = slot + timedelta(hours=2)

                clash = any(s < end and slot < e for (s, e) in taken)
                if clash:
                    api.Log("Day %d already booked at 09:00" % day)
                    continue

                api.Calendar.Create(EventDraft(
                    Title="Focus time",
                    Start=slot.isoformat() + "Z",
                    End=end.isoformat() + "Z",
                    Description="Blocked automatically."))
                created += 1

            result = {"blocks": created}
            """, null, "focus", "scheduling"),

        // ─────────────────────── External APIs ───────────────────────

        T("fx-rates-to-sheet", "Exchange rates to a sheet", "Appends today's rates against a base currency. No API key needed.",
            "External APIs", ScriptLanguage.JavaScript,
            ScriptScope.SheetsWrite | ScriptScope.Network,
            """
            // api.frankfurter.app is free and needs no key.
            var base = input.base || 'USD';
            var symbols = input.symbols || 'IDR,EUR,JPY,SGD';
            var sheetId = input.sheetId;

            var data = api.http.getJson(
                'https://api.frankfurter.app/latest?from=' + base + '&to=' + symbols);

            var date = data.date;
            var rates = data.rates;

            for (var code in rates) {
                api.sheets.appendRow(sheetId, [date, base, code, rates[code]]);
                console.log(date + '  1 ' + base + ' = ' + rates[code] + ' ' + code);
            }

            rates;
            """, "api.frankfurter.app", "forex", "kurs", "currency", "finance"),

        T("gold-price-tracker", "Gold and metals tracker", "Appends the current gold price. Needs a metalpriceapi.com key in inputs.",
            "External APIs", ScriptLanguage.Python,
            ScriptScope.SheetsWrite | ScriptScope.Network,
            """
            # Put your key in the script's inputs as apiKey — never in the source, which is versioned.
            key = input["apiKey"]
            sheet_id = input["sheetId"]

            url = ("https://api.metalpriceapi.com/v1/latest?api_key=" + key
                   + "&base=USD&currencies=XAU,XAG")

            data = api.Http.GetJson(url)
            rates = data["rates"]

            # The API quotes metal-per-USD, so invert for the price of one ounce.
            gold = 1.0 / rates["XAU"]
            silver = 1.0 / rates["XAG"]

            api.Sheets.AppendRow(sheet_id, [api.Now()[:10], round(gold, 2), round(silver, 2)])
            api.Log("Gold $%.2f/oz, silver $%.2f/oz" % (gold, silver))

            result = {"gold": gold, "silver": silver}
            """, "api.metalpriceapi.com", "gold", "emas", "commodities", "finance"),

        T("stock-watchlist", "Stock watchlist", "Refreshes quotes for a list of tickers. Needs a finnhub.io key.",
            "External APIs", ScriptLanguage.CSharp,
            ScriptScope.SheetsRead | ScriptScope.SheetsWrite | ScriptScope.Network,
            """
            // The sheet's first column holds the tickers; this fills in price and change.
            var key = Input["apiKey"];
            var sheetId = Input["sheetId"];

            var df = Api.Sheets.Read(sheetId);
            var row = 2;

            foreach (var record in df.Rows)
            {
                var ticker = record.GetValueOrDefault("Ticker")?.ToString();
                if (string.IsNullOrWhiteSpace(ticker)) { row++; continue; }

                var quote = Api.Http.GetJson(
                    $"https://finnhub.io/api/v1/quote?symbol={ticker}&token={key}")
                    as Dictionary<string, object?>;

                if (quote is null) { row++; continue; }

                Api.Sheets.SetCell(sheetId, $"B{row}", quote.GetValueOrDefault("c"));
                Api.Sheets.SetCell(sheetId, $"C{row}", quote.GetValueOrDefault("dp"));

                Log($"{ticker}: {quote.GetValueOrDefault("c")} ({quote.GetValueOrDefault("dp")}%)");
                row++;
            }

            return new { updated = row - 2 };
            """, "finnhub.io", "stocks", "saham", "quotes", "finance"),

        T("weather-daily", "Daily weather", "Writes tomorrow's forecast into a document. No API key needed.",
            "External APIs", ScriptLanguage.JavaScript,
            ScriptScope.DocsWrite | ScriptScope.DriveWrite | ScriptScope.Network,
            """
            // open-meteo.com is free and needs no key. Defaults to Jakarta.
            var lat = input.lat || '-6.2';
            var lon = input.lon || '106.8';
            var place = input.place || 'Jakarta';

            var data = api.http.getJson(
                'https://api.open-meteo.com/v1/forecast?latitude=' + lat + '&longitude=' + lon +
                '&daily=temperature_2m_max,temperature_2m_min,precipitation_sum&timezone=UTC');

            var days = data.daily.time;
            var rows = '';

            for (var i = 0; i < Math.min(5, days.length); i++) {
                rows += '<li>' + days[i] + ': ' +
                    data.daily.temperature_2m_min[i] + '–' + data.daily.temperature_2m_max[i] + '°C, ' +
                    data.daily.precipitation_sum[i] + ' mm</li>';
                console.log(days[i] + '  ' + data.daily.temperature_2m_max[i] + '°C');
            }

            var doc = api.drive.createDocument('document', 'Weather — ' + place);
            api.docs.write(doc.id, '<h1>Forecast for ' + place + '</h1><ul>' + rows + '</ul>');

            ({ doc: doc.id });
            """, "api.open-meteo.com", "weather", "cuaca", "forecast"),

        T("translate-column", "Translate a column", "Translates one spreadsheet column into another language. No API key needed.",
            "External APIs", ScriptLanguage.Python,
            ScriptScope.SheetsRead | ScriptScope.SheetsWrite | ScriptScope.Network,
            """
            # MyMemory is free for modest volumes and needs no key.
            sheet_id = input["sheetId"]
            source_col = input.get("column", "Text") if hasattr(input, "get") else "Text"
            pair = input["langpair"] if "langpair" in input else "en|id"

            df = api.Sheets.Read(sheet_id)
            row = 2

            for record in df.Rows:
                text = record[source_col] if source_col in record else None
                if not text:
                    row += 1
                    continue

                url = "https://api.mymemory.translated.net/get?q=" + str(text) + "&langpair=" + pair
                data = api.Http.GetJson(url)
                translated = data["responseData"]["translatedText"]

                api.Sheets.SetCell(sheet_id, "Z" + str(row), translated)
                api.Log(str(text)[:40] + "  ->  " + translated[:40])
                row += 1

            result = {"rows": row - 2}
            """, "api.mymemory.translated.net", "translate", "terjemah", "language"),

        T("web-search-digest", "Web search digest", "Searches the web and writes a digest document. Needs a Tavily key.",
            "External APIs", ScriptLanguage.JavaScript,
            ScriptScope.DocsWrite | ScriptScope.DriveWrite | ScriptScope.Network,
            """
            var key = input.apiKey;
            var query = input.query || 'workplace automation';

            var data = api.http.postJson('https://api.tavily.com/search', {
                api_key: key,
                query: query,
                max_results: 8,
                include_answer: true
            });

            var html = '<h1>' + query + '</h1>';
            if (data.answer) html += '<p><em>' + data.answer + '</em></p>';

            for (var i = 0; i < data.results.length; i++) {
                var r = data.results[i];
                html += '<h2>' + r.title + '</h2><p>' + r.content + '</p><p>' + r.url + '</p>';
                console.log(r.title);
            }

            var doc = api.drive.createDocument('document', 'Digest — ' + query);
            api.docs.write(doc.id, html);

            ({ doc: doc.id, results: data.results.length });
            """, "api.tavily.com", "search", "research", "digest"),

        T("crypto-portfolio", "Crypto portfolio value", "Prices a holdings sheet against CoinGecko. No API key needed.",
            "External APIs", ScriptLanguage.CSharp,
            ScriptScope.SheetsRead | ScriptScope.SheetsWrite | ScriptScope.Network,
            """
            // Sheet columns: Coin (coingecko id, e.g. bitcoin), Amount.
            var sheetId = Input["sheetId"];
            var df = Api.Sheets.Read(sheetId);

            var ids = df.Rows
                .Select(r => r.GetValueOrDefault("Coin")?.ToString())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()
                .ToList();

            if (ids.Count == 0) throw new Exception("No coins found in the Coin column.");

            var prices = Api.Http.GetJson(
                "https://api.coingecko.com/api/v3/simple/price?ids=" + string.Join(",", ids) + "&vs_currencies=usd")
                as Dictionary<string, object?>;

            double total = 0;
            var row = 2;

            foreach (var record in df.Rows)
            {
                var coin = record.GetValueOrDefault("Coin")?.ToString();
                var amount = Convert.ToDouble(record.GetValueOrDefault("Amount") ?? 0);

                if (coin is not null
                    && prices?.GetValueOrDefault(coin) is Dictionary<string, object?> quote
                    && quote.GetValueOrDefault("usd") is { } usd)
                {
                    var price = Convert.ToDouble(usd);
                    var value = price * amount;
                    total += value;

                    Api.Sheets.SetCell(sheetId, $"C{row}", price);
                    Api.Sheets.SetCell(sheetId, $"D{row}", value);
                    Log($"{coin}: {amount} x ${price} = ${value:N2}");
                }

                row++;
            }

            Log($"Portfolio total: ${total:N2}");
            return new { total, coins = ids.Count };
            """, "api.coingecko.com", "crypto", "portfolio", "finance"),
    ];
}
