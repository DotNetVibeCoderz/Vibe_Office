using System.ComponentModel;
using AutoWork.Core.Agents;
using AutoWork.Core.Meetings;
using AutoWork.Core.Security;
using Microsoft.Extensions.AI;

namespace AutoWork.Tools;

/// <summary>
/// Meeting recordings, turned into something you can act on.
///
/// The pipeline is transcribe → understand → write it down, and only the first of those is a
/// tool. Pulling out the decisions and who owes what is reasoning, which the agent already does
/// better than a fixed prompt buried in a tool would; and writing the result is
/// <c>doc_create_word</c> or <c>knowledge_save</c>, which already exist. So this adds the one
/// piece that was genuinely missing.
///
/// It advertises nothing when transcription is switched off — the same rule the shell tools
/// follow. A tool that exists only to refuse wastes tokens and makes the model keep trying.
/// </summary>
public sealed class MeetingTools : ToolSetBase, IToolProvider
{
    private readonly ITranscriber _transcriber;

    public MeetingTools(ToolContext context, ITranscriber? transcriber = null) : base(context)
    {
        _transcriber = transcriber ?? Build(context);
    }

    protected override AgentOrgan Organ => AgentOrgan.Eyes;

    public string Name => "Meetings";

    private static ITranscriber Build(ToolContext context) => context.Transcription.Mode switch
    {
        TranscriptionMode.Remote => new RemoteTranscriber(
            context.Transcription,
            context.Secrets?.Resolve(context.Transcription.ApiKeyRef)),

        _ => new LocalTranscriber(context.Transcription),
    };

    public IEnumerable<ToolDescriptor> GetTools(ToolContext context)
    {
        // Nothing is offered until a speech model is configured.
        if (context.Transcription.Mode == TranscriptionMode.Off) yield break;

        // Carries this instance's transcriber rather than building a fresh one: the provider is
        // also the thing that was handed a transcriber, and dropping it here would silently make
        // an injected one unreachable.
        var tools = new MeetingTools(context, _transcriber);

        yield return new ToolDescriptor
        {
            Function = AIFunctionFactory.Create(tools.TranscribeAsync, "meeting_transcribe",
                """
                Turn an audio or video recording into text, and save the transcript beside it.
                Then read the transcript and write up what matters: decisions taken, action items
                with who owns them, and anything left open. Use doc_create_word or doc_create_pdf
                for a write-up, or knowledge_save to remember it.
                """),
            Organ = AgentOrgan.Eyes,

            // It reads a file and, in remote mode, uploads it. Neither is a quiet act.
            Risk = context.Transcription.Mode == TranscriptionMode.Remote ? ToolRisk.System : ToolRisk.Write,
            Category = "Meetings",
            ApprovalKind = context.Transcription.Mode == TranscriptionMode.Remote
                ? ApprovalKind.NetworkAccess
                : ApprovalKind.WriteFiles,
        };
    }

    [Description("Transcribe a recording.")]
    private Task<string> TranscribeAsync(
        [Description("Audio or video file to transcribe.")] string path,
        [Description("Where to save the transcript. Defaults to the recording's name with a .txt extension.")] string? destination = null,
        [Description("Overwrite an existing transcript.")] bool overwrite = false)
    {
        var source = Locate(path);
        var remote = Context.Transcription.Mode == TranscriptionMode.Remote;

        var summary = remote
            ? $"Upload {PathGuard.Describe(source)} to {Context.Transcription.Endpoint} for transcription"
            : $"Transcribe {PathGuard.Describe(source)}";

        return GuardedAsync("meeting.transcribe", summary, async () =>
        {
            if (Transcription.Validate(Context.Transcription) is { } problem) return Refused(problem);

            var canonical = Guard.EnsureReadable(source);
            if (!File.Exists(canonical)) return Failed($"{PathGuard.Describe(canonical)} does not exist.");

            if (!Transcription.LooksLikeAudio(canonical))
            {
                return Refused(
                    $"{Path.GetExtension(canonical)} is not a recording. Expected one of: " +
                    $"{string.Join(", ", Transcription.AudioExtensions)}.");
            }

            var target = string.IsNullOrWhiteSpace(destination)
                ? Path.ChangeExtension(canonical, ".transcript.txt")
                : Locate(destination);

            var canonicalTarget = Guard.EnsureWritable(target);

            if (File.Exists(canonicalTarget) && !overwrite)
                return Refused($"{PathGuard.Describe(canonicalTarget)} already exists. Call again with overwrite=true to replace it.");

            var result = await _transcriber.TranscribeAsync(canonical).ConfigureAwait(false);
            if (!result.Success) return Failed(result.Error);

            Directory.CreateDirectory(Path.GetDirectoryName(canonicalTarget)!);
            await File.WriteAllTextAsync(canonicalTarget, result.Text).ConfigureAwait(false);

            var words = result.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            return Ok(
                $"Transcribed {PathGuard.Describe(canonical)} — {words:N0} words, saved to " +
                $"{PathGuard.Describe(canonicalTarget)}.\n\n{CapTranscript(result.Text)}");
        },
        [source], remote ? ApprovalKind.NetworkAccess : ApprovalKind.WriteFiles,
        remote
            ? "The recording will be uploaded to the transcription service you configured."
            : "Runs the speech model on this computer. Nothing is uploaded.");
    }

    /// <summary>
    /// The transcript goes back to the model so it can act on it in the same step, but a
    /// two-hour meeting is a lot of tokens — so the tail is cut here. Unlike the base helper,
    /// this one can say where the rest is, because there is always a saved file.
    /// </summary>
    private static string CapTranscript(string text, int max = 6_000) =>
        text.Length <= max
            ? text
            : text[..max] + $"\n\n… transcript continues ({text.Length - max:N0} more characters). Read the saved file for the rest.";
}
