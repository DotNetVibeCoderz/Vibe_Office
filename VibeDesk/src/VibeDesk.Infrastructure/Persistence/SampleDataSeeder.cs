using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VibeDesk.Application.Documents;
using VibeDesk.Domain;
using VibeDesk.Domain.Entities;
using VibeDesk.Infrastructure.Identity;

namespace VibeDesk.Infrastructure.Persistence;

/// <summary>
/// Seeds roles, demo users and a realistic set of documents so a fresh install is immediately
/// explorable rather than an empty shell.
/// </summary>
/// <remarks>
/// Idempotent: every step checks before inserting, so running it against a populated database is a
/// no-op and it is safe to call on every startup.
/// </remarks>
public sealed class SampleDataSeeder(
    AppDbContext db,
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    ILogger<SampleDataSeeder> logger)
{
    /// <summary>
    /// Shared password for every demo account. Development convenience only — the seeder refuses to
    /// run when <paramref name="isDevelopment"/> is false unless a password is supplied explicitly.
    /// </summary>
    public const string DemoPassword = "VibeDesk#2026";

    private static readonly (string Email, string Name, string Title, string Color, string Role)[] DemoUsers =
    [
        ("fadhil@gravicode.com", "Kang Fadhil", "Founder & Lead Engineer", "#6366f1", AppRoles.Admin),
        ("sari@gravicode.com", "Sari Wulandari", "Product Manager", "#ec4899", AppRoles.Member),
        ("budi@gravicode.com", "Budi Santoso", "Backend Engineer", "#14b8a6", AppRoles.Member),
        ("dewi@gravicode.com", "Dewi Lestari", "UI/UX Designer", "#f59e0b", AppRoles.Member),
        ("rizki@gravicode.com", "Rizki Pratama", "Data Analyst", "#8b5cf6", AppRoles.Member),
        ("maya@gravicode.com", "Maya Anggraini", "QA Engineer", "#ef4444", AppRoles.Member),
        ("agus@gravicode.com", "Agus Setiawan", "DevOps Engineer", "#0ea5e9", AppRoles.Member),
        ("nina@gravicode.com", "Nina Kartika", "Finance Lead", "#22c55e", AppRoles.Member),
    ];

    public async Task SeedAsync(bool isDevelopment = true, CancellationToken ct = default)
    {
        if (!isDevelopment)
        {
            logger.LogWarning(
                "Sample data seeding was requested outside Development and will be skipped. " +
                "Demo accounts share a well-known password and must never exist in production.");
            return;
        }

        await SeedRolesAsync();
        var users = await SeedUsersAsync();

        if (users.Count == 0)
        {
            logger.LogWarning("No demo users available; skipping content seeding.");
            return;
        }

        // If any drive content exists, assume the database has already been seeded.
        if (await db.DriveItems.AnyAsync(ct))
        {
            logger.LogInformation("Drive already contains items; skipping content seeding.");
            return;
        }

        await SeedContentAsync(users, ct);
        logger.LogInformation("Sample data seeded: {UserCount} users.", users.Count);
    }

    private async Task SeedRolesAsync()
    {
        foreach (var role in AppRoles.All)
        {
            if (await roleManager.RoleExistsAsync(role)) continue;

            await roleManager.CreateAsync(new AppRole
            {
                Name = role,
                Description = role == AppRoles.Admin
                    ? "Full administrative access"
                    : "Standard workspace member",
            });
        }
    }

    private async Task<List<AppUser>> SeedUsersAsync()
    {
        var result = new List<AppUser>();

        foreach (var (email, name, title, color, role) in DemoUsers)
        {
            var existing = await userManager.FindByEmailAsync(email);
            if (existing is not null)
            {
                result.Add(existing);
                continue;
            }

            var user = new AppUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = name,
                JobTitle = title,
                AvatarColor = color,
                TimeZoneId = "Asia/Jakarta",
                Locale = "id",
            };

            var created = await userManager.CreateAsync(user, DemoPassword);
            if (!created.Succeeded)
            {
                logger.LogError(
                    "Failed to create demo user {Email}: {Errors}",
                    email,
                    string.Join("; ", created.Errors.Select(e => e.Description)));
                continue;
            }

            await userManager.AddToRoleAsync(user, role);
            result.Add(user);
        }

        return result;
    }

    private async Task SeedContentAsync(List<AppUser> users, CancellationToken ct)
    {
        var owner = users[0];
        var now = DateTimeOffset.UtcNow;

        // ── folder tree ──────────────────────────────────────────────────────────────────────
        var projects = NewFolder("Projects", null, owner.Id, "#6366f1");
        var q3 = NewFolder("Q3 Launch", projects, owner.Id, "#ec4899");
        var finance = NewFolder("Finance", null, owner.Id, "#22c55e");
        var design = NewFolder("Design", projects, owner.Id, "#f59e0b");
        var archive = NewFolder("Archive", null, owner.Id, "#64748b");

        db.DriveItems.AddRange(projects, q3, finance, design, archive);

        // ── documents ────────────────────────────────────────────────────────────────────────
        AddDocument(
            "Product Requirements — VibeDesk",
            q3.Id,
            owner.Id,
            """
            <h1>VibeDesk — Product Requirements</h1>
            <p>VibeDesk is a web-first office suite: documents, spreadsheets, presentations,
            storage and calendaring in one workspace, with an assistant available in every app.</p>
            <h2>Goals</h2>
            <ul>
              <li>Real-time collaboration across every editor, not only documents.</li>
              <li>Drive as the single storage hub — one sharing model for all content types.</li>
              <li><strong>Mr Clippy</strong> grounded in the user's own files.</li>
            </ul>
            <h2>Non-goals</h2>
            <p>We are not building an email client. Calendar syncs with Gmail and Outlook instead.</p>
            <blockquote>Built by Gravicode Studios, led by Kang Fadhil.</blockquote>
            """);

        AddDocument(
            "Engineering Onboarding",
            projects.Id,
            users.Count > 2 ? users[2].Id : owner.Id,
            """
            <h1>Engineering Onboarding</h1>
            <p>Welcome to the team. This page gets you from a clean machine to a running stack.</p>
            <h2>Prerequisites</h2>
            <ul><li>.NET 10 SDK</li><li>Node 22 (tooling only)</li><li>Docker for Redis and MinIO</li></ul>
            <h2>First run</h2>
            <p>SQLite is the default, so <code>dotnet run</code> works with no external services.
            Switch providers through <code>Database:Provider</code> when you need to test one.</p>
            """);

        AddDocument(
            "Meeting Notes — Sprint 14 Retro",
            q3.Id,
            users.Count > 1 ? users[1].Id : owner.Id,
            """
            <h1>Sprint 14 Retrospective</h1>
            <h2>What went well</h2>
            <ul><li>Formula engine landed ahead of schedule.</li><li>Zero production incidents.</li></ul>
            <h2>What to improve</h2>
            <ul><li>Permission caching needs load testing before launch.</li></ul>
            <h2>Actions</h2>
            <ol><li>Budi: benchmark inherited-permission resolution.</li>
            <li>Dewi: dark-theme audit of the Slides editor.</li></ol>
            """);

        // ── spreadsheets ─────────────────────────────────────────────────────────────────────
        AddSpreadsheet("Q3 Revenue Model", finance.Id, owner.Id, BuildRevenueModel());
        AddSpreadsheet("Project Timeline", q3.Id, owner.Id, BuildTimeline(now));
        AddSpreadsheet(
            "Regional Sales Analysis",
            finance.Id,
            users.Count > 4 ? users[4].Id : owner.Id,
            BuildSalesAnalysis());

        // ── presentations ────────────────────────────────────────────────────────────────────
        AddPresentation("VibeDesk Launch Deck", q3.Id, owner.Id, BuildLaunchDeck());
        AddPresentation(
            "Design System Review",
            design.Id,
            users.Count > 3 ? users[3].Id : owner.Id,
            BuildDesignDeck());

        await db.SaveChangesAsync(ct);

        await SeedSharingAsync(users, ct);
        await SeedCommentsAsync(users, ct);
        await SeedCalendarsAsync(users, now, ct);
        await SeedScriptsAsync(users, ct);
    }

    /// <summary>
    /// One sample script per language, so the Scripts tab opens on something readable. All three are
    /// disabled and their triggers with them: seed data that armed a schedule would run against a
    /// workspace nobody had looked at yet.
    /// </summary>
    private async Task SeedScriptsAsync(List<AppUser> users, CancellationToken ct)
    {
        if (await db.Scripts.AnyAsync(ct)) return;

        foreach (var (script, trigger) in SampleScripts.For(users[0].Id))
        {
            db.Scripts.Add(script);

            if (trigger is not null)
            {
                trigger.ScriptId = script.Id;
                db.ScriptTriggers.Add(trigger);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // ─────────────────────────────────── entity builders ───────────────────────────────────

    private static DriveItem NewFolder(string name, DriveItem? parent, Guid ownerId, string color)
    {
        var item = new DriveItem
        {
            Type = DriveItemType.Folder,
            Name = name,
            OwnerId = ownerId,
            ParentId = parent?.Id,
            Color = color,
            SearchText = name,
            UpdatedById = ownerId,
        };

        item.Path = parent is null ? $"/{item.Id}/" : $"{parent.Path}{item.Id}/";
        return item;
    }

    private void AddDocument(string name, Guid parentId, Guid ownerId, string html)
    {
        var model = new DocumentModel
        {
            Html = html,
            WordCount = html.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
        };

        AddEditable(name, parentId, ownerId, DriveItemType.Document, ContentJson.Serialize(model));
    }

    private void AddSpreadsheet(string name, Guid parentId, Guid ownerId, SpreadsheetModel model) =>
        AddEditable(name, parentId, ownerId, DriveItemType.Spreadsheet, ContentJson.Serialize(model));

    private void AddPresentation(string name, Guid parentId, Guid ownerId, PresentationModel model) =>
        AddEditable(name, parentId, ownerId, DriveItemType.Presentation, ContentJson.Serialize(model));

    private void AddEditable(
        string name, Guid parentId, Guid ownerId, DriveItemType type, string data)
    {
        var parent = db.DriveItems.Local.First(x => x.Id == parentId);

        var item = new DriveItem
        {
            Type = type,
            Name = name,
            OwnerId = ownerId,
            ParentId = parentId,
            Path = $"{parent.Path}",
            SearchText = name,
            UpdatedById = ownerId,
            VersionNumber = 1,
        };

        item.Path = $"{parent.Path}{item.Id}/";

        db.DriveItems.Add(item);
        db.DriveItemContents.Add(new DriveItemContent
        {
            DriveItemId = item.Id,
            Data = data,
            Revision = 1,
            UpdatedById = ownerId,
        });
    }

    // ─────────────────────────────────── spreadsheet data ───────────────────────────────────

    /// <summary>
    /// A revenue model that exercises the formula engine end to end: cross-row arithmetic, SUM over a
    /// range, AVERAGE, growth ratios and conditional formatting.
    /// </summary>
    private static SpreadsheetModel BuildRevenueModel()
    {
        var sheet = new SheetTab { Id = "s1", Name = "Model", FrozenRows = 1, ColCount = 8, RowCount = 30 };

        var headers = new[] { "Month", "New Users", "ARPU (USD)", "Revenue", "Costs", "Margin", "Growth" };
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cells[new CellRef(0, i).Key] = new Cell { V = headers[i], S = "header" };
        }

        var months = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep" };
        var newUsers = new[] { 320, 410, 505, 640, 720, 880, 1010, 1180, 1340 };
        var arpu = new[] { 12.5, 12.8, 13.0, 13.2, 13.5, 13.9, 14.2, 14.5, 14.9 };

        for (var r = 0; r < months.Length; r++)
        {
            var row = r + 1;
            var excelRow = row + 1;

            sheet.Cells[new CellRef(row, 0).Key] = new Cell { V = months[r] };
            sheet.Cells[new CellRef(row, 1).Key] = new Cell { V = newUsers[r].ToString() };
            sheet.Cells[new CellRef(row, 2).Key] = new Cell
            {
                V = arpu[r].ToString(System.Globalization.CultureInfo.InvariantCulture),
                Fmt = "0.00",
            };

            sheet.Cells[new CellRef(row, 3).Key] = new Cell { F = $"=B{excelRow}*C{excelRow}", Fmt = "#,##0.00" };
            sheet.Cells[new CellRef(row, 4).Key] = new Cell { F = $"=D{excelRow}*0.42", Fmt = "#,##0.00" };
            sheet.Cells[new CellRef(row, 5).Key] = new Cell { F = $"=D{excelRow}-E{excelRow}", Fmt = "#,##0.00" };

            // The first month has no prior month to compare against.
            sheet.Cells[new CellRef(row, 6).Key] = row == 1
                ? new Cell { V = "—" }
                : new Cell { F = $"=IFERROR(D{excelRow}/D{excelRow - 1}-1,0)", Fmt = "0.0%" };
        }

        var totalRow = months.Length + 2;
        var excelTotal = totalRow + 1;
        sheet.Cells[new CellRef(totalRow, 0).Key] = new Cell { V = "Total", S = "header" };
        sheet.Cells[new CellRef(totalRow, 1).Key] = new Cell { F = "=SUM(B2:B10)", S = "total" };
        sheet.Cells[new CellRef(totalRow, 2).Key] = new Cell { F = "=AVERAGE(C2:C10)", S = "total", Fmt = "0.00" };
        sheet.Cells[new CellRef(totalRow, 3).Key] = new Cell { F = "=SUM(D2:D10)", S = "total", Fmt = "#,##0.00" };
        sheet.Cells[new CellRef(totalRow, 4).Key] = new Cell { F = "=SUM(E2:E10)", S = "total", Fmt = "#,##0.00" };
        sheet.Cells[new CellRef(totalRow, 5).Key] = new Cell { F = "=SUM(F2:F10)", S = "total", Fmt = "#,##0.00" };
        sheet.Cells[new CellRef(totalRow, 6).Key] = new Cell
        {
            F = $"=IFERROR(F{excelTotal}/D{excelTotal},0)",
            S = "total",
            Fmt = "0.0%",
        };

        sheet.ColWidths["A"] = 90;
        sheet.ColWidths["D"] = 130;
        sheet.ColWidths["E"] = 130;
        sheet.ColWidths["F"] = 130;

        sheet.ConditionalFormats.Add(new ConditionalFormatRule
        {
            Range = "G2:G10",
            Op = "greaterThan",
            Value1 = "0.15",
            Style = new CellStyle { Background = "#dcfce7", Color = "#166534", Bold = true },
        });

        sheet.Charts.Add(new ChartSpec
        {
            Title = "Revenue vs Costs",
            Kind = "column",
            CategoryRange = "A2:A10",
            SeriesRanges = ["D2:D10", "E2:E10"],
            SeriesNames = ["Revenue", "Costs"],
            X = 640,
            Y = 40,
        });

        return new SpreadsheetModel
        {
            Sheets = [sheet],
            Styles =
            {
                ["header"] = new CellStyle { Bold = true, Background = "#eef2ff", Color = "#3730a3" },
                ["total"] = new CellStyle { Bold = true, Background = "#f1f5f9", Border = "top" },
            },
            NamedRanges = { ["Revenue"] = "Model!D2:D10" },
        };
    }

    /// <summary>A schedule shaped for <c>ImportFromSpreadsheetAsync</c>: title, start, end, location.</summary>
    private static SpreadsheetModel BuildTimeline(DateTimeOffset now)
    {
        var sheet = new SheetTab { Id = "s1", Name = "Timeline", FrozenRows = 1, ColCount = 5 };

        var headers = new[] { "Milestone", "Start", "End", "Location", "Owner" };
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cells[new CellRef(0, i).Key] = new Cell { V = headers[i], S = "header" };
        }

        var milestones = new[]
        {
            ("Kickoff & scope freeze", 0, 0, "Meeting Room A", "Kang Fadhil"),
            ("Design system handoff", 5, 6, "Design Studio", "Dewi Lestari"),
            ("Backend feature complete", 12, 14, "Remote", "Budi Santoso"),
            ("QA regression pass", 18, 21, "QA Lab", "Maya Anggraini"),
            ("Load & soak testing", 22, 24, "Remote", "Agus Setiawan"),
            ("Launch readiness review", 27, 27, "Boardroom", "Sari Wulandari"),
            ("Public launch", 30, 30, "Online", "Kang Fadhil"),
        };

        for (var r = 0; r < milestones.Length; r++)
        {
            var (title, startOffset, endOffset, location, ownerName) = milestones[r];
            var row = r + 1;

            var start = TimeZoneInfo.ConvertTime(now, DemoZone).Date.AddDays(startOffset).AddHours(9);
            var end = TimeZoneInfo.ConvertTime(now, DemoZone).Date.AddDays(endOffset).AddHours(17);

            sheet.Cells[new CellRef(row, 0).Key] = new Cell { V = title };
            // ISO strings so the importer's date parsing path is exercised as well as serial numbers.
            sheet.Cells[new CellRef(row, 1).Key] = new Cell { V = start.ToString("yyyy-MM-dd HH:mm") };
            sheet.Cells[new CellRef(row, 2).Key] = new Cell { V = end.ToString("yyyy-MM-dd HH:mm") };
            sheet.Cells[new CellRef(row, 3).Key] = new Cell { V = location };
            sheet.Cells[new CellRef(row, 4).Key] = new Cell { V = ownerName };
        }

        sheet.ColWidths["A"] = 220;
        sheet.ColWidths["B"] = 150;
        sheet.ColWidths["C"] = 150;

        return new SpreadsheetModel
        {
            Sheets = [sheet],
            Styles = { ["header"] = new CellStyle { Bold = true, Background = "#eef2ff", Color = "#3730a3" } },
        };
    }

    /// <summary>Flat records with repeated dimensions, so the pivot table has something to aggregate.</summary>
    private static SpreadsheetModel BuildSalesAnalysis()
    {
        var sheet = new SheetTab { Id = "s1", Name = "Sales", FrozenRows = 1, ColCount = 6, RowCount = 60 };

        var headers = new[] { "Region", "Product", "Quarter", "Units", "Unit Price", "Total" };
        for (var i = 0; i < headers.Length; i++)
        {
            sheet.Cells[new CellRef(0, i).Key] = new Cell { V = headers[i], S = "header" };
        }

        var regions = new[] { "Jakarta", "Surabaya", "Bandung", "Medan" };
        var products = new[] { "Docs Pro", "Sheets Pro", "Slides Pro" };
        var quarters = new[] { "Q1", "Q2", "Q3" };

        // Deterministic pseudo-random so the seeded numbers are stable across runs.
        var random = new Random(20260817);
        var row = 1;

        foreach (var region in regions)
        {
            foreach (var product in products)
            {
                foreach (var quarter in quarters)
                {
                    var units = random.Next(40, 400);
                    var price = product switch
                    {
                        "Docs Pro" => 9.99,
                        "Sheets Pro" => 12.99,
                        _ => 11.49,
                    };

                    var excelRow = row + 1;
                    sheet.Cells[new CellRef(row, 0).Key] = new Cell { V = region };
                    sheet.Cells[new CellRef(row, 1).Key] = new Cell { V = product };
                    sheet.Cells[new CellRef(row, 2).Key] = new Cell { V = quarter };
                    sheet.Cells[new CellRef(row, 3).Key] = new Cell { V = units.ToString() };
                    sheet.Cells[new CellRef(row, 4).Key] = new Cell
                    {
                        V = price.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Fmt = "0.00",
                    };
                    sheet.Cells[new CellRef(row, 5).Key] = new Cell
                    {
                        F = $"=D{excelRow}*E{excelRow}",
                        Fmt = "#,##0.00",
                    };

                    row++;
                }
            }
        }

        var lastRow = row;

        sheet.Pivots.Add(new PivotSpec
        {
            Title = "Revenue by region and quarter",
            SourceRange = $"A1:F{lastRow}",
            Rows = ["Region"],
            Columns = ["Quarter"],
            Values = [new PivotValue { Field = "Total", Aggregate = "sum", Label = "Revenue" }],
        });

        sheet.ConditionalFormats.Add(new ConditionalFormatRule
        {
            Range = $"F2:F{lastRow}",
            Op = "colorScale",
            ScaleColors = ["#fef3c7", "#fb923c", "#b91c1c"],
        });

        sheet.Charts.Add(new ChartSpec
        {
            Title = "Units by region",
            Kind = "bar",
            CategoryRange = "A2:A13",
            SeriesRanges = ["D2:D13"],
            SeriesNames = ["Units"],
        });

        return new SpreadsheetModel
        {
            Sheets = [sheet],
            Styles = { ["header"] = new CellStyle { Bold = true, Background = "#eef2ff", Color = "#3730a3" } },
        };
    }

    // ─────────────────────────────────── presentation data ───────────────────────────────────

    private static PresentationModel BuildLaunchDeck() => new()
    {
        Theme = "aurora",
        Slides =
        [
            new Slide
            {
                Layout = "title",
                Transition = "fade",
                Notes = "Open with the problem, not the product.",
                Elements =
                [
                    new SlideElement
                    {
                        Type = "text", X = 8, Y = 32, W = 84, H = 22,
                        Text = "<h1>VibeDesk</h1>",
                        Style = new SlideElementStyle { FontSize = 64, Bold = true, Align = "center" },
                        Animation = new SlideAnimation { Type = "fadeInUp", DurationMs = 600 },
                    },
                    new SlideElement
                    {
                        Type = "text", X = 8, Y = 56, W = 84, H = 12,
                        Text = "One workspace for documents, data, decks and time",
                        Style = new SlideElementStyle { FontSize = 24, Align = "center" },
                        Animation = new SlideAnimation { Type = "fadeIn", DelayMs = 300, Order = 1 },
                    },
                ],
            },
            new Slide
            {
                Layout = "titleContent",
                Transition = "slideLeft",
                Notes = "Three points only. Do not read the slide.",
                Elements =
                [
                    new SlideElement
                    {
                        Type = "text", X = 8, Y = 12, W = 84, H = 14,
                        Text = "<h2>Why now</h2>",
                        Style = new SlideElementStyle { FontSize = 40, Bold = true },
                    },
                    new SlideElement
                    {
                        Type = "text", X = 8, Y = 30, W = 84, H = 50,
                        Text = "<ul><li>Teams pay for four tools that do not talk to each other.</li>"
                               + "<li>Assistants are bolted on, not grounded in your files.</li>"
                               + "<li>Self-hosting is either impossible or a second product.</li></ul>",
                        Style = new SlideElementStyle { FontSize = 22 },
                        Animation = new SlideAnimation { Type = "slideInLeft", Order = 1 },
                    },
                ],
            },
            new Slide
            {
                Layout = "twoColumn",
                Transition = "zoom",
                Notes = "Pause here — this is the differentiator.",
                Elements =
                [
                    new SlideElement
                    {
                        Type = "text", X = 8, Y = 12, W = 84, H = 12,
                        Text = "<h2>Mr Clippy knows your workspace</h2>",
                        Style = new SlideElementStyle { FontSize = 36, Bold = true },
                    },
                    new SlideElement
                    {
                        Type = "text", X = 8, Y = 30, W = 40, H = 50,
                        Text = "<p>Ask about a spreadsheet and it reads the spreadsheet. "
                               + "Ask it to build a deck and it writes into Slides.</p>",
                        Style = new SlideElementStyle { FontSize = 20 },
                    },
                    new SlideElement
                    {
                        Type = "shape", X = 54, Y = 30, W = 38, H = 44,
                        Style = new SlideElementStyle
                        {
                            Shape = "rect", Background = "rgba(255,255,255,0.12)", BorderRadius = 16,
                        },
                        Animation = new SlideAnimation { Type = "zoomIn", Order = 1 },
                    },
                ],
            },
            new Slide
            {
                Layout = "sectionHeader",
                Transition = "fade",
                Elements =
                [
                    new SlideElement
                    {
                        Type = "text", X = 8, Y = 42, W = 84, H = 16,
                        Text = "<h2>Built by Gravicode Studios</h2>",
                        Style = new SlideElementStyle { FontSize = 40, Bold = true, Align = "center" },
                    },
                    new SlideElement
                    {
                        Type = "text", X = 8, Y = 60, W = 84, H = 8,
                        Text = "Led by Kang Fadhil",
                        Style = new SlideElementStyle { FontSize = 20, Align = "center" },
                    },
                ],
            },
        ],
    };

    private static PresentationModel BuildDesignDeck() => new()
    {
        Theme = "mint",
        Slides =
        [
            new Slide
            {
                Layout = "title",
                Elements =
                [
                    new SlideElement
                    {
                        Type = "text", X = 8, Y = 36, W = 84, H = 20,
                        Text = "<h1>Design System Review</h1>",
                        Style = new SlideElementStyle { FontSize = 52, Bold = true, Align = "center" },
                    },
                ],
            },
            new Slide
            {
                Layout = "titleContent",
                Transition = "slideUp",
                Elements =
                [
                    new SlideElement
                    {
                        Type = "text", X = 8, Y = 12, W = 84, H = 12,
                        Text = "<h2>Dark theme audit</h2>",
                        Style = new SlideElementStyle { FontSize = 36, Bold = true },
                    },
                    new SlideElement
                    {
                        Type = "text", X = 8, Y = 28, W = 84, H = 50,
                        Text = "<ul><li>Contrast passes AA everywhere except chart legends.</li>"
                               + "<li>Slides canvas needs a distinct surface token.</li>"
                               + "<li>Focus rings must not rely on colour alone.</li></ul>",
                        Style = new SlideElementStyle { FontSize = 22 },
                    },
                ],
            },
        ],
    };

    // ─────────────────────────────────── sharing & comments ───────────────────────────────────

    private async Task SeedSharingAsync(List<AppUser> users, CancellationToken ct)
    {
        if (users.Count < 3) return;

        var owner = users[0];
        var shareable = await db.DriveItems
            .Where(x => x.OwnerId == owner.Id && x.Type != DriveItemType.Folder)
            .OrderBy(x => x.Name)
            .Take(4)
            .ToListAsync(ct);

        var roles = new[] { PermissionRole.Editor, PermissionRole.Commenter, PermissionRole.Viewer };

        for (var i = 0; i < shareable.Count; i++)
        {
            // Give each shared item a different mix so all three roles are represented in the demo.
            for (var u = 1; u < Math.Min(users.Count, 4); u++)
            {
                db.DriveItemPermissions.Add(new DriveItemPermission
                {
                    DriveItemId = shareable[i].Id,
                    UserId = users[u].Id,
                    Role = roles[(i + u) % roles.Length],
                    GrantedById = owner.Id,
                });
            }
        }

        // One link-shared item so the anonymous-share path has something to exercise.
        if (shareable.Count > 0)
        {
            shareable[0].Scope = ShareScope.AnyoneWithLink;
            shareable[0].LinkRole = PermissionRole.Viewer;
            shareable[0].ShareToken = Convert.ToHexString(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedCommentsAsync(List<AppUser> users, CancellationToken ct)
    {
        if (users.Count < 3) return;

        var doc = await db.DriveItems
            .FirstOrDefaultAsync(x => x.Type == DriveItemType.Document, ct);
        if (doc is null) return;

        var thread = new Comment
        {
            DriveItemId = doc.Id,
            AuthorId = users[1].Id,
            Body = "Should we call out the offline story here? It's a differentiator.",
            Anchor = """{"from":120,"to":168}""",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-6),
        };

        db.Comments.Add(thread);

        db.Comments.Add(new Comment
        {
            DriveItemId = doc.Id,
            ParentCommentId = thread.Id,
            AuthorId = users[0].Id,
            Body = "Agreed — I'll add a paragraph under Goals.",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-5),
        });

        // A suggestion with the original text recorded, so accepting it can actually be applied.
        db.Comments.Add(new Comment
        {
            DriveItemId = doc.Id,
            AuthorId = users[2].Id,
            Kind = CommentKind.Suggestion,
            Body = "Tighten this phrasing.",
            OriginalText = "web-first office suite",
            SuggestedText = "web-first, collaboration-first office suite",
            Anchor = """{"from":40,"to":62}""",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-3),
        });

        var sheet = await db.DriveItems
            .FirstOrDefaultAsync(x => x.Type == DriveItemType.Spreadsheet, ct);

        if (sheet is not null && users.Count > 4)
        {
            db.Comments.Add(new Comment
            {
                DriveItemId = sheet.Id,
                AuthorId = users[4].Id,
                Body = "Is the 42% cost ratio still right for Q3?",
                Anchor = """{"sheet":0,"a1":"E2"}""",
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The demo workspace's time zone. Seeded events are built as wall-clock times here and then
    /// converted to UTC — computing "09:00" from a UTC date instead lands the demo standup at 02:00
    /// local, which makes the sample data look broken even though the calendar is correct.
    /// </summary>
    private static readonly TimeZoneInfo DemoZone = ResolveDemoZone();

    private static TimeZoneInfo ResolveDemoZone()
    {
        // Windows and Linux disagree on time-zone ids, so try both spellings before giving up.
        foreach (var id in new[] { "Asia/Jakarta", "SE Asia Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the next spelling.
            }
            catch (InvalidTimeZoneException)
            {
                // Corrupt zone data; fall through to UTC.
            }
        }

        return TimeZoneInfo.Utc;
    }

    /// <summary>Builds a UTC instant from a wall-clock time in <see cref="DemoZone"/>.</summary>
    private static DateTimeOffset Local(DateTimeOffset now, int addDays, int hour, int minute = 0)
    {
        var zoned = TimeZoneInfo.ConvertTime(now, DemoZone);
        var wall = zoned.Date.AddDays(addDays).AddHours(hour).AddMinutes(minute);
        var offset = DemoZone.GetUtcOffset(wall);

        return new DateTimeOffset(wall, offset).ToUniversalTime();
    }

    private async Task SeedCalendarsAsync(List<AppUser> users, DateTimeOffset now, CancellationToken ct)
    {
        var owner = users[0];

        var primary = new Calendar
        {
            Name = "My calendar",
            OwnerId = owner.Id,
            IsPrimary = true,
            Color = "#6366f1",
            TimeZoneId = "Asia/Jakarta",
        };

        var team = new Calendar
        {
            Name = "Team — Q3 Launch",
            OwnerId = owner.Id,
            Color = "#ec4899",
            Description = "Shared launch schedule",
            TimeZoneId = "Asia/Jakarta",
        };

        db.Calendars.AddRange(primary, team);

        // Every other member also needs a primary calendar, or event creation would have nowhere to go.
        foreach (var member in users.Skip(1))
        {
            db.Calendars.Add(new Calendar
            {
                Name = "My calendar",
                OwnerId = member.Id,
                IsPrimary = true,
                Color = member.AvatarColor ?? "#6366f1",
                TimeZoneId = member.TimeZoneId,
            });
        }

        foreach (var member in users.Skip(1).Take(4))
        {
            db.CalendarShares.Add(new CalendarShare
            {
                CalendarId = team.Id,
                UserId = member.Id,
                Role = PermissionRole.Editor,
            });
        }

        // A weekly recurring standup, restricted to weekdays via the by-day list.
        var standup = new CalendarEvent
        {
            CalendarId = team.Id,
            Title = "Daily standup",
            Description = "15 minutes, blockers only.",
            Location = "Meeting Room A",
            StartUtc = Local(now, 1, 9, 30),
            EndUtc = Local(now, 1, 9, 45),
            OrganizerId = owner.Id,
            Recurrence = RecurrenceFrequency.Weekly,
            RecurrenceInterval = 1,
            RecurrenceByDay = "1,2,3,4,5",
            RecurrenceUntilUtc = now.AddMonths(3),
        };

        standup.Reminders.Add(new EventReminder { MinutesBefore = 5 });

        var review = new CalendarEvent
        {
            CalendarId = team.Id,
            Title = "Launch readiness review",
            Description = "Go / no-go decision.",
            Location = "Boardroom",
            StartUtc = Local(now, 9, 14),
            EndUtc = Local(now, 9, 16),
            OrganizerId = owner.Id,
        };

        review.Reminders.Add(new EventReminder { MinutesBefore = 30 });
        review.Reminders.Add(new EventReminder { Method = ReminderMethod.Email, MinutesBefore = 1440 });

        var retro = new CalendarEvent
        {
            CalendarId = team.Id,
            Title = "Sprint retro",
            StartUtc = Local(now, 4, 16),
            EndUtc = Local(now, 4, 17),
            OrganizerId = owner.Id,
            Recurrence = RecurrenceFrequency.Weekly,
            RecurrenceInterval = 2,
            RecurrenceUntilUtc = now.AddMonths(2),
        };

        var focus = new CalendarEvent
        {
            CalendarId = primary.Id,
            Title = "Focus block — formula engine",
            StartUtc = Local(now, 2, 10),
            EndUtc = Local(now, 2, 13),
            OrganizerId = owner.Id,
            Visibility = EventVisibility.Private,
        };

        var offsite = new CalendarEvent
        {
            CalendarId = team.Id,
            Title = "Team offsite",
            Location = "Bandung",
            StartUtc = Local(now, 21, 0),
            EndUtc = Local(now, 23, 0),
            IsAllDay = true,
            OrganizerId = owner.Id,
        };

        db.CalendarEvents.AddRange(standup, review, retro, focus, offsite);

        foreach (var member in users.Skip(1).Take(4))
        {
            standup.Attendees.Add(new EventAttendee
            {
                CalendarEventId = standup.Id,
                Email = member.Email!,
                UserId = member.Id,
                DisplayName = member.DisplayName,
                Response = AttendeeResponse.Accepted,
            });

            review.Attendees.Add(new EventAttendee
            {
                CalendarEventId = review.Id,
                Email = member.Email!,
                UserId = member.Id,
                DisplayName = member.DisplayName,
                Response = AttendeeResponse.NeedsAction,
            });
        }

        await db.SaveChangesAsync(ct);

        // Attach the launch deck to the review, exercising the Calendar+Drive integration.
        var deck = await db.DriveItems
            .FirstOrDefaultAsync(x => x.Type == DriveItemType.Presentation, ct);

        if (deck is not null)
        {
            db.EventAttachments.Add(new EventAttachment
            {
                CalendarEventId = review.Id,
                DriveItemId = deck.Id,
                GrantAttendees = PermissionRole.Viewer,
            });

            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Tiny helper so seed data can address cells by row/column instead of building A1 strings.</summary>
    private readonly record struct CellRef(int Row, int Col)
    {
        public string Key => $"{ColumnName(Col)}{Row + 1}";

        private static string ColumnName(int col)
        {
            var name = string.Empty;
            var n = col;
            do
            {
                name = (char)('A' + n % 26) + name;
                n = n / 26 - 1;
            } while (n >= 0);
            return name;
        }
    }
}
