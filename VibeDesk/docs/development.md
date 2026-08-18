# Development

[← README](../README.md) · [Architecture](architecture.md) · [Configuration](configuration.md) · [Apps](apps.md) · [API](api.md) · [Mr Clippy](assistant.md) · [Scripts](scripting.md)

---

## Commands

```bash
dotnet build                                  # whole solution
dotnet run --project src/VibeDesk.Web         # https://localhost:7181, http://localhost:5181
dotnet run --project src/VibeDesk.Api         # https://localhost:7299, docs at /scalar/v1
dotnet run --project src/VibeDesk.Desktop     # native window; needs the API running
dotnet build src/VibeDesk.Mobile -f net10.0-android
dotnet run --project tests/VibeDesk.Tests     # 213 tests
```

The CLI is a separate executable rather than a `dotnet run` target — it is meant to go on `PATH`:

```bash
dotnet publish src/VibeDesk.Cli -c Release    # produces vibedesk(.exe)
vibedesk config http://localhost:5299
vibedesk login fadhil@gravicode.com
```

The desktop and mobile hosts read `Api:BaseAddress`, overridable as `VIBEDESK_Api__BaseAddress`. The
web host talks to its services in-process and needs no API.

**Tests do not run through `dotnet test`.** xunit.v3 runs on Microsoft.Testing.Platform and the .NET 10
SDK removed the VSTest path for it. `dotnet.config` selects the right runner, but the test project is
an executable and running it directly is the reliable route.

Filter to one class:

```bash
dotnet run --project tests/VibeDesk.Tests -- --filter-class "*FormulaEngineTests"
```

The SDK is pinned to `10.0.400` in `global.json` with `allowPrerelease: false`, because several SDKs
including a 10.0 preview are installed side by side.

Package versions are centralised in `Directory.Packages.props`; project files carry
`<PackageReference Include="…" />` with no version.

---

## Migrations

One assembly per provider (see [Architecture](architecture.md#migrations-one-assembly-per-provider)).
Adding a migration means adding it to each provider you support:

```bash
dotnet ef migrations add <Name> \
  --project src/Migrations/VibeDesk.Migrations.Sqlite \
  --startup-project src/VibeDesk.Web
```

Things that will bite you once:

- Keep `dotnet-ef` in step with the runtime. A 10.0.10 tool against a 10.0.11 runtime fails with
  *"Could not load System.Runtime 10.0.0.0"*.
- The migration class libraries need `<GenerateRuntimeConfigurationFiles>true</…>` and their own
  `IDesignTimeDbContextFactory` — EF only scans the startup and target assemblies.

To start over in development, stop the app, delete `src/VibeDesk.Web/App_Data`, and run again. The
database is recreated and reseeded.

---

## Conventions

**Comments explain why, never what.** A comment that restates the next line is noise the moment the PR
merges. The ones worth writing state a constraint the code cannot show — why an operation is ordered a
certain way, which failure a guard prevents, what a provider does differently.

**Match the surrounding code.** Comment density, naming and idiom are local properties; new code should
be indistinguishable from what is already there.

**Errors are values where a user caused them.** `ValidationException`, `NotFoundException` and
`ForbiddenException` cross the layer boundary; the UI turns them into toasts and the API into status
codes.

**Pages live in `VibeDesk.Ui`, never in a host.** A page in `VibeDesk.Web` is a page the desktop and
mobile apps cannot render. Adding one means two registrations, not one: `AdditionalAssemblies` on
`<Router>` *and* `.AddAdditionalAssemblies(...)` on `MapRazorComponents<App>()` — routing in a Blazor
Web App is resolved by the endpoint system as well as the router, and doing only one of the two
yields a 404 with no other symptom.

**Clients never reference `VibeDesk.Infrastructure`.** `VibeDesk.Client` implements the same
Application contracts over HTTP; that is what keeps the ASP.NET Core server framework out of a
desktop and mobile app. If a client project needs a new capability, the route goes in
`VibeDesk.Api` and the implementation in `VibeDesk.Client`.

---

## UI notes

- Design tokens live in `wwwroot/css/tokens.css`. Light values sit on bare `:root`; dark values are
  overrides in **both** `@media (prefers-color-scheme: dark) :root:not([data-theme="light"])` **and**
  `:root[data-theme="dark"]`, so an explicit toggle wins in both directions and "system" still works.
- The theme is applied by an inline `<head>` script before first paint, so there is no flash.
- `SectionContent` needs `@using Microsoft.AspNetCore.Components.Sections`, and `Virtualize` needs
  `@using Microsoft.AspNetCore.Components.Web.Virtualization`. Missing either is a *warning*, not an
  error — the toolbar or the grid just silently renders empty.
- A component with a named fragment (e.g. `Footer`) cannot also take implicit child content; wrap call
  sites in an explicit `<ChildContent>`.
- `RenderTreeBuilder` is in `Microsoft.AspNetCore.Components.Rendering`, not `.RenderTree`.

---

## Screenshots

`docs/screenshots/` is produced against the running app through the Chrome DevTools Protocol —
`Network.setCookie` for an authenticated session, `Emulation.setEmulatedMedia` for the dark-mode shots.

Enable `Network.setCacheDisabled`. A persistent browser profile will happily serve a stale stylesheet
and make a correct layout look broken, which costs far more time than the flag does.
