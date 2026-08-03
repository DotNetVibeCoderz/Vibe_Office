using System.ComponentModel;
using AutoWork.Core;
using AutoWork.Core.Agents;
using AutoWork.Core.Configuration;
using AutoWork.Core.Security;
using AutoWork.Providers;
using AutoWork.Tools;
using Microsoft.Extensions.AI;

namespace AutoWork.Agents;

/// <summary>
/// The understanding half of the Eyes subsystem. Capture lives in AutoWork.Tools; reading what
/// was captured needs a vision model, which only this layer has.
///
/// Keeping the loop text-only matters: the executor never carries raw images in its context —
/// it asks a question about the screen and gets an answer back as text. One screenshot costs
/// well over a thousand tokens, and a run that pulls a dozen of them into history would
/// trigger compaction long before it finished the actual job.
/// </summary>
public sealed class VisionTools : ToolSetBase, IToolProvider
{
    private readonly ModelClientFactory _factory;
    private readonly ModelProfile? _visionModel;

    public VisionTools(ToolContext context, ModelClientFactory factory, ModelProfile? visionModel)
        : base(context)
    {
        _factory = factory;
        _visionModel = visionModel;
    }

    protected override AgentOrgan Organ => AgentOrgan.Eyes;

    public string Name => "Vision";

    public IEnumerable<ToolDescriptor> GetTools(ToolContext context)
    {
        if (!context.Guard.Policy.AllowScreenCapture) yield break;

        yield return new ToolDescriptor
        {
            Function = AIFunctionFactory.Create(LookAsync, "screen_look",
                "Look at the screen and answer a question about it. Captures a fresh screenshot unless you pass a path from screen_capture. Use this to read dialogs, find a button's coordinates, or read a table you cannot get at as a file."),
            Organ = AgentOrgan.Eyes,
            Risk = ToolRisk.Safe,
            Category = "Screen",
            ApprovalKind = ApprovalKind.CaptureScreen,
        };

        yield return new ToolDescriptor
        {
            Function = AIFunctionFactory.Create(DescribeImageAsync, "image_describe",
                "Describe or answer a question about an image file on disk."),
            Organ = AgentOrgan.Eyes,
            Risk = ToolRisk.Safe,
            Category = "Screen",
        };
    }

    [Description("Look at the screen and answer a question about it.")]
    private Task<string> LookAsync(
        [Description("What you want to know about what is on screen.")] string question,
        [Description("Optional existing screenshot path. Leave empty to capture a fresh one.")] string? imagePath = null)
        => GuardedAsync("vision.look", $"Look at the screen: {Trim(question)}", async () =>
        {
            if (_visionModel is null) return NoVisionModel();

            if (!Guard.Policy.AllowScreenCapture)
                return Refused("Screen capture is turned off in Settings › Permissions.");

            string capture;

            if (string.IsNullOrWhiteSpace(imagePath))
            {
                capture = Path.Combine(AppPaths.ScreenshotsDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss}-look.png");
                try
                {
                    await ScreenCaptureBridge.CaptureAsync(capture).ConfigureAwait(false);
                }
                catch (PlatformNotSupportedException ex)
                {
                    return Refused(ex.Message);
                }
            }
            else
            {
                // A path the model supplied is user data and must clear the sandbox.
                capture = Guard.EnsureReadable(Locate(imagePath));
                if (!File.Exists(capture)) return Failed($"{PathGuard.Describe(capture)} does not exist.");
            }

            return await AskAsync(capture, question).ConfigureAwait(false);
        },
        approval: ApprovalKind.CaptureScreen,
        approvalDetail: "AutoWork will photograph your desktop and send that image to the vision model.");

    [Description("Describe an image file.")]
    private Task<string> DescribeImageAsync(
        [Description("Image file to look at.")] string path,
        [Description("What you want to know. Defaults to a general description.")] string question = "Describe this image in detail.")
    {
        var target = Locate(path);

        return GuardedAsync("vision.describe", $"Look at {PathGuard.Describe(target)}", async () =>
        {
            if (_visionModel is null) return NoVisionModel();

            var canonical = Guard.EnsureReadable(target);
            if (!File.Exists(canonical)) return Failed($"{PathGuard.Describe(canonical)} does not exist.");

            return await AskAsync(canonical, question).ConfigureAwait(false);
        }, [target]);
    }

    private async Task<string> AskAsync(string imagePath, string question)
    {
        var bytes = await File.ReadAllBytesAsync(imagePath).ConfigureAwait(false);
        var client = _factory.CreateChatClient(_visionModel!);

        var message = new ChatMessage(ChatRole.User,
        [
            new TextContent(
                $"""
                 {question}

                 Answer from what is actually visible. If you are asked where something is, give
                 pixel coordinates measured from the top-left of the image. If something is not
                 visible, say so rather than guessing.
                 """),
            new DataContent(bytes, MediaTypeFor(imagePath)),
        ]);

        var response = await client.GetResponseAsync([message],
            new ChatOptions { Temperature = 0, MaxOutputTokens = 1500 }).ConfigureAwait(false);

        var text = response.Text?.Trim();
        return string.IsNullOrWhiteSpace(text) ? Failed("The vision model returned nothing.") : Ok(text);
    }

    private string NoVisionModel() =>
        Refused("No vision-capable model is configured. Add one in Settings › Models and select it as the vision model.");

    private static string MediaTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "image/png",
    };

    private static string Trim(string text) =>
        text.Length <= 60 ? text : text[..60] + "…";
}
