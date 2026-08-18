using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using VibeDesk.Application.Drive;
using DomainType = VibeDesk.Domain.DriveItemType;

namespace VibeDesk.Api.Grpc;

/// <summary>
/// gRPC over the same <see cref="IDriveService"/> the REST endpoints use. No logic lives here — the
/// moment it does, the three transports start behaving differently.
/// </summary>
[Authorize]
public sealed class DriveGrpcService(
    IDriveService drive,
    IDocumentContentService content) : Drive.DriveBase
{
    public override async Task<QueryReply> Query(QueryRequest request, ServerCallContext context)
    {
        var page = await drive.QueryAsync(ToQuery(request), context.CancellationToken);

        var reply = new QueryReply
        {
            TotalCount = page.TotalCount,
            HasMore = page.HasMore,
        };

        reply.Items.AddRange(page.Items.Select(ToProto));

        return reply;
    }

    public override async Task<DriveItem> Get(ItemRequest request, ServerCallContext context)
    {
        var item = await drive.GetAsync(ParseId(request.Id), context.CancellationToken);

        // NotFound rather than PermissionDenied, matching REST: a status code must not reveal that
        // an id exists.
        return item is null
            ? throw new RpcException(new Status(StatusCode.NotFound, "Item not found."))
            : ToProto(item);
    }

    public override async Task StreamChildren(
        ChildrenRequest request,
        IServerStreamWriter<DriveItem> responseStream,
        ServerCallContext context)
    {
        var parentId = string.IsNullOrWhiteSpace(request.ParentId) ? (Guid?)null : ParseId(request.ParentId);
        var skip = 0;
        const int page = 200;

        // Paged internally so a large folder never materialises in one list, which is the whole
        // reason this call streams.
        while (!context.CancellationToken.IsCancellationRequested)
        {
            var batch = await drive.QueryAsync(
                new DriveQuery { ParentId = parentId, Recursive = request.Recursive, Skip = skip, Take = page },
                context.CancellationToken);

            foreach (var item in batch.Items)
            {
                await responseStream.WriteAsync(ToProto(item), context.CancellationToken);
            }

            if (!batch.HasMore) break;
            skip += batch.Items.Count;
        }
    }

    public override async Task<DocumentContent> GetContent(ItemRequest request, ServerCallContext context)
    {
        var payload = await content.GetAsync(ParseId(request.Id), context.CancellationToken);

        return payload is null
            ? throw new RpcException(new Status(StatusCode.NotFound, "Item not found."))
            : new DocumentContent
            {
                Id = payload.Id.ToString(),
                Type = payload.Type.ToString(),
                Name = payload.Name,
                Data = payload.Data,
                Revision = payload.Revision,
                MyRole = payload.MyRole.ToString(),
            };
    }

    public override async Task<SaveContentReply> SaveContent(
        SaveContentRequest request,
        ServerCallContext context)
    {
        var result = await content.SaveAsync(
            ParseId(request.Id), request.Data, request.BaseRevision, context.CancellationToken);

        return new SaveContentReply
        {
            Accepted = result.Accepted,
            Revision = result.Revision,
            ServerData = result.ServerData ?? string.Empty,
            Reason = result.Reason ?? string.Empty,
        };
    }

    private static DriveQuery ToQuery(QueryRequest request) => new()
    {
        Keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword,
        ParentId = string.IsNullOrWhiteSpace(request.ParentId) ? null : ParseId(request.ParentId),
        // System.Enum, spelled out: Protobuf's well-known types define an `Enum` of their own.
        Types = System.Enum.TryParse<DomainType>(request.Type, ignoreCase: true, out var type) ? [type] : null,
        Skip = Math.Max(0, request.Skip),
        Take = request.Take <= 0 ? 100 : Math.Clamp(request.Take, 1, 500),
        Recursive = request.Recursive,
    };

    private static DriveItem ToProto(DriveItemDto item) => new()
    {
        Id = item.Id.ToString(),
        Type = item.Type.ToString(),
        Name = item.Name,
        ParentId = item.ParentId?.ToString() ?? string.Empty,
        OwnerName = item.OwnerName,
        SizeBytes = item.SizeBytes,
        IsStarred = item.IsStarred,
        IsShared = item.IsShared,
        MyRole = item.MyRole.ToString(),
        VersionNumber = item.VersionNumber,
        UpdatedAt = Timestamp.FromDateTimeOffset(item.UpdatedAt),
        OpenRoute = item.OpenRoute,
    };

    private static Guid ParseId(string value) => Guid.TryParse(value, out var id)
        ? id
        : throw new RpcException(new Status(StatusCode.InvalidArgument, $"'{value}' is not a valid id."));
}
