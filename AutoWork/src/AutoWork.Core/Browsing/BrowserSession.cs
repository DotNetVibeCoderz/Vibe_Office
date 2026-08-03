using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutoWork.Core.Browsing;

public sealed class BrowserSettings
{
    /// <summary>
    /// Off by default. A logged-in browser is the single most powerful thing on the machine —
    /// it is every account the person has — so it gets its own switch rather than riding in on
    /// "network access".
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Which browser to drive. Empty means "find Edge or Chrome", because both are already on
    /// most machines and downloading another one to automate is not a reasonable ask.
    /// </summary>
    public string ExecutablePath { get; set; } = "";

    /// <summary>
    /// Where the profile lives. Its own directory rather than the user's real profile: pointing
    /// automation at the browser you have open loses your tabs and can corrupt the profile, and
    /// a separate one still stays signed in between runs, which is the point.
    /// </summary>
    public string ProfileDirectory { get; set; } = "";

    /// <summary>Visible by default — a browser acting as you invisibly is the wrong default.</summary>
    public bool Headless { get; set; }

    public int Port { get; set; }

    public int TimeoutSeconds { get; set; } = 45;
}

public sealed record BrowserResult(bool Success, string Text, string Error)
{
    public static BrowserResult Failed(string error) => new(false, "", error);
    public static BrowserResult Ok(string text) => new(true, text, "");
}

/// <summary>
/// A real browser, kept signed in, driven by the agent.
///
/// This is the thing <c>web_fetch</c> cannot be. Fetching a URL gets you the page a stranger
/// sees; most of what people actually want automated is behind a login they already have. So the
/// session runs a browser that is <em>already on the machine</em> — Edge or Chrome — against a
/// profile of its own that persists, and talks to it over the DevTools protocol.
///
/// Three decisions worth stating:
///
/// <list type="bullet">
/// <item><b>No bundled browser.</b> Shipping one, or downloading a few hundred megabytes on first
/// use, is not something to do behind someone's back when a perfectly good browser is installed.</item>
/// <item><b>Its own profile, not yours.</b> Attaching to the browser you have open would fight
/// with it for the profile lock. A separate directory still remembers logins between runs.</item>
/// <item><b>Visible unless asked otherwise.</b> Something acting as you should be watchable.</item>
/// </list>
/// </summary>
public sealed class BrowserSession : IAsyncDisposable
{
    private readonly BrowserSettings _settings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private Process? _browser;
    private ClientWebSocket? _socket;
    private int _messageId;

    public BrowserSession(BrowserSettings settings) => _settings = settings;

    public bool IsOpen => _socket?.State == WebSocketState.Open;

    /// <summary>The browser AutoWork would drive, or null when none can be found.</summary>
    public static string? FindBrowser(string? configured = null)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return File.Exists(configured) ? configured : null;

        var candidates = OperatingSystem.IsWindows()
            ?
            [
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            ]
            : OperatingSystem.IsMacOS()
                ?
                [
                    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                    "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
                ]
                : new[] { "/usr/bin/google-chrome", "/usr/bin/chromium", "/usr/bin/microsoft-edge" };

        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task<BrowserResult> OpenAsync(CancellationToken cancellationToken = default)
    {
        if (IsOpen) return BrowserResult.Ok("The browser is already open.");

        var executable = FindBrowser(_settings.ExecutablePath);

        if (executable is null)
        {
            return BrowserResult.Failed(
                "No browser was found. Install Edge or Chrome, or set the browser path in Settings.");
        }

        var profile = string.IsNullOrWhiteSpace(_settings.ProfileDirectory)
            ? Path.Combine(AppPaths.Root, "browser-profile")
            : _settings.ProfileDirectory;

        Directory.CreateDirectory(profile);

        var port = _settings.Port > 0 ? _settings.Port : FreePort();

        var arguments = new List<string>
        {
            $"--remote-debugging-port={port}",
            $"--user-data-dir={profile}",
            "--no-first-run",
            "--no-default-browser-check",

            // Without this the first window can be a restored session or a promo page, and the
            // first navigation then lands in the wrong tab.
            "about:blank",
        };

        if (_settings.Headless) arguments.Insert(0, "--headless=new");

        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        try
        {
            _browser = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return BrowserResult.Failed($"{executable} could not be started: {ex.Message}");
        }

        if (_browser is null) return BrowserResult.Failed($"{executable} could not be started.");

        var endpoint = await WaitForDebuggerAsync(port, cancellationToken).ConfigureAwait(false);

        if (endpoint is null)
        {
            // A browser that exited on its own almost always means the profile is already open
            // in another window, and saying that beats "it never opened its automation port".
            var exited = _browser?.HasExited == true;

            await DisposeAsync().ConfigureAwait(false);

            return BrowserResult.Failed(exited
                ? $"The browser closed immediately. The profile in {profile} is probably open in another window."
                : "The browser started but never opened its automation port.");
        }

        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(new Uri(endpoint), cancellationToken).ConfigureAwait(false);

        await SendAsync("Page.enable", new JsonObject(), cancellationToken).ConfigureAwait(false);
        await SendAsync("Runtime.enable", new JsonObject(), cancellationToken).ConfigureAwait(false);

        return BrowserResult.Ok($"Opened {Path.GetFileNameWithoutExtension(executable)} using the profile in {profile}.");
    }

    public async Task<BrowserResult> NavigateAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!IsOpen && (await OpenAsync(cancellationToken).ConfigureAwait(false)) is { Success: false } failure)
            return failure;

        await SendAsync("Page.navigate", new JsonObject { ["url"] = url }, cancellationToken).ConfigureAwait(false);

        // Settling rather than a load event: single-page apps finish loading long before they
        // finish rendering, and a fixed pause reads the page people actually see.
        await Task.Delay(1_200, cancellationToken).ConfigureAwait(false);

        var title = await EvaluateAsync("document.title", cancellationToken).ConfigureAwait(false);
        return BrowserResult.Ok($"Opened {url} — \"{title.Text}\".");
    }

    /// <summary>
    /// The page as text, the way a reader sees it. Script and style content is dropped because
    /// it is never what was being asked for and is most of the bytes.
    /// </summary>
    public Task<BrowserResult> ReadAsync(int maxCharacters, CancellationToken cancellationToken = default) =>
        EvaluateAsync(
            $$"""
              (() => {
                const drop = document.querySelectorAll('script,style,noscript,svg');
                for (const node of drop) node.remove();
                const text = (document.body ? document.body.innerText : '') || '';
                return text.replace(/\n{3,}/g, '\n\n').slice(0, {{maxCharacters}});
              })()
              """,
            cancellationToken);

    public Task<BrowserResult> ClickAsync(string selectorOrText, CancellationToken cancellationToken = default) =>
        EvaluateAsync(
            $$"""
              (() => {
                const wanted = {{JsonSerializer.Serialize(selectorOrText)}};
                let target = null;
                try { target = document.querySelector(wanted); } catch { }
                if (!target) {
                  const clickable = [...document.querySelectorAll('a,button,[role=button],input[type=submit]')];
                  target = clickable.find(e => (e.innerText || e.value || '').trim().toLowerCase()
                                               === wanted.trim().toLowerCase())
                        ?? clickable.find(e => (e.innerText || e.value || '').toLowerCase()
                                               .includes(wanted.trim().toLowerCase()));
                }
                if (!target) return 'NOTFOUND';
                target.scrollIntoView({block:'center'});
                target.click();
                return 'CLICKED ' + (target.innerText || target.value || target.tagName).trim().slice(0, 80);
              })()
              """,
            cancellationToken);

    public Task<BrowserResult> TypeAsync(string selector, string text, CancellationToken cancellationToken = default) =>
        EvaluateAsync(
            $$"""
              (() => {
                const field = document.querySelector({{JsonSerializer.Serialize(selector)}});
                if (!field) return 'NOTFOUND';
                field.focus();
                field.value = {{JsonSerializer.Serialize(text)}};
                field.dispatchEvent(new Event('input', {bubbles:true}));
                field.dispatchEvent(new Event('change', {bubbles:true}));
                return 'TYPED';
              })()
              """,
            cancellationToken);

    /// <summary>The page's interactive parts, so the model can aim at something real.</summary>
    public Task<BrowserResult> OutlineAsync(CancellationToken cancellationToken = default) =>
        EvaluateAsync(
            """
            (() => {
              const seen = [];
              const describe = (e) => {
                const label = (e.innerText || e.value || e.placeholder || e.getAttribute('aria-label') || '').trim();
                if (!label && !e.id && !e.name) return null;
                const how = e.id ? '#' + e.id : e.name ? `[name="${e.name}"]` : '';
                return `${e.tagName.toLowerCase()}${how} ${label.slice(0, 60)}`.trim();
              };
              for (const e of document.querySelectorAll('a,button,input,select,textarea,[role=button]')) {
                const rect = e.getBoundingClientRect();
                if (rect.width === 0 && rect.height === 0) continue;
                const line = describe(e);
                if (line) seen.push(line);
                if (seen.length >= 60) break;
              }
              return seen.join('\n');
            })()
            """,
            cancellationToken);

    public async Task<BrowserResult> EvaluateAsync(string expression, CancellationToken cancellationToken = default)
    {
        if (!IsOpen) return BrowserResult.Failed("The browser is not open.");

        try
        {
            var response = await SendAsync("Runtime.evaluate", new JsonObject
            {
                ["expression"] = expression,
                ["returnByValue"] = true,
                ["awaitPromise"] = true,
            }, cancellationToken).ConfigureAwait(false);

            var result = response?["result"]?["result"];

            if (response?["result"]?["exceptionDetails"] is not null)
                return BrowserResult.Failed($"The page rejected that: {response["result"]!["exceptionDetails"]!["text"]}");

            return BrowserResult.Ok(result?["value"]?.ToString() ?? "");
        }
        catch (Exception ex) when (ex is WebSocketException or JsonException or InvalidOperationException)
        {
            return BrowserResult.Failed($"The browser connection failed: {ex.Message}");
        }
    }

    // ── Protocol ──────────────────────────────────────────────────────────────────────────

    private async Task<JsonNode?> SendAsync(string method, JsonObject parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _messageId);

        var payload = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters };
        var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());

        await _socket!.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
            .ConfigureAwait(false);

        // The protocol interleaves events with replies, so anything that is not the answer to
        // this id is skipped rather than mistaken for it.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _settings.TimeoutSeconds)));

        while (true)
        {
            var message = await ReceiveAsync(timeout.Token).ConfigureAwait(false);
            if (message is null) return null;

            var node = JsonNode.Parse(message);
            if (node?["id"]?.GetValue<int>() == id) return new JsonObject { ["result"] = node["result"]?.DeepClone() };
        }
    }

    private async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var builder = new StringBuilder();

        while (true)
        {
            var received = await _socket!.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);

            if (received.MessageType == WebSocketMessageType.Close) return null;

            builder.Append(Encoding.UTF8.GetString(buffer, 0, received.Count));
            if (received.EndOfMessage) return builder.ToString();
        }
    }

    /// <summary>
    /// Finds the socket for a <em>page</em>, not for the browser.
    ///
    /// <c>/json/version</c> gives a browser-level endpoint, which is the obvious thing to connect
    /// to and the wrong one: it speaks <c>Target</c> and <c>Browser</c> but not <c>Page</c> or
    /// <c>Runtime</c>, so navigation and evaluation quietly do nothing. The page targets are in
    /// <c>/json/list</c>, and connecting straight to one avoids threading a session id through
    /// every message.
    /// </summary>
    private async Task<string?> WaitForDebuggerAsync(int port, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // No point waiting out the deadline for a process that has already gone.
            if (_browser?.HasExited == true) return null;

            try
            {
                var json = await _http.GetStringAsync($"http://127.0.0.1:{port}/json/list", cancellationToken)
                    .ConfigureAwait(false);

                if (JsonNode.Parse(json) is JsonArray targets)
                {
                    var page = targets.FirstOrDefault(t =>
                        t?["type"]?.ToString() == "page" && !string.IsNullOrWhiteSpace(t["webSocketDebuggerUrl"]?.ToString()));

                    if (page?["webSocketDebuggerUrl"]?.ToString() is { Length: > 0 } socket) return socket;
                }

                // The browser is up but has no page yet — ask it for one.
                using var created = await _http.PutAsync($"http://127.0.0.1:{port}/json/new?about:blank",
                    content: null, cancellationToken).ConfigureAwait(false);

                if (created.IsSuccessStatusCode)
                {
                    var body = await created.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    var socket = JsonNode.Parse(body)?["webSocketDebuggerUrl"]?.ToString();

                    if (!string.IsNullOrWhiteSpace(socket)) return socket;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                // Still starting.
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket is not null)
        {
            try
            {
                if (_socket.State == WebSocketState.Open)
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None)
                        .ConfigureAwait(false);
            }
            catch (WebSocketException) { }

            _socket.Dispose();
            _socket = null;
        }

        if (_browser is not null)
        {
            try
            {
                if (!_browser.HasExited) _browser.Kill(entireProcessTree: true);

                // Waited for, not just asked for. A browser holds a lock on its profile
                // directory until it has really gone, so a session opened straight after this
                // one — same profile — would find the directory in use and fail to start.
                using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _browser.WaitForExitAsync(grace.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException
                                       or System.ComponentModel.Win32Exception
                                       or OperationCanceledException) { }

            _browser.Dispose();
            _browser = null;
        }

        _http.Dispose();
    }
}
