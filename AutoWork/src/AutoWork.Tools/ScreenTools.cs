using System.ComponentModel;
using AutoWork.Core;
using AutoWork.Core.Agents;
using AutoWork.Core.Security;
using Microsoft.Extensions.AI;

namespace AutoWork.Tools;

/// <summary>
/// The Eyes subsystem's capture half. Taking the picture lives here; understanding it lives in
/// AutoWork.Agents, which is the layer that has a vision model to hand.
///
/// Screenshots land in AutoWork's own folder, which PathGuard treats as protected — so a
/// capture of the user's screen cannot then be read back through the file tools and copied
/// somewhere else.
/// </summary>
public sealed class ScreenTools : ToolSetBase, IToolProvider
{
    public ScreenTools(ToolContext context) : base(context) { }

    protected override AgentOrgan Organ => AgentOrgan.Eyes;

    public string Name => "Screen";

    public IEnumerable<ToolDescriptor> GetTools(ToolContext context)
    {
        if (!context.Guard.Policy.AllowScreenCapture) yield break;

        var tools = new ScreenTools(context);

        yield return new ToolDescriptor
        {
            Function = AIFunctionFactory.Create(tools.CaptureAsync, "screen_capture",
                "Take a screenshot of the whole desktop and save it. Returns the file path, which you can then pass to screen_look to read what is on screen."),
            Organ = AgentOrgan.Eyes,
            Risk = ToolRisk.Safe,
            Category = "Screen",
            ApprovalKind = ApprovalKind.CaptureScreen,
        };

        yield return new ToolDescriptor
        {
            Function = AIFunctionFactory.Create(tools.DisplaysAsync, "screen_displays",
                "Report how many displays there are and their resolutions."),
            Organ = AgentOrgan.Eyes,
            Risk = ToolRisk.Safe,
            Category = "Screen",
        };
    }

    [Description("Capture the screen to a PNG file.")]
    private Task<string> CaptureAsync(
        [Description("Optional label used in the filename, so captures are easy to tell apart.")] string? label = null)
    {
        var name = Sanitize(label);
        var output = Path.Combine(AppPaths.ScreenshotsDirectory,
            $"{DateTime.Now:yyyyMMdd-HHmmss}{(name is null ? "" : "-" + name)}.png");

        return GuardedAsync("screen.capture", "Capture the screen", async () =>
        {
            if (!Guard.Policy.AllowScreenCapture)
                return Refused("Screen capture is turned off in Settings › Permissions.");

            try
            {
                await ScreenCapture.CaptureAsync(output).ConfigureAwait(false);
            }
            catch (PlatformNotSupportedException ex)
            {
                return Refused(ex.Message);
            }

            var info = new FileInfo(output);
            if (!info.Exists || info.Length == 0)
                return Failed("The capture produced an empty file.");

            return Ok($"Captured the screen to {output} ({Human(info.Length)}). " +
                      "Pass this path to screen_look to read what is on it.");
        },
        [output],
        // Capture is read-only but it photographs whatever is on screen, which may include
        // things the user never meant to share. That warrants asking.
        ApprovalKind.CaptureScreen,
        "AutoWork will take a picture of your entire desktop.");
    }

    [Description("List the displays attached to this machine.")]
    private Task<string> DisplaysAsync() =>
        GuardedAsync("screen.displays", "Check displays", () => Ok(ScreenCapture.DescribeDisplays()));

    private static string? Sanitize(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;

        var cleaned = new string(label.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return cleaned.Length == 0 ? null : cleaned[..Math.Min(cleaned.Length, 40)];
    }
}
