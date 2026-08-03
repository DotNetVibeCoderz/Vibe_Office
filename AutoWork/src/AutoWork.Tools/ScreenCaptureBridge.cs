namespace AutoWork.Tools;

/// <summary>
/// Public seam over the platform capture code, for the agent layer's vision tools.
/// The interop itself stays internal — callers only ever need "put a screenshot here".
/// </summary>
public static class ScreenCaptureBridge
{
    /// <summary>Captures the full desktop to a PNG at <paramref name="outputPath"/>.</summary>
    /// <exception cref="PlatformNotSupportedException">No capture mechanism is available.</exception>
    public static Task<string> CaptureAsync(string outputPath, CancellationToken cancellationToken = default) =>
        ScreenCapture.CaptureAsync(outputPath, cancellationToken);

    public static string DescribeDisplays() => ScreenCapture.DescribeDisplays();
}
