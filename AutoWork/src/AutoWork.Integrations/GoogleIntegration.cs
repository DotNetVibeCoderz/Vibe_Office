using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using AutoWork.Core.Agents;
using AutoWork.Core.Security;
using Microsoft.Extensions.AI;

namespace AutoWork.Integrations;

/// <summary>
/// Google Drive and Gmail.
///
/// Both live in one connector because they share an OAuth client and a refresh token — asking
/// the user to complete the same consent flow twice would be pointless friction.
///
/// AutoWork is a desktop app, so it cannot keep a client secret secret, and it does not try to.
/// The user creates their own OAuth client in Google Cloud Console, completes the consent flow
/// once, and pastes the resulting refresh token. That keeps the credential scoped to their own
/// project rather than to a shared one, which is the right trade for a tool that reads mail.
/// Access tokens are exchanged on demand and held in memory only.
/// </summary>
public sealed class GoogleIntegration : RestConnector
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string DriveApi = "https://www.googleapis.com/drive/v3";
    private const string GmailApi = "https://gmail.googleapis.com/gmail/v1/users/me";

    private readonly Lock _gate = new();
    private string? _accessToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public override string Id => "google";
    public override string DisplayName => "Google Drive and Gmail";
    public override string Description => "Search and download Drive files, and search and read Gmail messages.";
    public override string DocsUrl => "https://console.cloud.google.com/apis/credentials";

    public override IReadOnlyList<IntegrationField> Fields =>
    [
        new("clientId", "OAuth client id", "From a Desktop app OAuth client in Google Cloud Console."),
        new("clientSecret", "OAuth client secret", "From the same OAuth client.", Secret: true),
        new("refreshToken", "Refresh token",
            "Obtained once by completing the consent flow with the drive.readonly and gmail.readonly scopes. See docs/en/integrations.md for the exact steps.",
            Secret: true),
    ];

    public override async Task<IntegrationStatus> TestAsync(
        IntegrationCredentials credentials, CancellationToken cancellationToken = default)
    {
        try
        {
            var token = await AccessTokenAsync(credentials, cancellationToken).ConfigureAwait(false);
            var about = await GetAsync($"{DriveApi}/about?fields=user", Bearer(token), cancellationToken)
                .ConfigureAwait(false);

            var email = about?["user"]?["emailAddress"]?.GetValue<string>();
            return new IntegrationStatus(true, $"Connected as {email}.", email);
        }
        catch (Exception ex) when (ex is IntegrationException or InvalidOperationException or HttpRequestException)
        {
            return new IntegrationStatus(false, ex.Message);
        }
    }

    /// <summary>
    /// Exchanges the refresh token for an access token, reusing it until shortly before it
    /// expires. The 60-second margin avoids a token expiring mid-request.
    /// </summary>
    private async Task<string> AccessTokenAsync(IntegrationCredentials credentials, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _expiresAt)
                return _accessToken;
        }

        var response = await PostFormAsync(TokenEndpoint,
        [
            new("client_id", credentials.Require("clientId")),
            new("client_secret", credentials.Require("clientSecret")),
            new("refresh_token", credentials.Require("refreshToken")),
            new("grant_type", "refresh_token"),
        ], cancellationToken: cancellationToken).ConfigureAwait(false);

        var token = response?["access_token"]?.GetValue<string>()
            ?? throw new IntegrationException("Google did not return an access token. The refresh token may have been revoked.");

        var lifetime = response?["expires_in"]?.GetValue<int>() ?? 3600;

        lock (_gate)
        {
            _accessToken = token;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(lifetime - 60);
        }

        return token;
    }

    public override IEnumerable<ToolDescriptor> GetTools(ToolContext context, IntegrationCredentials credentials)
    {
        if (credentials.Any("refreshToken") is null) yield break;

        // ── Drive ─────────────────────────────────────────────────────────────────────────

        yield return Tool(AIFunctionFactory.Create(
            ([Description("Search text matched against file names and contents.")] string query,
             [Description("Maximum results.")] int limit = 15) =>
                SafelyAsync(async () =>
                {
                    var token = await AccessTokenAsync(credentials, default).ConfigureAwait(false);

                    var escaped = query.Replace("'", "\\'");
                    var url = $"{DriveApi}/files?q={Uri.EscapeDataString($"fullText contains '{escaped}' and trashed = false")}" +
                              $"&pageSize={Math.Clamp(limit, 1, 50)}&fields=files(id,name,mimeType,size,modifiedTime)";

                    var result = await GetAsync(url, Bearer(token)).ConfigureAwait(false);

                    if (result?["files"] is not JsonArray files || files.Count == 0)
                        return $"No Drive files match \"{query}\".";

                    var lines = files.Select(f =>
                        $"{f?["name"]}  [{ShortMime(f?["mimeType"]?.GetValue<string>())}]  " +
                        $"modified {f?["modifiedTime"]?.GetValue<string>()?[..10]}  id={f?["id"]}");

                    return string.Join('\n', lines);
                }),
            "drive_search", "Search Google Drive."));

        yield return Tool(AIFunctionFactory.Create(
            ([Description("File id from drive_search.")] string fileId,
             [Description("Where to save it on this computer.")] string destination) =>
                SafelyAsync(async () =>
                {
                    var token = await AccessTokenAsync(credentials, default).ConfigureAwait(false);

                    // A download is a write to the local disk, so it must clear the sandbox
                    // exactly like any other file operation.
                    var target = context.Guard.EnsureWritable(
                        Path.IsPathRooted(destination)
                            ? destination
                            : Path.Combine(context.WorkingDirectory, destination));

                    var metadata = await GetAsync($"{DriveApi}/files/{fileId}?fields=name,mimeType",
                        Bearer(token)).ConfigureAwait(false);

                    var mime = metadata?["mimeType"]?.GetValue<string>() ?? "";

                    // Google-native formats cannot be downloaded directly; they are exported.
                    var url = mime.StartsWith("application/vnd.google-apps.", StringComparison.Ordinal)
                        ? $"{DriveApi}/files/{fileId}/export?mimeType={Uri.EscapeDataString(ExportFormat(mime))}"
                        : $"{DriveApi}/files/{fileId}?alt=media";

                    var bytes = await DownloadAsync(url, token).ConfigureAwait(false);

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await File.WriteAllBytesAsync(target, bytes).ConfigureAwait(false);

                    return $"Downloaded {metadata?["name"]} ({bytes.Length:N0} bytes) to {PathGuard.Describe(target)}.";
                }),
            "drive_download", "Download a Drive file to this computer."),
            ToolRisk.Write);

        // ── Gmail ─────────────────────────────────────────────────────────────────────────

        yield return Tool(AIFunctionFactory.Create(
            ([Description("Gmail search, e.g. \"from:billing@vendor.com has:attachment newer_than:30d\".")] string query,
             [Description("Maximum results.")] int limit = 10) =>
                SafelyAsync(async () =>
                {
                    var token = await AccessTokenAsync(credentials, default).ConfigureAwait(false);

                    var list = await GetAsync(
                        $"{GmailApi}/messages?q={Uri.EscapeDataString(query)}&maxResults={Math.Clamp(limit, 1, 50)}",
                        Bearer(token)).ConfigureAwait(false);

                    if (list?["messages"] is not JsonArray messages || messages.Count == 0)
                        return $"No messages match \"{query}\".";

                    var builder = new StringBuilder($"{messages.Count} message(s):\n");

                    foreach (var message in messages)
                    {
                        var id = message?["id"]?.GetValue<string>();
                        if (id is null) continue;

                        var detail = await GetAsync(
                            $"{GmailApi}/messages/{id}?format=metadata&metadataHeaders=From&metadataHeaders=Subject&metadataHeaders=Date",
                            Bearer(token)).ConfigureAwait(false);

                        builder.AppendLine(
                            $"{Header(detail, "Date")}  {Header(detail, "From")}  —  {Header(detail, "Subject")}  id={id}");
                    }

                    return builder.ToString();
                }),
            "gmail_search", "Search Gmail messages."));

        yield return Tool(AIFunctionFactory.Create(
            ([Description("Message id from gmail_search.")] string messageId) =>
                SafelyAsync(async () =>
                {
                    var token = await AccessTokenAsync(credentials, default).ConfigureAwait(false);
                    var message = await GetAsync($"{GmailApi}/messages/{messageId}?format=full", Bearer(token))
                        .ConfigureAwait(false);

                    var body = ExtractBody(message?["payload"]);

                    return $"""
                            From: {Header(message, "From")}
                            To: {Header(message, "To")}
                            Date: {Header(message, "Date")}
                            Subject: {Header(message, "Subject")}

                            {Trim(body, 10_000)}
                            """;
                }),
            "gmail_read", "Read the full text of a Gmail message."));
    }

    private static async Task<byte[]> DownloadAsync(string url, string token)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new IntegrationException($"Drive returned {(int)response.StatusCode} for the download.");

        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    private static string ExportFormat(string googleMime) => googleMime switch
    {
        "application/vnd.google-apps.document" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.google-apps.spreadsheet" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.google-apps.presentation" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        _ => "application/pdf",
    };

    private static string ShortMime(string? mime) => mime switch
    {
        null => "?",
        "application/vnd.google-apps.folder" => "folder",
        "application/vnd.google-apps.document" => "Google Doc",
        "application/vnd.google-apps.spreadsheet" => "Google Sheet",
        "application/vnd.google-apps.presentation" => "Google Slides",
        _ => mime.Split('/').Last(),
    };

    private static string Header(JsonNode? message, string name)
    {
        if (message?["payload"]?["headers"] is not JsonArray headers) return "";

        foreach (var header in headers)
        {
            if (string.Equals(header?["name"]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase))
                return header?["value"]?.GetValue<string>() ?? "";
        }

        return "";
    }

    /// <summary>
    /// Walks the MIME tree for the first text/plain part, falling back to stripped HTML.
    /// Gmail nests parts arbitrarily deep for multipart/alternative inside multipart/mixed.
    /// </summary>
    private static string ExtractBody(JsonNode? payload)
    {
        if (payload is null) return "";

        var mime = payload["mimeType"]?.GetValue<string>() ?? "";

        if (mime == "text/plain")
            return DecodeBase64Url(payload["body"]?["data"]?.GetValue<string>());

        if (payload["parts"] is JsonArray parts)
        {
            foreach (var part in parts)
            {
                var text = ExtractBody(part);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }

        if (mime == "text/html")
        {
            var html = DecodeBase64Url(payload["body"]?["data"]?.GetValue<string>());
            return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        }

        return "";
    }

    private static string DecodeBase64Url(string? data)
    {
        if (string.IsNullOrEmpty(data)) return "";

        var normalized = data.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');

        try { return Encoding.UTF8.GetString(Convert.FromBase64String(normalized)); }
        catch (FormatException) { return ""; }
    }
}
