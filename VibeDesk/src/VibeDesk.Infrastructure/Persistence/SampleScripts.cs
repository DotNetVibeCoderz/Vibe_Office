using VibeDesk.Domain;
using VibeDesk.Domain.Entities;

namespace VibeDesk.Infrastructure.Persistence;

/// <summary>
/// Sample scripts for a fresh install: one per language, so the Scripts tab opens on something
/// readable rather than an empty state.
/// </summary>
/// <remarks>
/// All three are seeded **disabled**, exactly as a script created from a template or installed from
/// the shared list would be. Seed data that armed a schedule on first boot would run against a
/// workspace nobody had looked at yet.
/// </remarks>
internal static class SampleScripts
{
    internal static IEnumerable<(Script Script, ScriptTrigger? Trigger)> For(Guid ownerId) =>
    [
        (
            new Script
            {
                OwnerId = ownerId,
                Name = "Weekly storage report",
                Description = "Totals Drive usage by file type and writes the breakdown into a new sheet.",
                Language = ScriptLanguage.Python,
                Scopes = ScriptScope.DriveRead | ScriptScope.SheetsWrite | ScriptScope.Notify,
                IsEnabled = false,
                Code = StorageReport,
            },
            new ScriptTrigger
            {
                Kind = TriggerKind.Schedule,
                CronExpression = "0 8 * * 1",
                TimeZoneId = "Asia/Jakarta",
                IsEnabled = false,
            }
        ),
        (
            new Script
            {
                OwnerId = ownerId,
                Name = "Tag new uploads",
                Description = "Prefixes every newly created file with the month it arrived.",
                Language = ScriptLanguage.JavaScript,
                Scopes = ScriptScope.DriveRead | ScriptScope.DriveWrite,
                IsEnabled = false,
                Code = TagUploads,
            },
            new ScriptTrigger
            {
                Kind = TriggerKind.Event,
                EventName = "item.created",
                TimeZoneId = "Asia/Jakarta",
                IsEnabled = false,
            }
        ),
        (
            new Script
            {
                OwnerId = ownerId,
                Name = "Check the revenue model",
                Description = "Flags blank cells and non-numeric amounts before the sheet is shared.",
                Language = ScriptLanguage.CSharp,
                Scopes = ScriptScope.SheetsRead | ScriptScope.Notify,
                IsEnabled = false,
                Code = ValidateSheet,
            },
            null
        ),
    ];

    private const string StorageReport = """
        # Groups everything in Drive by kind and writes the breakdown into a new spreadsheet.
        # Search recurses; List would only see the root folder's own children.
        files = api.Drive.Search('', None, 2000)
        totals = {}

        for f in files:
            if f.Type == 'Folder':
                continue

            dot = f.Name.rfind('.')
            kind = f.Name[dot + 1:].lower() if dot > 0 else f.Type.lower()
            bucket = totals.get(kind, [0, 0])

            totals[kind] = [bucket[0] + 1, bucket[1] + f.Size]

        rows = sorted(([k, v[0], v[1]] for k, v in totals.items()), key=lambda r: -r[2])

        for row in rows:
            api.Log('%-14s %4d files %12d bytes' % (row[0], row[1], row[2]))

        sheet = api.Sheets.Create('Storage report', api.Frame(['kind', 'files', 'bytes'], rows))

        api.Notify('Storage report ready', sheet.Name)

        result = len(rows)
        """;

    private const string TagUploads = """
        // Runs on item.created; the new file's id arrives as input.itemId.
        var itemId = input.itemId;

        if (!itemId) {
            console.log('No item in this event; nothing to do.');
        } else {
            var file = api.drive.get(itemId);
            var stamp = new Date().toISOString().slice(0, 7);   // 2026-08

            // Idempotent: a redelivered event must not stack a second prefix.
            if (file && file.type !== 'Folder' && file.name.indexOf(stamp) !== 0) {
                api.drive.rename(file.id, stamp + ' ' + file.name);
                console.log('Tagged ' + file.name);
            }
        }

        true;
        """;

    private const string ValidateSheet = """
        // Set fileId to the spreadsheet to check. The column defaults to the one this script is
        // named after; pass `column` to point it somewhere else.
        var fileId = Input.TryGetValue("fileId", out var id) ? id : "";
        var column = Input.TryGetValue("column", out var c) ? c : "Revenue";

        if (string.IsNullOrEmpty(fileId))
        {
            Log("Pass a fileId input: vibedesk run <id> --input fileId=<spreadsheet id>");
            return -1;
        }

        var frame = Api.Sheets.Read(fileId, null);

        // A missing column would otherwise report every row as blank, which reads like real findings.
        if (!frame.Columns.Contains(column))
        {
            Log($"No '{column}' column here. Found: {string.Join(", ", frame.Columns)}");
            return -1;
        }

        var problems = new List<string>();
        var line = 1;

        foreach (var row in frame.Rows)
        {
            line++;   // row 1 is the header

            var value = row.TryGetValue(column, out var raw) ? raw?.ToString() : null;

            if (string.IsNullOrWhiteSpace(value))
            {
                problems.Add($"Row {line}: {column} is blank");
            }
            else if (!decimal.TryParse(value.Replace(",", "").TrimStart('$'), out _))
            {
                problems.Add($"Row {line}: '{value}' is not a number");
            }
        }

        foreach (var problem in problems) Log(problem);

        Log(problems.Count == 0
            ? $"All {frame.Count} rows look fine."
            : $"{problems.Count} of {frame.Count} rows need attention.");

        if (problems.Count > 0)
        {
            Api.Notify("Revenue model needs attention", $"{problems.Count} problem(s) found.");
        }

        return problems.Count;
        """;
}
