# Architecture

[← README](../README.md) · [Configuration](configuration.md) · [Apps](apps.md) · [API](api.md) · [Mr Clippy](assistant.md) · [Scripts](scripting.md) · [Development](development.md)

---

## Layers

```
Domain ← Application ← Infrastructure ← Web / Api
                    ↖ Ai            ↙
                       Ui ←────────┘
```

| Project | Depends on | Holds |
|---|---|---|
| `VibeDesk.Domain` | nothing | Entities and enums |
| `VibeDesk.Application` | Domain | Contracts, DTOs, document models, the formula engine |
| `VibeDesk.Infrastructure` | Application | EF Core, Identity, storage, cache, service implementations |
| `VibeDesk.Ai` | Application, Infrastructure | Mr Clippy |
| `VibeDesk.Ui` | Application | Every component **and every page**, in a Razor class library |
| `VibeDesk.Client` | Application, Ui | The same contracts over HTTP, plus sign-in and auth state |
| `VibeDesk.Web` | Ui, Infrastructure, Ai | Blazor Server host |
| `VibeDesk.Api` | Infrastructure, Ai | REST, SignalR, gRPC |
| `VibeDesk.Desktop` / `.Mobile` | Ui, Client | Thin shells |

**`VibeDesk.Ui` depends only on `Application`.** That is what lets the same components render in a
server-side circuit, a WebView2 window and a MAUI app: the UI never sees EF Core, and every capability
reaches it as an interface someone else implements.

**Desktop and mobile must not reference `Infrastructure`.** Identity's `AddDefaultTokenProviders` (the
TOTP backend) lives in the ASP.NET Core shared framework, so `Infrastructure` carries a
`FrameworkReference` to it. Pulling that into a client host would drag a web framework into a desktop
app. Those hosts talk to `VibeDesk.Api` instead.

`VibeDesk.Client` is how they do it: one class per Application interface, each mapping to the REST
routes, and an `ApiSession` that owns the bearer token. The clients translate HTTP failures back into
`NotFoundException` / `ForbiddenException` / `ValidationException`, so a page written against the
server-side services behaves identically when it is talking to the API — including returning *not
found* rather than *forbidden* for an item the caller cannot see.

Two members are deliberately not implemented over HTTP. `INotificationService.NotifyAsync` throws:
exposing it would let any signed-in caller push notifications to anyone. `ICalendarService.
DispatchDueRemindersAsync` returns 0: the reminder sweeper is a server job, and letting every device
run it would have them competing to dispatch the same reminders.

---

## Drive is the storage layer

There is no separate "documents" store. Docs, Sheets, Slides and uploaded files are all `DriveItem`
rows, which is why permissions, sharing, versioning, trash, search, quota and activity logging exist
once rather than five times.

### Metadata and payload are separate rows

`DriveItem` holds metadata; `DriveItemContent` holds the JSON payload in a 1:1 table. Listing a folder
is the hot path and must never load a megabyte of spreadsheet per row — splitting the table makes that
structural rather than a discipline every query has to remember.

`DriveItemContent.Data` is provider-agnostic JSON, shaped by the item's type:

| Type | Model |
|---|---|
| `Document` | `DocumentModel` — HTML body plus page setup |
| `Spreadsheet` | `SpreadsheetModel` — sheets, styles, named ranges |
| `Presentation` | `PresentationModel` — theme, slides, elements |

Cells use deliberately short property names (`V`, `F`, `S`, `Fmt`, `N`). They repeat thousands of times
in a single payload, so the names are a meaningful share of the stored bytes.

### Materialised paths

Each item stores its ancestor chain as `/{guid}/{guid}/`. A permission granted on a folder applies to
its whole subtree via a prefix match, and a move rewrites the subtree in one `ExecuteUpdate` instead of
walking it. The trade-off is that the visibility filter is not index-friendly at large scale; a closure
table is the documented next step.

Deletes run bottom-up, because the parent foreign key is `Restrict` — a deliberate choice so a bug can
never cascade a folder's contents into oblivion.

---

## Permissions

`PermissionRole` is ordered — `None < Viewer < Commenter < Editor < Owner` — and comparisons rely on
that numeric order.

Effective role is the **maximum** of four sources, resolved in a single indexed query:

1. Ownership
2. A direct grant on the item
3. A grant on any ancestor, found through the materialised path
4. Link sharing (`ShareScope` plus `LinkRole`)

`RequireAsync` throws `NotFoundException`, not `ForbiddenException`, when the resolved role is `None`.
Returning *forbidden* would confirm that the id exists.

A grant can be made to an email address that has no account yet; it is claimed automatically when that
person registers.

---

## Concurrency

Collaborative saves use a **revision counter**, not last-write-wins. `DriveItemContent.Revision` is
monotonic; a save carries the revision the client edited from, and if the stored revision has moved on
the save is rejected and the server's copy comes back so the client can rebase.

`baseRevision: -1` forces the write, for the cases (restore, suggestion accept) where the server itself
is the author.

Row versioning is the one place providers genuinely diverge, so it is handled in `AppDbContext` alone:
SQL Server gets a native `rowversion`; the others are client-stamped in `SaveChanges`. SQLite also gets
a `DateTimeOffset` → UTC-ticks converter, because ordering and range queries have to stay correct.

---

## Migrations: one assembly per provider

EF Core discovers every `Migration` type in the migrations assembly. Four providers in one project
would therefore collide. Each provider gets a thin project — `VibeDesk.Migrations.{Sqlite,SqlServer,
MySql,PostgreSql}` — with its own `IDesignTimeDbContextFactory`, since EF only scans the startup and
target assemblies.

The host references all four, so changing `Database:Provider` needs no rebuild.

---

## Rendering model

Blazor Server, with the render mode decided **per request** rather than per component:

```csharp
private IComponentRenderMode? RenderModeForPage =>
    HttpContext.AcceptsInteractiveRouting() ? InteractiveServer : null;
```

Marking `MainLayout` itself `@rendermode InteractiveServer` makes the layout an interactive boundary,
and `Body` — a `RenderFragment` — cannot be serialised across one. Deciding at `<Routes>` keeps account
pages static (they are marked `[ExcludeFromInteractiveRouting]`) while every app page is interactive.

Pages inject chrome into the shell through `SectionOutlet` / `SectionContent`, so a page owns its own
toolbar without the layout knowing what any app needs.

---

## Storage and cache

`IStorageProvider` has four implementations — filesystem, Azure Blob, and one S3 provider that serves
both S3 and MinIO through path-style addressing.

`EncryptingStorageProvider` is a **decorator** applied at registration, not logic inside each provider,
so all four gain AES-256-GCM for free. The wire format is `[magic:4][nonce:12][tag:16][ciphertext]`;
the magic prefix lets objects written before encryption was enabled pass through unchanged.

The Redis cache tracks tags as **sets**, not by scanning keys — `KEYS pattern` is O(keyspace) and
blocks the server.
