using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AutoWork.Core.Meetings;

public enum TranscriptionMode
{
    /// <summary>No transcription. The default: nothing listens until asked to.</summary>
    Off = 0,

    /// <summary>A speech model on this machine, run as a command. Nothing leaves the computer.</summary>
    Local = 1,

    /// <summary>
    /// A Whisper-compatible HTTP endpoint. This uploads the recording, so it is never the
    /// default and the user has to choose it deliberately.
    /// </summary>
    Remote = 2,
}

public sealed class TranscriptionSettings
{
    public TranscriptionMode Mode { get; set; } = TranscriptionMode.Off;

    /// <summary>
    /// The command to run, e.g. <c>whisper-cli</c> or <c>whisper</c>. AutoWork ships no speech
    /// model — a few hundred megabytes of weights is not something to install behind someone's
    /// back — so this points at whichever one the user already has.
    /// </summary>
    public string Command { get; set; } = "";

    /// <summary>
    /// Arguments, with <c>{audio}</c> and <c>{model}</c> substituted. The default matches
    /// whisper.cpp's CLI; faster-whisper and openai-whisper need their own.
    /// </summary>
    public string Arguments { get; set; } = "-m {model} -f {audio} --output-txt --no-prints";

    public string ModelPath { get; set; } = "";

    /// <summary>Base URL of a Whisper-compatible endpoint, used only in <see cref="TranscriptionMode.Remote"/>.</summary>
    public string Endpoint { get; set; } = "";

    public string RemoteModel { get; set; } = "whisper-1";

    /// <summary>Secret-store name or <c>env:VAR</c>. Never the key itself.</summary>
    public string ApiKeyRef { get; set; } = "";

    /// <summary>ISO code, or empty to let the model decide.</summary>
    public string Language { get; set; } = "";

    public int TimeoutSeconds { get; set; } = 900;
}

public sealed record TranscriptResult(bool Success, string Text, string Error)
{
    public static TranscriptResult Failed(string error) => new(false, "", error);
    public static TranscriptResult Ok(string text) => new(true, text, "");
}

public interface ITranscriber
{
    Task<TranscriptResult> TranscribeAsync(string audioPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Speech to text.
///
/// Local by default, and off until configured. A meeting recording is among the most sensitive
/// things on a person's machine — it contains other people who did not agree to anything — so
/// uploading one is a decision the user makes explicitly, not a default they discover afterwards.
/// That is the whole reason this is pluggable rather than one hard-wired API call.
/// </summary>
public static class Transcription
{
    /// <summary>Extensions the tool will attempt. Not a guarantee the chosen model accepts them.</summary>
    public static IReadOnlyList<string> AudioExtensions { get; } =
        [".wav", ".mp3", ".m4a", ".mp4", ".ogg", ".flac", ".webm", ".aac", ".wma"];

    public static bool LooksLikeAudio(string path) =>
        AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Null when the options are usable; otherwise what is missing.</summary>
    public static string? Validate(TranscriptionSettings options) => options.Mode switch
    {
        TranscriptionMode.Off =>
            "Transcription is off. Turn it on in Settings and point it at a speech model on this machine.",

        TranscriptionMode.Local when string.IsNullOrWhiteSpace(options.Command) =>
            "No speech command is configured. Set the command AutoWork should run, such as whisper-cli.",

        TranscriptionMode.Local when options.Arguments.Contains("{model}", StringComparison.Ordinal)
                                     && string.IsNullOrWhiteSpace(options.ModelPath) =>
            "The arguments mention {model} but no model file is configured.",

        TranscriptionMode.Remote when string.IsNullOrWhiteSpace(options.Endpoint) =>
            "No transcription endpoint is configured.",

        _ => null,
    };
}

/// <summary>
/// Runs a speech model already installed on this machine and reads back what it wrote.
///
/// Deliberately a command rather than a bundled library: whisper.cpp, faster-whisper and
/// openai-whisper all exist, people already have one, and choosing for them would mean shipping
/// weights and a native runtime for a feature many will never use.
/// </summary>
public sealed class LocalTranscriber : ITranscriber
{
    private readonly TranscriptionSettings _options;

    public LocalTranscriber(TranscriptionSettings options) => _options = options;

    public async Task<TranscriptResult> TranscribeAsync(string audioPath, CancellationToken cancellationToken = default)
    {
        if (Transcription.Validate(_options) is { } problem) return TranscriptResult.Failed(problem);

        var arguments = BuildArguments(_options, audioPath);
        var workingDirectory = Path.GetDirectoryName(Path.GetFullPath(audioPath)) ?? Environment.CurrentDirectory;

        var startInfo = new ProcessStartInfo(_options.Command)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start()) return TranscriptResult.Failed($"{_options.Command} could not be started.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return TranscriptResult.Failed(
                $"\"{_options.Command}\" is not installed or not on PATH. Install a speech model, or point Settings at one you have.");
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(30, _options.TimeoutSeconds)));

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }

            return TranscriptResult.Failed($"Transcription gave up after {_options.TimeoutSeconds} seconds.");
        }

        var output = (await stdout.ConfigureAwait(false)).Trim();
        var error = (await stderr.ConfigureAwait(false)).Trim();

        if (process.ExitCode != 0)
            return TranscriptResult.Failed($"{_options.Command} exited with {process.ExitCode}. {Trim(error, 400)}");

        // Some tools print the transcript; others write it beside the audio and print progress.
        // Both are normal, so both are handled rather than one being declared correct.
        var sidecar = await ReadSidecarAsync(audioPath, cancellationToken).ConfigureAwait(false);

        var text = !string.IsNullOrWhiteSpace(sidecar) ? sidecar : output;

        return string.IsNullOrWhiteSpace(text)
            ? TranscriptResult.Failed($"{_options.Command} produced no transcript. {Trim(error, 300)}")
            : TranscriptResult.Ok(text);
    }

    /// <summary>
    /// Splits the template first, then fills the placeholders.
    ///
    /// The other order looks simpler and is wrong: a recording at <c>C:\Recordings\a b.wav</c>
    /// substituted into the string is then split on its own space, and the model is handed two
    /// arguments that are each half a path.
    /// </summary>
    internal static IReadOnlyList<string> BuildArguments(TranscriptionSettings options, string audioPath)
    {
        var arguments = Split(options.Arguments)
            .Select(part => part
                .Replace("{audio}", audioPath, StringComparison.Ordinal)
                .Replace("{model}", options.ModelPath, StringComparison.Ordinal)
                .Replace("{language}", options.Language, StringComparison.Ordinal))
            .ToList();

        // A template that never mentions the audio would otherwise transcribe nothing, silently.
        if (!options.Arguments.Contains("{audio}", StringComparison.Ordinal)) arguments.Add(audioPath);

        return arguments;
    }

    /// <summary>
    /// Splits an argument template, honouring quotes. Model paths live under "Program Files" and
    /// splitting on spaces alone turns one path into two broken arguments.
    /// </summary>
    internal static List<string> Split(string arguments)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';

        foreach (var c in arguments)
        {
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                else current.Append(c);
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0) { parts.Add(current.ToString()); current.Clear(); }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }

    private static async Task<string?> ReadSidecarAsync(string audioPath, CancellationToken cancellationToken)
    {
        foreach (var candidate in new[]
                 {
                     audioPath + ".txt",
                     Path.ChangeExtension(audioPath, ".txt"),
                     audioPath + ".srt",
                     Path.ChangeExtension(audioPath, ".srt"),
                 })
        {
            if (!File.Exists(candidate)) continue;

            var text = await File.ReadAllTextAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text)) continue;

            return candidate.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) ? StripSubtitleTiming(text) : text.Trim();
        }

        return null;
    }

    /// <summary>Turns an .srt into prose: the timings help a player, not a reader.</summary>
    internal static string StripSubtitleTiming(string srt)
    {
        var lines = srt.ReplaceLineEndings("\n").Split('\n');
        var output = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0) continue;
            if (int.TryParse(trimmed, out _)) continue;
            if (trimmed.Contains("-->", StringComparison.Ordinal)) continue;

            output.AppendLine(trimmed);
        }

        return output.ToString().Trim();
    }

    private static string Trim(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}

/// <summary>
/// A Whisper-compatible HTTP endpoint. Only reached when the user has chosen
/// <see cref="TranscriptionMode.Remote"/>, because it uploads the recording.
/// </summary>
public sealed class RemoteTranscriber : ITranscriber
{
    private readonly TranscriptionSettings _options;
    private readonly string? _apiKey;
    private readonly HttpClient _http;

    public RemoteTranscriber(TranscriptionSettings options, string? apiKey, HttpClient? http = null)
    {
        _options = options;
        _apiKey = apiKey;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(60, options.TimeoutSeconds)) };
    }

    public async Task<TranscriptResult> TranscribeAsync(string audioPath, CancellationToken cancellationToken = default)
    {
        if (Transcription.Validate(_options) is { } problem) return TranscriptResult.Failed(problem);

        var url = _options.Endpoint.TrimEnd('/') + "/audio/transcriptions";

        using var content = new MultipartFormDataContent
        {
            { new StringContent(_options.RemoteModel), "model" },
            { new StringContent("text"), "response_format" },
        };

        if (!string.IsNullOrWhiteSpace(_options.Language))
            content.Add(new StringContent(_options.Language), "language");

        await using var audio = File.OpenRead(audioPath);
        content.Add(new StreamContent(audio), "file", Path.GetFileName(audioPath));

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return TranscriptResult.Failed($"The transcription service answered {(int)response.StatusCode}. {Shorten(body)}");

            // response_format=text gives prose; some gateways return JSON regardless.
            var text = body.TrimStart().StartsWith('{') ? ExtractJsonText(body) ?? body : body;

            return string.IsNullOrWhiteSpace(text)
                ? TranscriptResult.Failed("The transcription service returned nothing.")
                : TranscriptResult.Ok(text.Trim());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return TranscriptResult.Failed($"The transcription service could not be reached: {ex.Message}");
        }
    }

    private static string? ExtractJsonText(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("text", out var text) ? text.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Shorten(string text) =>
        text.Length <= 300 ? text : text[..300] + "…";
}
