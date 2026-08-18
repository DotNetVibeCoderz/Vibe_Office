using Microsoft.AspNetCore.Mvc;
using VibeDesk.Application.Scripting;
using VibeDesk.Domain;

namespace VibeDesk.Api.Endpoints;

/// <summary>The scripting engine over HTTP: authoring, running, triggers, templates, marketplace.</summary>
public static class ScriptEndpoints
{
    public static IEndpointRouteBuilder MapScriptEndpoints(this IEndpointRouteBuilder app)
    {
        var scripts = app.MapGroup("/api/scripts").WithTags("Scripts").RequireAuthorization();

        scripts.MapGet("/", async (IScriptService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(ct)))
            .WithSummary("The caller's scripts");

        scripts.MapGet("/languages", (IScriptExecutor executor) => Results.Ok(new
        {
            languages = executor.AvailableLanguages,
            events = ScriptEvents.All.Select(e => new { name = e.Name, description = e.Description }),
            scopes = Enum.GetNames<ScriptScope>(),
        })).WithSummary("What this server can run, and what triggers and scopes exist");

        scripts.MapGet("/{id:guid}", async (Guid id, IScriptService service, CancellationToken ct) =>
                await service.GetAsync(id, ct) is { } script ? Results.Ok(script) : Results.NotFound())
            .WithSummary("One script with its code and triggers");

        scripts.MapPost("/", async (ScriptInput input, IScriptService service, CancellationToken ct) =>
                Results.Ok(await service.SaveAsync(input, ct)))
            .WithSummary("Create or update a script; set Id to update");

        scripts.MapDelete("/{id:guid}", async (Guid id, IScriptService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        }).WithSummary("Delete a script and its history");

        scripts.MapPost("/validate", (ValidateRequest request, IScriptExecutor executor) =>
        {
            var problem = executor.Validate(request.Language, request.Code);

            return Results.Ok(new { ok = problem is null, problem });
        }).WithSummary("Static check only — syntax, and for C# the sandbox analysis");

        scripts.MapPost("/{id:guid}/run", async (
            Guid id,
            RunRequest? request,
            IScriptService service,
            CancellationToken ct) =>
                Results.Ok(await service.RunAsync(id, request?.Input, ScriptRunTrigger.Api, ct)))
            .WithSummary("Run a saved script now");

        scripts.MapPost("/run", async (
            AdHocRunRequest request,
            IScriptExecutor executor,
            CancellationToken ct) =>
        {
            // Unsaved source, for the editor's "test run" and the CLI's `run <file>`. Not recorded:
            // a run history full of half-written drafts is a run history nobody reads.
            var result = await executor.ExecuteAsync(new ScriptExecutionRequest
            {
                Name = request.Name ?? "ad-hoc",
                Language = request.Language,
                Code = request.Code,
                Scopes = request.Scopes,
                AllowedHosts = request.AllowedHosts,
                TimeoutSeconds = request.TimeoutSeconds,
                Input = request.Input,
                TriggeredBy = ScriptRunTrigger.Api,
                Record = false,
            }, ct);

            return Results.Ok(result);
        }).WithSummary("Run source that has not been saved");

        scripts.MapGet("/{id:guid}/versions", async (
            Guid id, IScriptService service, CancellationToken ct) =>
                Results.Ok(await service.ListVersionsAsync(id, ct)))
            .WithSummary("Version history");

        scripts.MapGet("/{id:guid}/versions/{versionId:guid}", async (
            Guid id, Guid versionId, IScriptService service, CancellationToken ct) =>
                await service.GetVersionCodeAsync(id, versionId, ct) is { } code
                    ? Results.Ok(new { code })
                    : Results.NotFound())
            .WithSummary("The source of one version");

        scripts.MapPost("/{id:guid}/triggers", async (
            Guid id, ScriptTriggerInput input, IScriptService service, CancellationToken ct) =>
                Results.Ok(await service.SaveTriggerAsync(id, input, ct)))
            .WithSummary("Add or update a trigger");

        scripts.MapDelete("/{id:guid}/triggers/{triggerId:guid}", async (
            Guid id, Guid triggerId, IScriptService service, CancellationToken ct) =>
        {
            await service.DeleteTriggerAsync(id, triggerId, ct);
            return Results.NoContent();
        }).WithSummary("Remove a trigger");

        // ── runs ──
        var runs = app.MapGroup("/api/script-runs").WithTags("Scripts").RequireAuthorization();

        runs.MapGet("/", async (
            [FromQuery] Guid? scriptId, [FromQuery] int? take, IScriptService service, CancellationToken ct) =>
                Results.Ok(await service.ListRunsAsync(scriptId, take ?? 50, ct)))
            .WithSummary("Run history — the audit log");

        runs.MapGet("/{runId:guid}", async (Guid runId, IScriptService service, CancellationToken ct) =>
                await service.GetRunAsync(runId, ct) is { } run ? Results.Ok(run) : Results.NotFound())
            .WithSummary("One run with its output and the API calls it made");

        // ── templates ──
        var templates = app.MapGroup("/api/script-templates").WithTags("Scripts").RequireAuthorization();

        templates.MapGet("/", (
            [FromQuery] string? q,
            [FromQuery] string? category,
            [FromQuery] ScriptLanguage? language,
            IScriptTemplateGallery gallery) =>
                Results.Ok(new
                {
                    categories = gallery.Categories,
                    // Code is omitted from the list: the gallery shows cards, and a page carrying
                    // twenty full scripts is a page nobody waits for.
                    templates = gallery.Search(q, category, language).Select(t => new
                    {
                        t.Id, t.Name, t.Summary, t.Category, t.Language, t.Scopes, t.AllowedHosts, t.Tags,
                    }),
                }))
            .WithSummary("Browse the template gallery");

        templates.MapGet("/{id}", (string id, IScriptTemplateGallery gallery) =>
                gallery.Find(id) is { } template ? Results.Ok(template) : Results.NotFound())
            .WithSummary("One template, with its code");

        templates.MapPost("/{id}/create", async (
            string id, IScriptTemplateGallery gallery, IScriptService service, CancellationToken ct) =>
        {
            var template = gallery.Find(id);
            if (template is null) return Results.NotFound();

            // Created disabled: a template needs its ids and keys filled in before it should run.
            var created = await service.SaveAsync(new ScriptInput
            {
                Name = template.Name,
                Description = template.Summary,
                Language = template.Language,
                Code = template.Code,
                Scopes = template.Scopes,
                AllowedHosts = template.AllowedHosts,
                TemplateId = template.Id,
                IsEnabled = false,
            }, ct);

            return Results.Ok(created);
        }).WithSummary("Create a script from a template");

        // ── marketplace ──
        var market = app.MapGroup("/api/script-market").WithTags("Scripts").RequireAuthorization();

        market.MapGet("/", async (
            [FromQuery] string? q, [FromQuery] string? category, IScriptService service, CancellationToken ct) =>
                Results.Ok(await service.BrowseAsync(q, category, ct)))
            .WithSummary("Scripts other people have shared");

        market.MapPost("/publish/{scriptId:guid}", async (
            Guid scriptId, PublishRequest? request, IScriptService service, CancellationToken ct) =>
                Results.Ok(await service.PublishAsync(scriptId, request?.Summary, request?.Category, ct)))
            .WithSummary("Share a script; the source is copied, not referenced");

        market.MapDelete("/{publicationId:guid}", async (
            Guid publicationId, IScriptService service, CancellationToken ct) =>
        {
            await service.UnpublishAsync(publicationId, ct);
            return Results.NoContent();
        }).WithSummary("Delist a shared script; installed copies keep working");

        market.MapPost("/{publicationId:guid}/install", async (
            Guid publicationId, IScriptService service, CancellationToken ct) =>
                Results.Ok(await service.InstallAsync(publicationId, ct)))
            .WithSummary("Copy a shared script into your workspace, disabled until you review it");

        return app;
    }

    public sealed record ValidateRequest(ScriptLanguage Language, string Code);
    public sealed record RunRequest(Dictionary<string, string>? Input);
    public sealed record PublishRequest(string? Summary, string? Category);

    public sealed record AdHocRunRequest(
        ScriptLanguage Language,
        string Code,
        ScriptScope Scopes,
        string? AllowedHosts,
        int? TimeoutSeconds,
        string? Name,
        Dictionary<string, string>? Input);
}
