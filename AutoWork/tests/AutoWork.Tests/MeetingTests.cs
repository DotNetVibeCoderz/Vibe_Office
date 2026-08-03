using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Logging;
using AutoWork.Core.Meetings;
using AutoWork.Core.Security;
using AutoWork.Tools;
using Microsoft.Extensions.AI;

namespace AutoWork.Tests;

/// <summary>
/// Transcription, without a speech model.
///
/// AutoWork does not ship one and this machine has none, so the local path is exercised against
/// a stub command that behaves the way whisper.cpp does — which is enough to settle the parts
/// that are actually AutoWork's: how the command line is built, how the output is found, and
/// what happens when it is not there.
/// </summary>
public sealed class TranscriptionTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "autowork-meeting", Guid.NewGuid().ToString("n")[..8]);

    private readonly string _workspace;

    public TranscriptionTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    // ── Argument building ─────────────────────────────────────────────────────────────────

    [Fact]
    public void The_audio_and_model_placeholders_are_substituted()
    {
        var options = new TranscriptionSettings
        {
            Mode = TranscriptionMode.Local,
            Command = "whisper-cli",
            Arguments = "-m {model} -f {audio} --output-txt",
            ModelPath = "/models/ggml-base.bin",
        };

        Assert.Equal(
            ["-m", "/models/ggml-base.bin", "-f", "/tmp/meeting.wav", "--output-txt"],
            LocalTranscriber.BuildArguments(options, "/tmp/meeting.wav"));
    }

    /// <summary>
    /// Model files live under "Program Files". Splitting on spaces alone turns one path into two
    /// broken arguments and the command fails with something unhelpful.
    /// </summary>
    [Fact]
    public void A_quoted_path_with_spaces_stays_one_argument()
    {
        var options = new TranscriptionSettings
        {
            Mode = TranscriptionMode.Local,
            Command = "whisper",
            Arguments = "--model \"C:\\Program Files\\Whisper\\base.bin\" --file {audio}",
        };

        var arguments = LocalTranscriber.BuildArguments(options, "C:\\Recordings\\a b.wav");

        Assert.Equal(["--model", "C:\\Program Files\\Whisper\\base.bin", "--file", "C:\\Recordings\\a b.wav"], arguments);
    }

    /// <summary>A template that never mentions the audio would otherwise transcribe nothing.</summary>
    [Fact]
    public void A_template_that_forgets_the_audio_gets_it_appended()
    {
        var options = new TranscriptionSettings
        {
            Mode = TranscriptionMode.Local,
            Command = "whisper",
            Arguments = "--language en",
        };

        Assert.Equal(["--language", "en", "/tmp/x.wav"], LocalTranscriber.BuildArguments(options, "/tmp/x.wav"));
    }

    [Fact]
    public void Subtitle_timings_are_stripped_so_the_transcript_reads_as_prose()
    {
        const string srt = """
                           1
                           00:00:00,000 --> 00:00:03,120
                           Right, shall we start.

                           2
                           00:00:03,120 --> 00:00:06,000
                           Ada is taking the migration.
                           """;

        Assert.Equal("Right, shall we start.\nAda is taking the migration.",
            LocalTranscriber.StripSubtitleTiming(srt).ReplaceLineEndings("\n"));
    }

    // ── Configuration ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Transcription_is_off_until_it_is_configured()
    {
        Assert.NotNull(Transcription.Validate(new TranscriptionSettings()));

        Assert.NotNull(Transcription.Validate(new TranscriptionSettings
        {
            Mode = TranscriptionMode.Local,
            Command = "",
        }));

        Assert.NotNull(Transcription.Validate(new TranscriptionSettings
        {
            Mode = TranscriptionMode.Local,
            Command = "whisper",
            Arguments = "-m {model} -f {audio}",
            ModelPath = "",
        }));

        Assert.Null(Transcription.Validate(new TranscriptionSettings
        {
            Mode = TranscriptionMode.Local,
            Command = "whisper",
            Arguments = "-f {audio}",
        }));
    }

    /// <summary>
    /// A tool that only ever refuses wastes tokens and makes the model keep trying, so nothing
    /// is advertised until a speech model is configured.
    /// </summary>
    [Fact]
    public void No_meeting_tool_is_offered_while_transcription_is_off()
    {
        var context = Context(new TranscriptionSettings());

        Assert.Empty(new MeetingTools(context).GetTools(context));
    }

    [Fact]
    public void The_tool_appears_once_a_speech_model_is_configured()
    {
        var context = Context(new TranscriptionSettings
        {
            Mode = TranscriptionMode.Local,
            Command = "whisper",
            Arguments = "-f {audio}",
        });

        var tool = Assert.Single(new MeetingTools(context).GetTools(context));

        Assert.Equal("meeting_transcribe", tool.Name);
        Assert.Equal(AgentOrgan.Eyes, tool.Organ);
    }

    /// <summary>
    /// Uploading a recording is a different act from running a model on your own machine, and
    /// the consent prompt has to reflect that rather than treating both as "writing a file".
    /// </summary>
    [Fact]
    public void Sending_a_recording_away_is_treated_as_a_bigger_ask_than_running_it_locally()
    {
        var local = Assert.Single(Tools(new TranscriptionSettings
        {
            Mode = TranscriptionMode.Local, Command = "whisper", Arguments = "-f {audio}",
        }));

        var remote = Assert.Single(Tools(new TranscriptionSettings
        {
            Mode = TranscriptionMode.Remote, Endpoint = "https://api.example.com/v1",
        }));

        Assert.Equal(ToolRisk.Write, local.Risk);
        Assert.Equal(ApprovalKind.WriteFiles, local.ApprovalKind);

        Assert.Equal(ToolRisk.System, remote.Risk);
        Assert.Equal(ApprovalKind.NetworkAccess, remote.ApprovalKind);
    }

    // ── End to end, against a stub speech command ─────────────────────────────────────────

    [Fact]
    public async Task A_recording_is_transcribed_and_the_transcript_saved_beside_it()
    {
        var recording = Path.Combine(_workspace, "standup.wav");
        await File.WriteAllBytesAsync(recording, [0x52, 0x49, 0x46, 0x46], TestContext.Current.CancellationToken);

        const string spoken = "Ada takes the migration. Ravi writes the release note by Friday.";

        var result = await InvokeAsync(StubTranscriber.Returning(spoken), new() { ["path"] = recording });

        Assert.DoesNotContain("ERROR", result);
        Assert.Contains(spoken, result);

        var transcript = Path.Combine(_workspace, "standup.transcript.txt");
        Assert.True(File.Exists(transcript), result);
        Assert.Equal(spoken, await File.ReadAllTextAsync(transcript, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_file_that_is_not_a_recording_is_refused_before_anything_runs()
    {
        var notAudio = Path.Combine(_workspace, "notes.txt");
        await File.WriteAllTextAsync(notAudio, "not audio", TestContext.Current.CancellationToken);

        var stub = StubTranscriber.Returning("should never be reached");
        var result = await InvokeAsync(stub, new() { ["path"] = notAudio });

        Assert.StartsWith("REFUSED:", result);
        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task A_recording_outside_the_granted_folders_is_refused()
    {
        var outside = Path.Combine(_root, "elsewhere.wav");
        await File.WriteAllBytesAsync(outside, [0x52], TestContext.Current.CancellationToken);

        var stub = StubTranscriber.Returning("private conversation");
        var result = await InvokeAsync(stub, new() { ["path"] = outside });

        Assert.StartsWith("REFUSED:", result);
        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task An_existing_transcript_is_not_replaced_unless_asked_for()
    {
        var recording = Path.Combine(_workspace, "review.wav");
        await File.WriteAllBytesAsync(recording, [0x52], TestContext.Current.CancellationToken);

        var transcript = Path.Combine(_workspace, "review.transcript.txt");
        await File.WriteAllTextAsync(transcript, "the one I edited by hand", TestContext.Current.CancellationToken);

        var refused = await InvokeAsync(StubTranscriber.Returning("new"), new() { ["path"] = recording });

        Assert.StartsWith("REFUSED:", refused);
        Assert.Equal("the one I edited by hand",
            await File.ReadAllTextAsync(transcript, TestContext.Current.CancellationToken));

        await InvokeAsync(StubTranscriber.Returning("new"), new() { ["path"] = recording, ["overwrite"] = true });

        Assert.Equal("new", await File.ReadAllTextAsync(transcript, TestContext.Current.CancellationToken));
    }

    /// <summary>A long meeting must not swallow the context window on its way back to the model.</summary>
    [Fact]
    public async Task A_very_long_transcript_is_saved_whole_and_returned_short()
    {
        var recording = Path.Combine(_workspace, "long.wav");
        await File.WriteAllBytesAsync(recording, [0x52], TestContext.Current.CancellationToken);

        var spoken = string.Join(' ', Enumerable.Range(0, 4_000).Select(i => $"word{i}"));

        var result = await InvokeAsync(StubTranscriber.Returning(spoken), new() { ["path"] = recording });

        Assert.Contains("transcript continues", result);
        Assert.True(result.Length < spoken.Length, "the whole transcript came back to the model");

        var saved = await File.ReadAllTextAsync(Path.Combine(_workspace, "long.transcript.txt"),
            TestContext.Current.CancellationToken);

        Assert.Equal(spoken, saved);
    }

    [Fact]
    public async Task A_speech_model_that_fails_reports_why_rather_than_writing_an_empty_file()
    {
        var recording = Path.Combine(_workspace, "broken.wav");
        await File.WriteAllBytesAsync(recording, [0x52], TestContext.Current.CancellationToken);

        var result = await InvokeAsync(StubTranscriber.Failing("the model file is corrupt"),
            new() { ["path"] = recording });

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("corrupt", result);
        Assert.False(File.Exists(Path.Combine(_workspace, "broken.transcript.txt")));
    }

    /// <summary>
    /// The one part a stub cannot settle: a real command, started for real. Uses the shell that
    /// is definitely present rather than a speech model that is not.
    /// </summary>
    [Fact]
    public async Task A_real_command_is_started_and_its_output_becomes_the_transcript()
    {
        var recording = Path.Combine(_workspace, "cmd.wav");
        await File.WriteAllBytesAsync(recording, [0x52], TestContext.Current.CancellationToken);

        var (command, arguments) = OperatingSystem.IsWindows()
            ? ("cmd.exe", "/c echo Ada takes the migration.")
            : ("/bin/sh", "-c \"echo Ada takes the migration.\"");

        var transcriber = new LocalTranscriber(new TranscriptionSettings
        {
            Mode = TranscriptionMode.Local,
            Command = command,
            Arguments = arguments,
        });

        var result = await transcriber.TranscribeAsync(recording, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Contains("Ada takes the migration", result.Text);
    }

    [Fact]
    public async Task A_command_that_is_not_installed_says_so_plainly()
    {
        var recording = Path.Combine(_workspace, "missing.wav");
        await File.WriteAllBytesAsync(recording, [0x52], TestContext.Current.CancellationToken);

        var transcriber = new LocalTranscriber(new TranscriptionSettings
        {
            Mode = TranscriptionMode.Local,
            Command = "definitely-not-a-real-speech-model-xyz",
            Arguments = "{audio}",
        });

        var result = await transcriber.TranscribeAsync(recording, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("not installed", result.Error);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    private ToolContext Context(TranscriptionSettings transcription) => new()
    {
        Guard = new PathGuard(new PermissionPolicy
        {
            Roots = [new PermissionRoot { Path = _workspace, Access = FolderAccess.ReadWrite, IncludeSubfolders = true }],
            AllowNetwork = true,
        }),
        Approvals = new AutoApproveBroker(),
        Log = NullActionLog.Instance,
        Options = new AgentOptions(),
        RunId = "meeting",
        WorkingDirectory = _workspace,
        Transcription = transcription,
    };

    private IEnumerable<ToolDescriptor> Tools(TranscriptionSettings transcription)
    {
        var context = Context(transcription);
        return new MeetingTools(context).GetTools(context);
    }

    private async Task<string> InvokeAsync(ITranscriber transcriber, Dictionary<string, object?> arguments)
    {
        var context = Context(new TranscriptionSettings
        {
            Mode = TranscriptionMode.Local,
            Command = "stub",
            Arguments = "-f {audio}",
        });

        var tool = new MeetingTools(context, transcriber).GetTools(context).First().Function;

        return (await tool.InvokeAsync(new AIFunctionArguments(arguments), TestContext.Current.CancellationToken))
            ?.ToString() ?? "";
    }

    private sealed class StubTranscriber : ITranscriber
    {
        private readonly TranscriptResult _result;

        private StubTranscriber(TranscriptResult result) => _result = result;

        public int Calls { get; private set; }

        public static StubTranscriber Returning(string text) => new(TranscriptResult.Ok(text));
        public static StubTranscriber Failing(string error) => new(TranscriptResult.Failed(error));

        public Task<TranscriptResult> TranscribeAsync(string audioPath, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }
}
