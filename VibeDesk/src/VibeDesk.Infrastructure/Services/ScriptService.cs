using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Drive;
using VibeDesk.Application.Platform;
using VibeDesk.Application.Scripting;
using VibeDesk.Domain;
using VibeDesk.Domain.Entities;
using VibeDesk.Infrastructure.Persistence;

namespace VibeDesk.Infrastructure.Services;

/// <summary>
/// Scripts, versions, triggers, runs and the marketplace.
/// </summary>
/// <remarks>
/// Persistence and orchestration only — the actual execution belongs to
/// <see cref="IScriptExecutor"/>, which lives in the scripting layer and knows nothing about EF Core.
/// </remarks>
public sealed class ScriptService(
    AppDbContext db,
    ICurrentUser currentUser,
    IUserDirectory users,
    IDriveService drive,
    IScriptExecutor executor,
    ILogger<ScriptService> logger) : IScriptService, IScriptTriggerLookup
{
    private const int MaxRunsKept = 200;

    public async Task<IReadOnlyList<ScriptDto>> ListAsync(CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        var scripts = await db.Scripts
            .AsNoTracking()
            .Where(s => s.OwnerId == userId)
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => new
            {
                Script = s,
                Triggers = s.Triggers.Count,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var owner = await users.GetAsync(userId, ct).ConfigureAwait(false);

        return [.. scripts.Select(x => Map(x.Script, owner?.DisplayName ?? "Me", x.Triggers))];
    }

    public async Task<ScriptDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var script = await FindAsync(id, ct).ConfigureAwait(false);
        if (script is null) return null;

        var triggers = await db.ScriptTriggers
            .AsNoTracking()
            .Where(t => t.ScriptId == id)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var itemNames = await ResolveItemNamesAsync(triggers, ct).ConfigureAwait(false);
        var owner = await users.GetAsync(script.OwnerId, ct).ConfigureAwait(false);

        return new ScriptDetailDto(
            Map(script, owner?.DisplayName ?? "Me", triggers.Count),
            script.Code,
            [.. triggers.Select(t => MapTrigger(t, itemNames.GetValueOrDefault(t.DriveItemId ?? Guid.Empty)))]);
    }

    public async Task<ScriptDto> SaveAsync(ScriptInput input, CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new ValidationException("A script needs a name.");
        }

        var script = input.Id is { } id
            ? await FindAsync(id, ct).ConfigureAwait(false)
              ?? throw new NotFoundException("That script could not be found.")
            : new Script { OwnerId = userId };

        var codeChanged = script.Code != input.Code;

        if (input.Id is null)
        {
            db.Scripts.Add(script);
        }
        else if (codeChanged)
        {
            // Snapshot what is being replaced, not what is replacing it — so a version restores the
            // state the script was actually in.
            db.ScriptVersions.Add(new ScriptVersion
            {
                ScriptId = script.Id,
                VersionNumber = script.VersionNumber,
                Code = script.Code,
                Note = input.VersionNote,
                AuthorId = userId,
            });

            script.VersionNumber++;
        }

        script.Name = input.Name.Trim();
        script.Description = input.Description;
        script.Language = input.Language;
        script.Code = input.Code;
        script.Scopes = input.Scopes;
        script.AllowedHosts = Normalise(input.AllowedHosts);
        script.TimeoutSeconds = input.TimeoutSeconds;
        script.IsEnabled = input.IsEnabled;
        script.TemplateId ??= input.TemplateId;
        script.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var owner = await users.GetAsync(userId, ct).ConfigureAwait(false);
        var triggerCount = await db.ScriptTriggers.CountAsync(t => t.ScriptId == script.Id, ct)
            .ConfigureAwait(false);

        return Map(script, owner?.DisplayName ?? "Me", triggerCount);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var script = await FindAsync(id, ct).ConfigureAwait(false);
        if (script is null) return;

        db.Scripts.Remove(script);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ScriptVersionDto>> ListVersionsAsync(
        Guid scriptId, CancellationToken ct = default)
    {
        if (await FindAsync(scriptId, ct).ConfigureAwait(false) is null) return [];

        var versions = await db.ScriptVersions
            .AsNoTracking()
            .Where(v => v.ScriptId == scriptId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var authors = await users
            .GetManyAsync(versions.Select(v => v.AuthorId).Distinct(), ct)
            .ConfigureAwait(false);

        return [.. versions.Select(v => new ScriptVersionDto(
            v.Id,
            v.VersionNumber,
            v.Note,
            v.AuthorId,
            authors.GetValueOrDefault(v.AuthorId)?.DisplayName ?? "Unknown",
            v.CreatedAt))];
    }

    public async Task<string?> GetVersionCodeAsync(
        Guid scriptId, Guid versionId, CancellationToken ct = default)
    {
        if (await FindAsync(scriptId, ct).ConfigureAwait(false) is null) return null;

        return await db.ScriptVersions
            .AsNoTracking()
            .Where(v => v.Id == versionId && v.ScriptId == scriptId)
            .Select(v => v.Code)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<ScriptTriggerDto> SaveTriggerAsync(
        Guid scriptId, ScriptTriggerInput input, CancellationToken ct = default)
    {
        var script = await FindAsync(scriptId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("That script could not be found.");

        var trigger = input.Id is { } id
            ? await db.ScriptTriggers.FirstOrDefaultAsync(t => t.Id == id && t.ScriptId == scriptId, ct)
                  .ConfigureAwait(false)
              ?? throw new NotFoundException("That trigger could not be found.")
            : new ScriptTrigger { ScriptId = script.Id };

        if (input.Kind == TriggerKind.Event && string.IsNullOrWhiteSpace(input.EventName))
        {
            throw new ValidationException("An event trigger needs an event name.");
        }

        if (input.Kind == TriggerKind.Schedule)
        {
            if (string.IsNullOrWhiteSpace(input.CronExpression))
            {
                throw new ValidationException("A schedule trigger needs a cron expression.");
            }

            // Validated here rather than at the first tick: a typo should be reported while the
            // author is looking at the form.
            if (!CronSchedule.TryParse(input.CronExpression, out _))
            {
                throw new ValidationException(
                    $"'{input.CronExpression}' is not a valid cron expression. Use 'minute hour day month weekday'.");
            }
        }

        trigger.Kind = input.Kind;
        trigger.EventName = input.Kind == TriggerKind.Event ? input.EventName : null;
        trigger.DriveItemId = input.DriveItemId;
        trigger.CronExpression = input.Kind == TriggerKind.Schedule ? input.CronExpression : null;
        trigger.TimeZoneId = string.IsNullOrWhiteSpace(input.TimeZoneId) ? "UTC" : input.TimeZoneId;
        trigger.IsEnabled = input.IsEnabled;

        trigger.NextRunAt = input.Kind == TriggerKind.Schedule
            ? CronSchedule.NextRun(trigger.CronExpression, trigger.TimeZoneId, DateTimeOffset.UtcNow)
            : null;

        if (input.Id is null) db.ScriptTriggers.Add(trigger);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var names = await ResolveItemNamesAsync([trigger], ct).ConfigureAwait(false);

        return MapTrigger(trigger, names.GetValueOrDefault(trigger.DriveItemId ?? Guid.Empty));
    }

    public async Task DeleteTriggerAsync(Guid scriptId, Guid triggerId, CancellationToken ct = default)
    {
        if (await FindAsync(scriptId, ct).ConfigureAwait(false) is null) return;

        await db.ScriptTriggers
            .Where(t => t.Id == triggerId && t.ScriptId == scriptId)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<ScriptRunDto> RunAsync(
        Guid scriptId,
        IReadOnlyDictionary<string, string>? input = null,
        ScriptRunTrigger triggeredBy = ScriptRunTrigger.Manual,
        CancellationToken ct = default)
    {
        var script = await FindAsync(scriptId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("That script could not be found.");

        var run = new ScriptRun
        {
            ScriptId = script.Id,
            ActorId = currentUser.RequireId(),
            TriggeredBy = triggeredBy,
            VersionNumber = script.VersionNumber,
        };

        if (!script.IsEnabled)
        {
            // Recorded, not silently skipped: "why did nothing happen" is exactly the question an
            // audit log exists to answer.
            run.Status = ScriptRunStatus.Refused;
            run.Error = "This script is disabled.";
            run.FinishedAt = DateTimeOffset.UtcNow;

            return await PersistAsync(script, run, ct).ConfigureAwait(false);
        }

        var result = await executor.ExecuteAsync(new ScriptExecutionRequest
        {
            ScriptId = script.Id,
            Name = script.Name,
            Language = script.Language,
            Code = script.Code,
            Scopes = script.Scopes,
            AllowedHosts = script.AllowedHosts,
            TimeoutSeconds = script.TimeoutSeconds,
            Input = input,
            TriggeredBy = triggeredBy,
        }, ct).ConfigureAwait(false);

        run.Status = result.Status;
        run.Output = result.Output;
        run.ResultJson = result.ResultJson;
        run.Error = result.Error;
        run.DurationMs = result.DurationMs;
        run.FinishedAt = DateTimeOffset.UtcNow;
        run.ApiCallsJson = result.ApiCalls.Count > 0 ? JsonSerializer.Serialize(result.ApiCalls) : null;

        return await PersistAsync(script, run, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ScriptRunDto>> ListRunsAsync(
        Guid? scriptId = null, int take = 50, CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        var query = db.ScriptRuns
            .AsNoTracking()
            .Include(r => r.Script)
            .Where(r => r.Script!.OwnerId == userId);

        if (scriptId is { } id) query = query.Where(r => r.ScriptId == id);

        var runs = await query
            .OrderByDescending(r => r.StartedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. runs.Select(MapRun)];
    }

    public async Task<ScriptRunDto?> GetRunAsync(Guid runId, CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        var run = await db.ScriptRuns
            .AsNoTracking()
            .Include(r => r.Script)
            .FirstOrDefaultAsync(r => r.Id == runId && r.Script!.OwnerId == userId, ct)
            .ConfigureAwait(false);

        return run is null ? null : MapRun(run);
    }

    // ─────────────────────────────── marketplace ───────────────────────────────

    public async Task<IReadOnlyList<ScriptPublicationDto>> BrowseAsync(
        string? keyword = null, string? category = null, CancellationToken ct = default)
    {
        var query = db.ScriptPublications.AsNoTracking().Where(p => p.IsListed);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(p =>
                EF.Functions.Like(p.Name, $"%{keyword}%") ||
                (p.Summary != null && EF.Functions.Like(p.Summary, $"%{keyword}%")));
        }

        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(p => p.Category == category);

        var listings = await query
            .OrderByDescending(p => p.InstallCount)
            .ThenByDescending(p => p.PublishedAt)
            .Take(200)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var authors = await users
            .GetManyAsync(listings.Select(p => p.AuthorId).Distinct(), ct)
            .ConfigureAwait(false);

        return [.. listings.Select(p => new ScriptPublicationDto(
            p.Id, p.ScriptId, p.Name, p.Summary, p.Category, p.Language, p.Scopes, p.AllowedHosts,
            p.AuthorId, authors.GetValueOrDefault(p.AuthorId)?.DisplayName ?? "Unknown",
            p.InstallCount, p.PublishedAt))];
    }

    public async Task<ScriptPublicationDto> PublishAsync(
        Guid scriptId, string? summary, string? category, CancellationToken ct = default)
    {
        var script = await FindAsync(scriptId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("That script could not be found.");

        var existing = await db.ScriptPublications
            .FirstOrDefaultAsync(p => p.ScriptId == scriptId, ct)
            .ConfigureAwait(false);

        var publication = existing ?? new ScriptPublication
        {
            ScriptId = script.Id,
            AuthorId = script.OwnerId,
        };

        // A copy of the source, not a reference: republishing must not change what already runs in
        // someone else's workspace.
        publication.Name = script.Name;
        publication.Summary = summary ?? script.Description;
        publication.Category = category;
        publication.Language = script.Language;
        publication.Code = script.Code;
        publication.Scopes = script.Scopes;
        publication.AllowedHosts = script.AllowedHosts;
        publication.UpdatedAt = DateTimeOffset.UtcNow;
        publication.IsListed = true;

        if (existing is null) db.ScriptPublications.Add(publication);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var author = await users.GetAsync(publication.AuthorId, ct).ConfigureAwait(false);

        return new ScriptPublicationDto(
            publication.Id, publication.ScriptId, publication.Name, publication.Summary,
            publication.Category, publication.Language, publication.Scopes, publication.AllowedHosts,
            publication.AuthorId, author?.DisplayName ?? "Unknown",
            publication.InstallCount, publication.PublishedAt);
    }

    public async Task UnpublishAsync(Guid publicationId, CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        var publication = await db.ScriptPublications
            .FirstOrDefaultAsync(p => p.Id == publicationId && p.AuthorId == userId, ct)
            .ConfigureAwait(false);

        if (publication is null) return;

        // Delisted rather than deleted: copies already installed keep working, and the row is still
        // there to explain where an installed script came from.
        publication.IsListed = false;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<ScriptDto> InstallAsync(Guid publicationId, CancellationToken ct = default)
    {
        var userId = currentUser.RequireId();

        var publication = await db.ScriptPublications
            .FirstOrDefaultAsync(p => p.Id == publicationId && p.IsListed, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("That listing could not be found.");

        var script = new Script
        {
            OwnerId = userId,
            Name = publication.Name,
            Description = publication.Summary,
            Language = publication.Language,
            Code = publication.Code,
            Scopes = publication.Scopes,
            AllowedHosts = publication.AllowedHosts,
            // Installed disabled on purpose. Someone else's code should not start running against
            // your files because you clicked Install; you enable it after reading it.
            IsEnabled = false,
        };

        db.Scripts.Add(script);
        publication.InstallCount++;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var owner = await users.GetAsync(userId, ct).ConfigureAwait(false);

        return Map(script, owner?.DisplayName ?? "Me", 0);
    }

    // ─────────────────────────────── trigger lookup ───────────────────────────────

    /// <summary>
    /// Event triggers that want this event.
    /// </summary>
    /// <remarks>
    /// Runs inside the request that produced the event, so it stays cheap: an indexed pull of the
    /// enabled event triggers, then wildcard matching in memory. Matching in SQL would mean a LIKE
    /// per row and would still not express <c>item.*</c> cleanly.
    /// </remarks>
    public async Task<IReadOnlyList<TriggerMatch>> MatchAsync(
        string eventName, Guid? driveItemId, CancellationToken ct = default)
    {
        var candidates = await db.ScriptTriggers
            .AsNoTracking()
            .Where(t => t.Kind == TriggerKind.Event && t.IsEnabled && t.Script!.IsEnabled)
            .Select(t => new
            {
                t.Id,
                t.ScriptId,
                t.EventName,
                t.DriveItemId,
                t.CronExpression,
                t.TimeZoneId,
                OwnerId = t.Script!.OwnerId,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return
        [
            .. candidates
                .Where(t => ScriptEvents.Matches(t.EventName, eventName))
                // A trigger scoped to one item ignores events about anything else.
                .Where(t => t.DriveItemId is null || t.DriveItemId == driveItemId)
                .Select(t => new TriggerMatch(t.Id, t.ScriptId, t.OwnerId, t.CronExpression, t.TimeZoneId)),
        ];
    }

    public async Task<IReadOnlyList<TriggerMatch>> DueAsync(
        DateTimeOffset nowUtc, CancellationToken ct = default)
    {
        var due = await db.ScriptTriggers
            .AsNoTracking()
            .Where(t => t.Kind == TriggerKind.Schedule
                        && t.IsEnabled
                        && t.Script!.IsEnabled
                        && t.NextRunAt != null
                        && t.NextRunAt <= nowUtc)
            .Select(t => new
            {
                t.Id,
                t.ScriptId,
                t.CronExpression,
                t.TimeZoneId,
                OwnerId = t.Script!.OwnerId,
            })
            .Take(50)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return [.. due.Select(t => new TriggerMatch(
            t.Id, t.ScriptId, t.OwnerId, t.CronExpression, t.TimeZoneId))];
    }

    public Task AdvanceAsync(Guid triggerId, DateTimeOffset nextUtc, CancellationToken ct = default) =>
        db.ScriptTriggers
            .Where(t => t.Id == triggerId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.NextRunAt, nextUtc)
                      .SetProperty(t => t.LastFiredAt, DateTimeOffset.UtcNow),
                ct);

    // ─────────────────────────────── helpers ───────────────────────────────

    private async Task<ScriptRunDto> PersistAsync(Script script, ScriptRun run, CancellationToken ct)
    {
        db.ScriptRuns.Add(run);

        script.LastRunAt = run.StartedAt;
        script.LastRunStatus = run.Status;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await PruneRunsAsync(script.Id, ct).ConfigureAwait(false);

        run.Script = script;

        return MapRun(run);
    }

    /// <summary>Keeps the newest runs. Audit value decays quickly and a busy trigger fills a table.</summary>
    private async Task PruneRunsAsync(Guid scriptId, CancellationToken ct)
    {
        var total = await db.ScriptRuns.CountAsync(r => r.ScriptId == scriptId, ct).ConfigureAwait(false);

        if (total <= MaxRunsKept) return;

        var cutoff = await db.ScriptRuns
            .Where(r => r.ScriptId == scriptId)
            .OrderByDescending(r => r.StartedAt)
            .Skip(MaxRunsKept)
            .Select(r => r.StartedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (cutoff == default) return;

        await db.ScriptRuns
            .Where(r => r.ScriptId == scriptId && r.StartedAt <= cutoff)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }

    private Task<Script?> FindAsync(Guid id, CancellationToken ct)
    {
        var userId = currentUser.RequireId();

        return db.Scripts.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == userId, ct);
    }

    private async Task<Dictionary<Guid, string>> ResolveItemNamesAsync(
        IEnumerable<ScriptTrigger> triggers, CancellationToken ct)
    {
        var ids = triggers.Select(t => t.DriveItemId).OfType<Guid>().Distinct().ToList();
        var names = new Dictionary<Guid, string>();

        foreach (var id in ids)
        {
            // Through IDriveService, so a trigger cannot reveal the name of an item its owner has
            // since lost access to.
            if (await drive.GetAsync(id, ct).ConfigureAwait(false) is { } item) names[id] = item.Name;
        }

        return names;
    }

    private static string? Normalise(string? hosts) =>
        string.IsNullOrWhiteSpace(hosts)
            ? null
            : string.Join(",", hosts
                .Split([',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(h => h.ToLowerInvariant())
                .Distinct());

    private static ScriptDto Map(Script s, string ownerName, int triggerCount) => new(
        s.Id, s.Name, s.Description, s.Language, s.Scopes, s.AllowedHosts, s.TimeoutSeconds,
        s.IsEnabled, s.TemplateId, s.VersionNumber, s.OwnerId, ownerName,
        s.CreatedAt, s.UpdatedAt, s.LastRunAt, s.LastRunStatus, triggerCount);

    private static ScriptTriggerDto MapTrigger(ScriptTrigger t, string? itemName) => new(
        t.Id, t.Kind, t.EventName, t.DriveItemId, itemName, t.CronExpression, t.TimeZoneId,
        t.IsEnabled, t.NextRunAt, t.LastFiredAt);

    private static ScriptRunDto MapRun(ScriptRun r) => new(
        r.Id,
        r.ScriptId,
        r.Script?.Name ?? "(deleted script)",
        r.TriggeredBy,
        r.TriggerDetail,
        r.Status,
        r.StartedAt,
        r.FinishedAt,
        r.DurationMs,
        r.Output,
        r.ResultJson,
        r.Error,
        ParseCalls(r.ApiCallsJson),
        r.VersionNumber);

    private static IReadOnlyList<ScriptApiCallDto> ParseCalls(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<ScriptApiCallDto>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
