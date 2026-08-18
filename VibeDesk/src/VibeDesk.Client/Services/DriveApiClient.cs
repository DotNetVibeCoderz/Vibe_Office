using System.Net.Http.Headers;
using System.Net.Http.Json;
using VibeDesk.Application.Abstractions;
using VibeDesk.Application.Documents;
using VibeDesk.Application.Drive;
using VibeDesk.Domain;

namespace VibeDesk.Client.Services;

/// <summary>Drive over HTTP. One class per interface would be five near-identical files.</summary>
public sealed class DriveApiClient(HttpClient http) : ApiClientBase(http), IDriveService
{
    public Task<DriveItemDto?> GetAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<DriveItemDto>($"/api/drive/{id}", ct);

    public async Task<PagedResult<DriveItemDto>> QueryAsync(DriveQuery query, CancellationToken ct = default) =>
        await GetAsync<PagedResult<DriveItemDto>>("/api/drive" + Query(
            ("parentId", query.ParentId),
            ("keyword", query.Keyword),
            ("type", query.Types is { Length: > 0 } t ? t[0] : null),
            ("starred", query.StarredOnly ? true : null),
            ("trashed", query.TrashedOnly ? true : null),
            ("sharedWithMe", query.SharedWithMeOnly ? true : null),
            ("sortBy", query.SortBy),
            ("descending", query.Descending),
            ("skip", query.Skip),
            ("take", query.Take),
            ("recursive", query.Recursive ? true : null)), ct).ConfigureAwait(false)
        ?? PagedResult<DriveItemDto>.Empty;

    public async Task<IReadOnlyList<BreadcrumbDto>> GetBreadcrumbsAsync(Guid id, CancellationToken ct = default) =>
        await GetAsync<List<BreadcrumbDto>>($"/api/drive/{id}/breadcrumbs", ct).ConfigureAwait(false) ?? [];

    public async Task<DriveItemDto> CreateFolderAsync(string name, Guid? parentId, CancellationToken ct = default) =>
        await SendAsync<DriveItemDto>(HttpMethod.Post, "/api/drive/folders", new { name, parentId }, ct)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException("The server did not return the created folder.");

    public async Task<DriveItemDto> CreateDocumentAsync(
        DriveItemType type,
        string name,
        Guid? parentId,
        string? initialData = null,
        CancellationToken ct = default) =>
        await SendAsync<DriveItemDto>(
            HttpMethod.Post, "/api/drive/documents", new { type, name, parentId, initialData }, ct)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException("The server did not return the created document.");

    public async Task<DriveItemDto> UploadAsync(
        string fileName,
        string contentType,
        Stream content,
        Guid? parentId,
        CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        using var file = new StreamContent(content);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(file, "file", fileName);

        var response = await Http.PostAsync("/api/drive/upload" + Query(("parentId", parentId)), form, ct)
            .ConfigureAwait(false);

        await ThrowIfFailedAsync(response, ct).ConfigureAwait(false);

        return await response.Content.ReadFromJsonAsync<DriveItemDto>(ApiJson.Options, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The server did not return the uploaded item.");
    }

    public async Task<Stream?> OpenFileAsync(Guid id, CancellationToken ct = default)
    {
        var response = await Http.GetAsync(
            $"/api/drive/{id}/download", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;

        await ThrowIfFailedAsync(response, ct).ConfigureAwait(false);

        return await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
    }

    public async Task<string?> GetDownloadUrlAsync(
        Guid id, TimeSpan? validFor = null, CancellationToken ct = default) =>
        (await GetAsync<UrlResponse>(
            $"/api/drive/{id}/download-url" + Query(("minutes", validFor?.TotalMinutes is { } m ? (int)m : null)), ct)
            .ConfigureAwait(false))?.Url;

    public async Task<DriveItemDto> RenameAsync(Guid id, string newName, CancellationToken ct = default) =>
        await SendAsync<DriveItemDto>(HttpMethod.Patch, $"/api/drive/{id}/name", new { name = newName }, ct)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException("The server did not return the renamed item.");

    public async Task<DriveItemDto> MoveAsync(Guid id, Guid? newParentId, CancellationToken ct = default) =>
        await SendAsync<DriveItemDto>(HttpMethod.Patch, $"/api/drive/{id}/parent", new { parentId = newParentId }, ct)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException("The server did not return the moved item.");

    public async Task<DriveItemDto> CopyAsync(
        Guid id, Guid? targetParentId, string? newName = null, CancellationToken ct = default) =>
        await SendAsync<DriveItemDto>(HttpMethod.Post, $"/api/drive/{id}/copy", new { targetParentId, newName }, ct)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException("The server did not return the copy.");

    public Task SetStarredAsync(Guid id, bool starred, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Patch, $"/api/drive/{id}/starred", new { starred }, ct);

    public Task TrashAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"/api/drive/{id}", null, ct);

    public Task RestoreAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"/api/drive/{id}/restore", null, ct);

    public Task DeleteForeverAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"/api/drive/{id}?permanent=true", null, ct);

    public async Task<int> EmptyTrashAsync(CancellationToken ct = default) =>
        (await SendAsync<CountResponse>(HttpMethod.Post, "/api/drive/trash/empty", null, ct)
            .ConfigureAwait(false))?.Deleted ?? 0;

    public Task<StorageUsageDto> GetUsageAsync(CancellationToken ct = default) =>
        GetRequiredAsync<StorageUsageDto>("/api/drive/usage", ct);

    public Task TouchAsync(Guid id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"/api/drive/{id}/touch", null, ct);

    private sealed record UrlResponse(string? Url);
    private sealed record CountResponse(int Deleted);
}

public sealed class DocumentContentApiClient(HttpClient http) : ApiClientBase(http), IDocumentContentService
{
    public Task<DriveItemContentDto?> GetAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<DriveItemContentDto>($"/api/items/{id}/content", ct);

    public async Task<SaveContentResult> SaveAsync(
        Guid id, string data, long baseRevision, CancellationToken ct = default) =>
        await SendAsync<SaveContentResult>(
            HttpMethod.Put, $"/api/items/{id}/content", new { data, baseRevision }, ct).ConfigureAwait(false)
        ?? throw new InvalidOperationException("The server did not return a save result.");

    public async Task<T?> GetTypedAsync<T>(Guid id, CancellationToken ct = default) where T : new()
    {
        var payload = await GetAsync(id, ct).ConfigureAwait(false);

        return payload is null ? default : ContentJson.Deserialize<T>(payload.Data);
    }

    public async Task<long> SaveTypedAsync<T>(Guid id, T model, CancellationToken ct = default)
    {
        // -1 skips the conflict check, matching the server-side implementation's contract.
        var result = await SaveAsync(id, ContentJson.Serialize(model), -1, ct).ConfigureAwait(false);

        return result.Revision;
    }
}

public sealed class VersionApiClient(HttpClient http) : ApiClientBase(http), IVersionService
{
    public async Task<IReadOnlyList<VersionDto>> ListAsync(Guid itemId, CancellationToken ct = default) =>
        await GetAsync<List<VersionDto>>($"/api/items/{itemId}/versions", ct).ConfigureAwait(false) ?? [];

    public async Task<VersionDto> SnapshotAsync(
        Guid itemId, string? label = null, bool isAuto = false, CancellationToken ct = default) =>
        await SendAsync<VersionDto>(HttpMethod.Post, $"/api/items/{itemId}/versions", new { label }, ct)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException("The server did not return the snapshot.");

    public Task RestoreAsync(Guid itemId, Guid versionId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"/api/items/{itemId}/versions/{versionId}/restore", null, ct);

    public async Task<string?> GetDataAsync(Guid itemId, Guid versionId, CancellationToken ct = default) =>
        (await GetAsync<DataResponse>($"/api/items/{itemId}/versions/{versionId}", ct).ConfigureAwait(false))?.Data;

    public Task DeleteAsync(Guid itemId, Guid versionId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"/api/items/{itemId}/versions/{versionId}", null, ct);

    private sealed record DataResponse(string? Data);
}

public sealed class PermissionApiClient(HttpClient http) : ApiClientBase(http), IPermissionService
{
    /// <summary>
    /// Resolved from the item itself: the API deliberately has no "what is my role on X" route, and
    /// adding one would be a second place for the permission rules to drift.
    /// </summary>
    public async Task<PermissionRole> ResolveRoleAsync(Guid itemId, Guid? userId, CancellationToken ct = default) =>
        (await GetAsync<DriveItemDto>($"/api/drive/{itemId}", ct).ConfigureAwait(false))?.MyRole
        ?? PermissionRole.None;

    public async Task RequireAsync(Guid itemId, PermissionRole minimum, CancellationToken ct = default)
    {
        var role = await ResolveRoleAsync(itemId, null, ct).ConfigureAwait(false);

        // NotFound rather than Forbidden when there is no access at all, matching the server.
        if (role == PermissionRole.None) throw new NotFoundException("That item could not be found.");
        if (role < minimum) throw new ForbiddenException($"This action requires {minimum} access.");
    }

    public async Task<IReadOnlyList<PermissionDto>> ListAsync(Guid itemId, CancellationToken ct = default) =>
        await GetAsync<List<PermissionDto>>($"/api/items/{itemId}/permissions", ct).ConfigureAwait(false) ?? [];

    public async Task<PermissionDto> ShareAsync(
        Guid itemId,
        string email,
        PermissionRole role,
        DateTimeOffset? expiresAt = null,
        CancellationToken ct = default) =>
        await SendAsync<PermissionDto>(
            HttpMethod.Post, $"/api/items/{itemId}/permissions", new { email, role, expiresAt }, ct)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException("The server did not return the grant.");

    public Task RevokeAsync(Guid itemId, Guid permissionId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"/api/items/{itemId}/permissions/{permissionId}", null, ct);

    public Task SetLinkSharingAsync(
        Guid itemId, ShareScope scope, PermissionRole linkRole, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"/api/items/{itemId}/link-sharing", new { scope, linkRole }, ct);

    public Task<DriveItemDto?> ResolveShareTokenAsync(string token, CancellationToken ct = default) =>
        GetAsync<DriveItemDto>($"/api/share/{Uri.EscapeDataString(token)}", ct);

    public Task TransferOwnershipAsync(Guid itemId, Guid newOwnerId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"/api/items/{itemId}/transfer-ownership", new { newOwnerId }, ct);
}

public sealed class CommentApiClient(HttpClient http) : ApiClientBase(http), ICommentService
{
    public async Task<IReadOnlyList<CommentDto>> ListAsync(
        Guid itemId, bool includeResolved = false, CancellationToken ct = default) =>
        await GetAsync<List<CommentDto>>(
            $"/api/items/{itemId}/comments" + Query(("includeResolved", includeResolved)), ct)
            .ConfigureAwait(false) ?? [];

    public async Task<CommentDto> AddAsync(
        Guid itemId,
        string body,
        string? anchor,
        CommentKind kind = CommentKind.Comment,
        string? suggestedText = null,
        string? originalText = null,
        Guid? parentCommentId = null,
        CancellationToken ct = default) =>
        await SendAsync<CommentDto>(
            HttpMethod.Post,
            $"/api/items/{itemId}/comments",
            new { body, anchor, kind, suggestedText, originalText, parentCommentId },
            ct).ConfigureAwait(false)
        ?? throw new InvalidOperationException("The server did not return the comment.");

    public async Task<CommentDto> EditAsync(Guid commentId, string body, CancellationToken ct = default) =>
        await SendAsync<CommentDto>(HttpMethod.Patch, $"/api/comments/{commentId}", new { body }, ct)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException("The server did not return the comment.");

    public Task ResolveAsync(Guid commentId, bool resolved, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Patch, $"/api/comments/{commentId}/resolved", new { resolved }, ct);

    public async Task<long> AcceptSuggestionAsync(Guid commentId, CancellationToken ct = default) =>
        (await SendAsync<RevisionResponse>(HttpMethod.Post, $"/api/comments/{commentId}/accept", null, ct)
            .ConfigureAwait(false))?.Revision ?? 0;

    public Task RejectSuggestionAsync(Guid commentId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, $"/api/comments/{commentId}/reject", null, ct);

    public Task DeleteAsync(Guid commentId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"/api/comments/{commentId}", null, ct);

    public async Task<int> CountOpenAsync(Guid itemId, CancellationToken ct = default) =>
        (await GetAsync<OpenResponse>($"/api/items/{itemId}/comments/count", ct).ConfigureAwait(false))?.Open ?? 0;

    private sealed record RevisionResponse(long Revision);
    private sealed record OpenResponse(int Open);
}
