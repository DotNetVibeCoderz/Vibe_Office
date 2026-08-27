# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

CUAN — an Indonesian double-entry accounting web app (Blazor Server, .NET 10). Single project, no solution-level split, no test project. UI text, comments, and domain vocabulary are Bahasa Indonesia (Jurnal, Kas, Giro, Pembelian, Neraca); code identifiers are English. Match that mix when adding code.

## Commands

```bash
dotnet restore
dotnet build
dotnet run                       # http://localhost:5081 (profile "http")
dotnet run --launch-profile https # https://localhost:7223
dotnet watch                     # hot reload

node docs/capture-screenshots.js # regenerate docs/screenshots (app must be running)
```

Swagger UI is at `/swagger`, **registered only in Development** (see `Program.cs`); it auto-fills the API key via `wwwroot/js/swagger-auth.js`.

There are no tests and no linter config. See Database below before reaching for `dotnet ef migrations add`.

## Database

- Provider comes from `Database:Provider` in appsettings (`Sqlite` default; `SqlServer` and `MySql` are referenced and wired up). Connection strings live under `ConnectionStrings` keyed by provider name.
- **Schema is created with `EnsureCreatedAsync()`, not migrations.** There is no `Migrations/` folder. Changing a model does not update an existing `Cuan.db` — delete it and restart, or introduce migrations deliberately (which also means replacing the `EnsureCreatedAsync` call).
- `Cuan.db` is *not* committed; it is generated on first boot. `Cuan.db.bak` is a backup of the pre-refactor demo database.
- `DataSeeder.SeedAsync` bails out entirely if any Identity role exists (`if (await ctx.Roles.AnyAsync()) return;`). Seed changes only take effect against a fresh database.
- `Program.cs` separately ensures the default API key row exists, and calls `SettingsService.SyncCatalogAsync` on every boot — so **new settings keys are added to existing databases automatically**; only new tables/columns need a DB reset.

## Architecture

**`AppDbContext` (`Data/AppDbContext.cs`)** is both the Identity store (`IdentityDbContext<ApplicationUser, ApplicationRole, string>`) and the business context (~35 DbSets). `ApplicationUser`/`ApplicationRole` are declared at the bottom of this file, not in `Models/`.

**Audit logging is automatic in the DbContext.** `SaveChangesAsync` is overridden to snapshot every Added/Modified/Deleted entry into `AuditLogs`, guarded by an `_isSavingAudit` re-entrancy flag. `Services/AuditService.cs` is a separate manual path most code does not need — do not add manual audit calls for ordinary CRUD, it double-logs.

**Blazor pages talk to EF directly.** Every page injects `AppDbContext` (scoped, shared for the circuit's lifetime) and runs LINQ inline in `@code`. There is no repository layer for CRUD. Page header block:

```razor
@page "/master/xxx"
@rendermode @(RenderMode.InteractiveServer)
@attribute [Authorize(Roles = "Admin,Akuntan")]
@inject AppDbContext DbContext
@inject SettingsService Settings
@inject IJSRuntime JS
```

Concurrent DB operations on one circuit will throw; keep DB work sequential inside a component.

**Auth.** Cookie Identity with 7 roles: `Admin, Akuntan, Kasir, StafGudang, Sales, Purchasing, Viewer`. Login is *not* a Blazor event handler — `Login.razor` posts a plain form to the `/account/login` minimal-API endpoint in `Program.cs` so `Set-Cookie` works outside the SignalR circuit. Keep that pattern for any cookie-mutating flow. `Login.razor` uses `@layout BlankLayout` so the app shell is absent there.

Page access is enforced per-page with `[Authorize(Roles = ...)]`. The sidebar in `MainLayout.razor` now filters links by the user's roles via its `Menu` table — **when you add a page, add it to that table too**, and keep its `Roles` in sync with the page's `[Authorize]`.

## Core services (`Services/`)

These are the spine of the app. Prefer them over inline logic.

**`SettingsCatalog.cs` + `SettingsService.cs` — every tunable value.** `SettingsCatalog.All` is a single list of ~95 `SettingDef` records (key, label, default, group, editor type, description). It is the *only* source of truth: `DataSeeder` seeds `SystemSettings` from it, `SettingsService` uses it for defaults, and `/admin/settings` renders its whole form from it. **Adding a parameter = adding one line to that list.** `SettingsService` is a singleton with an in-memory cache; `SaveAsync` writes and reloads so changes apply without restart. It also owns display formatting (`Money`, `Number`, `FormatDate`, `Culture`).

Never hardcode a rate, prefix, account code, or credential — put it in the catalog and read it via `SettingsService`.

**`PostingService.cs` — the only place that changes account balances or stock quantities.** `ChartOfAccount.CurrentBalance` and `Item.StockQuantity` must not be written anywhere else. It provides:
- `PostJournalAsync` / `UnpostJournalAsync` — apply/reverse a journal against account balances
- `PostSalesInvoiceAsync`, `PostPurchaseInvoiceAsync`, `PostCashAsync`, `PostBankAsync`, `PostGiroAsync`, `PostStockAdjustmentAsync` — build the document's journal from the account mapping in Settings
- `ReverseDocumentJournalAsync` — call this before editing or deleting any document that has a `JournalEntryId`, or balances double-count
- `RecalculateAllBalancesAsync` — rebuild every balance from opening + posted journals (exposed on the Settings page)
- `ApplyStockMovementAsync`, `ApplySalesStockAsync`, `ApplyPurchaseStockAsync`
- `SignedAmount(accountType, debit, credit)` — normal-balance sign; static, reused by reports

**`DocumentNumberService.cs`** — sequential numbers derived from the max existing number in the DB (not an in-memory count), serialized behind a process-wide semaphore. Also `NextFakturPajakAsync` for NSFP. Format, prefixes, padding, and reset period come from Settings.

**Tax stack.** `TaxService` (DPP/PPN computation, period summaries, faktur issuance, SPT build/revise), `EfakturExportService` (e-Faktur CSV, Coretax XML, e-Bupot CSV), `DjpClient` (host-to-host: OAuth token, submit faktur/SPT, logs every exchange to `DjpSubmission`). See `docs/PAJAK.md`.

**`ImportService.cs` + `MasterImportPlans.cs` — Excel import for master data.** `ImportPlan<T>` declares the columns once; the same definition generates the .xlsx template *and* parses the upload, so they can't drift apart. `MasterImportPlans` builds a plan per entity (COA, items, customers, suppliers, warehouses, currencies, taxes, bank accounts), resolving text to ids via `ImportService.Lookup` — build it lazily when the dialog opens, since the lookup tables come from the DB. Parse never touches the DB; `Plan.Commit` runs only when the user confirms. Rows whose `KeyOf` already exists update instead of inserting; invalid rows are skipped with a per-row reason rather than failing the file.

To add import to a new master page: add a plan method, then `@inject MasterImportPlans`, a button calling `OpenImport`, and `<ImportDialog TEntity="..." Plan="importPlan" OnImported="..." OnClose="..." />`. `docs/test-import.js` exercises the whole flow against a running app.

**Export.** Two distinct services:
- `Services/ExportService.cs` — static generic `ExportToCsv<T>` / `ExportToExcel<T>`, not DI-registered. Pages call it statically, base64 the bytes, and pass them to `downloadFileFromBytes` in `wwwroot/js/download.js`.
- `Services/ReportExportService.cs` — DI-registered, hand-built Excel/PDF (QuestPDF) for the executive dashboard only.

**Notifications.** `NotificationHub` (SignalR at `/notificationHub`) plus `NotificationService`, which writes a `Notification` row and broadcasts. `NotificationBell.razor` in the topbar reads rows from the DB when opened.

## REST API (`Api/ApiController.cs`)

One controller, ~24 endpoints grouped by `#region`. Route is `[Route("api/v1")]` written literally — **not** `[controller]`, which resolved to `/api/v1/Api/...` because the class is named `ApiController`.

Auth is a hand-rolled `ValidateApiKey()` checking the `X-Api-Key` header against the `ApiKeys` table. **Every action must call it first and return `Unauthorized` itself** — no filter or middleware does this, so a new endpoint that omits the call is silently public. The `ApiPolicy` rate limiter (100/min) is registered but not applied to the controller.

JSON uses `ReferenceHandler.IgnoreCycles`; EF navigation properties (e.g. `ChartOfAccount.Parent`/`Children`) otherwise throw a cycle exception.

## UI

**Design language: "Buku Besar" (the ledger).** `wwwroot/app.css` (~700 lines) holds the whole system as `cu-*` classes over `--cu-*` variables. No CSS framework.

- Greenbar alternating row tint; debit is blue ink (`--cu-debit`), credit is red ink (`--cu-credit`), used consistently across journal, ledger, trial balance, and tax tables.
- **The accountant's double rule is the signature element**: `.cu-total-row` closes every final total, and `.cu-balance` uses it as a state indicator — dashed red when unbalanced, double green when balanced.
- Money and document numbers use IBM Plex Mono (`.cu-mono` / `.cu-num`); everything else uses Archivo. Both load from Google Fonts in `App.razor`.
- Dark theme is a full `:root[data-theme="dark"]` token set. `wwwroot/js/theme.js` applies the saved theme before render to avoid a flash; the topbar toggle persists to `localStorage`.

**Shared components (`Components/Shared/`)**: `Icon.razor` (inline SVG set — use this, not emoji), `PageHead.razor`, `EmptyState.razor`, `NotificationBell.razor`.

Prefer `Settings.Money(x)` / `Settings.Number(x)` / `Settings.FormatDate(d)` over raw `ToString("N0")` so currency and date formats follow the settings.

## Domain behaviour

Unlike earlier versions of this app, posting is now real:

- Journals move `ChartOfAccount.CurrentBalance`; all financial reports read those balances, and Trial Balance / Balance Sheet verify they balance.
- Sales and purchase invoices create journals and stock movements; `AutoPostJournal` (default on) controls this.
- `StockMovement` updates `Item.StockQuantity` and per-warehouse `ItemStock`.
- `BankAccount.Balance` is maintained by `BankPage.razor`, and seeded bank accounts are linked to their COA via `ChartOfAccountId` (the cash-flow report depends on that link).
- VAT comes from `PpnRate` in Settings, not a hardcoded `0.11m`.
- Revenue and expense are temporary accounts: the accounting equation only balances when current-period profit is included in equity. `Home.razor` and `BalanceSheetPage.razor` both do this.

## Config

`appsettings.json` holds only what is needed before the database opens: provider, connection strings, default API key. Everything runtime-tunable lives in `SystemSettings` and is edited at `/admin/settings`.

Demo credentials (seeded): `admin@cuan.id` … `viewer@cuan.id`, password `Cuan@123`. Default API key: `cu4n-4p1-k3y-2024-d3f4ult`.

The seeder generates a rolling 12-month window of demo transactions ending in the current month, so reports are never empty regardless of the year.

## Related docs

`README.md` (overview + screenshots), `docs/README.md` (screenshot gallery), `docs/PAJAK.md` (tax and DJP flow), `PLAN.md` (original phase checklist — historical).
