using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AutoWork.Tools;

/// <summary>
/// Cross-platform screen capture — the Eyes subsystem's only platform-specific code.
///
/// Windows goes through GDI directly rather than System.Drawing.Common, which is
/// Windows-only anyway and would drag a dependency into a cross-platform project. macOS and
/// Linux shell out to the screenshot tool the desktop already ships, because there is no
/// portable API and reimplementing X11/Wayland capture is not the right trade.
/// </summary>
internal static partial class ScreenCapture
{
    /// <summary>Captures the full virtual desktop to a PNG. Returns the file path.</summary>
    public static async Task<string> CaptureAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        if (OperatingSystem.IsWindows())
        {
            CaptureWindows(outputPath);
            return outputPath;
        }

        if (OperatingSystem.IsMacOS())
        {
            await RunAsync("screencapture", ["-x", "-t", "png", outputPath], cancellationToken).ConfigureAwait(false);
            return outputPath;
        }

        return await CaptureLinuxAsync(outputPath, cancellationToken).ConfigureAwait(false);
    }

    public static string DescribeDisplays()
    {
        if (!OperatingSystem.IsWindows())
            return "Display enumeration is only available on Windows. Capture the screen to see what is there.";

        return DescribeWindowsDisplays();
    }

    // ── Windows ───────────────────────────────────────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private static void CaptureWindows(string outputPath)
    {
        var left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        var top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Windows reported a zero-sized desktop; there may be no active display.");

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) throw new InvalidOperationException("Could not obtain a device context for the screen.");

        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;

        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, width, height);

            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
                throw new InvalidOperationException("Could not allocate a bitmap for the capture.");

            var previous = NativeMethods.SelectObject(memoryDc, bitmap);

            // CAPTUREBLT includes layered windows, which is what makes tooltips and
            // translucent overlays show up in the capture.
            var copied = NativeMethods.BitBlt(memoryDc, 0, 0, width, height, screenDc, left, top,
                NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);

            NativeMethods.SelectObject(memoryDc, previous);

            if (!copied) throw new InvalidOperationException("The screen copy failed.");

            var header = new NativeMethods.BitmapInfoHeader
            {
                Size = Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = width,
                // Negative height requests a top-down DIB, so rows arrive in the order
                // ImageSharp expects and no flip is needed.
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = NativeMethods.BI_RGB,
            };

            var buffer = new byte[width * height * 4];

            int scanned;
            unsafe
            {
                fixed (byte* pixels = buffer)
                {
                    scanned = NativeMethods.GetDIBits(memoryDc, bitmap, 0, (uint)height,
                        (IntPtr)pixels, ref header, NativeMethods.DIB_RGB_COLORS);
                }
            }

            if (scanned == 0) throw new InvalidOperationException("Reading the captured pixels failed.");

            using var image = Image.LoadPixelData<Bgra32>(buffer, width, height);
            image.SaveAsPng(outputPath);
        }
        finally
        {
            if (bitmap != IntPtr.Zero) NativeMethods.DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero) NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [SupportedOSPlatform("windows")]
    private static string DescribeWindowsDisplays()
    {
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        var primaryWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        var primaryHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
        var count = NativeMethods.GetSystemMetrics(NativeMethods.SM_CMONITORS);

        return $"{count} display(s). Primary {primaryWidth}×{primaryHeight}. " +
               $"Combined desktop {width}×{height}.";
    }

    // ── Linux ─────────────────────────────────────────────────────────────────────────────

    private static async Task<string> CaptureLinuxAsync(string outputPath, CancellationToken cancellationToken)
    {
        // Ordered by desktop: Wayland first, then GNOME, KDE, and finally ImageMagick/scrot.
        (string Tool, string[] Arguments)[] candidates =
        [
            ("grim", [outputPath]),
            ("gnome-screenshot", ["-f", outputPath]),
            ("spectacle", ["-b", "-n", "-o", outputPath]),
            ("import", ["-window", "root", outputPath]),
            ("scrot", ["-o", outputPath]),
        ];

        var tried = new List<string>();

        foreach (var (tool, arguments) in candidates)
        {
            if (!IsOnPath(tool)) continue;
            tried.Add(tool);

            try
            {
                await RunAsync(tool, arguments, cancellationToken).ConfigureAwait(false);
                if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0) return outputPath;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Try the next tool.
            }
        }

        throw new PlatformNotSupportedException(
            tried.Count == 0
                ? "No screenshot tool was found. Install one of: grim, gnome-screenshot, spectacle, imagemagick, or scrot."
                : $"Screen capture failed with the available tools ({string.Join(", ", tried)}). " +
                  "On Wayland, the desktop may require granting screen-capture permission.");
    }

    private static bool IsOnPath(string tool)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return false;

        return path.Split(Path.PathSeparator)
            .Any(directory => !string.IsNullOrWhiteSpace(directory) && File.Exists(Path.Combine(directory, tool)));
    }

    private static async Task RunAsync(string fileName, string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"{fileName} could not be started.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"{fileName} exited with code {process.ExitCode}. {error.Trim()}");
        }
    }

    // ── Interop ───────────────────────────────────────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private static unsafe partial class NativeMethods
    {
        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;
        public const int SM_XVIRTUALSCREEN = 76;
        public const int SM_YVIRTUALSCREEN = 77;
        public const int SM_CXVIRTUALSCREEN = 78;
        public const int SM_CYVIRTUALSCREEN = 79;
        public const int SM_CMONITORS = 80;

        public const int SRCCOPY = 0x00CC0020;
        public const int CAPTUREBLT = 0x40000000;
        public const int BI_RGB = 0;
        public const uint DIB_RGB_COLORS = 0;

        [StructLayout(LayoutKind.Sequential)]
        public struct BitmapInfoHeader
        {
            public int Size;
            public int Width;
            public int Height;
            public short Planes;
            public short BitCount;
            public int Compression;
            public int SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public int ClrUsed;
            public int ClrImportant;
        }

        [LibraryImport("user32.dll")]
        public static partial int GetSystemMetrics(int index);

        [LibraryImport("user32.dll")]
        public static partial IntPtr GetDC(IntPtr window);

        [LibraryImport("user32.dll")]
        public static partial int ReleaseDC(IntPtr window, IntPtr deviceContext);

        [LibraryImport("gdi32.dll")]
        public static partial IntPtr CreateCompatibleDC(IntPtr deviceContext);

        [LibraryImport("gdi32.dll")]
        public static partial IntPtr CreateCompatibleBitmap(IntPtr deviceContext, int width, int height);

        [LibraryImport("gdi32.dll")]
        public static partial IntPtr SelectObject(IntPtr deviceContext, IntPtr handle);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool BitBlt(IntPtr destination, int x, int y, int width, int height,
            IntPtr source, int sourceX, int sourceY, int rasterOperation);

        [LibraryImport("gdi32.dll")]
        public static partial int GetDIBits(IntPtr deviceContext, IntPtr bitmap, uint startScan, uint scanLines,
            IntPtr bits, ref BitmapInfoHeader info, uint usage);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool DeleteObject(IntPtr handle);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool DeleteDC(IntPtr deviceContext);
    }
}
