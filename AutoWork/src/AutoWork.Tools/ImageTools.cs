using System.ComponentModel;
using AutoWork.Core.Agents;
using AutoWork.Core.Security;
using Microsoft.Extensions.AI;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace AutoWork.Tools;

/// <summary>
/// Batch image work — the "creative workflows" use case. Resizing 400 photos is the kind of
/// job people put off for months, so the batch tool is the one that matters here; the single
/// -image tools exist mostly so the model can check its work.
/// </summary>
public sealed class ImageTools : ToolSetBase, IToolProvider
{
    private static readonly string[] SupportedExtensions =
        [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tga", ".pbm", ".tiff"];

    public ImageTools(ToolContext context) : base(context) { }

    protected override AgentOrgan Organ => AgentOrgan.Hands;

    public string Name => "Images";

    public IEnumerable<ToolDescriptor> GetTools(ToolContext context)
    {
        var tools = new ImageTools(context);

        yield return Describe(AIFunctionFactory.Create(tools.InfoAsync, "image_info",
            "Get an image's dimensions, format and size."), ToolRisk.Safe);

        yield return Describe(AIFunctionFactory.Create(tools.ResizeAsync, "image_resize",
            "Resize one image. Aspect ratio is preserved unless both width and height are given with stretch=true."),
            ToolRisk.Write);

        yield return Describe(AIFunctionFactory.Create(tools.ConvertAsync, "image_convert",
            "Convert one image to another format: png, jpeg or webp."), ToolRisk.Write);

        yield return Describe(AIFunctionFactory.Create(tools.BatchResizeAsync, "image_batch_resize",
            "Resize every image in a folder, writing results to an output folder. Call with dryRun=true first."),
            ToolRisk.Write);
    }

    private static ToolDescriptor Describe(AIFunction function, ToolRisk risk) => new()
    {
        Function = function,
        Organ = AgentOrgan.Hands,
        Risk = risk,
        Category = "Images",
        ApprovalKind = risk == ToolRisk.Safe ? ApprovalKind.Other : ApprovalKind.WriteFiles,
    };

    [Description("Describe an image file.")]
    private Task<string> InfoAsync([Description("Image to inspect.")] string path)
    {
        var target = Locate(path);

        return GuardedAsync("image.info", $"Inspect {PathGuard.Describe(target)}", async () =>
        {
            var canonical = Guard.EnsureReadable(target);
            if (!File.Exists(canonical)) return Failed($"{PathGuard.Describe(canonical)} does not exist.");

            var info = await Image.IdentifyAsync(canonical).ConfigureAwait(false);

            return Ok($"{PathGuard.Describe(canonical)}\n" +
                      $"{info.Width} × {info.Height} px\n" +
                      $"Format: {info.Metadata.DecodedImageFormat?.Name ?? "unknown"}\n" +
                      $"Size: {Human(new FileInfo(canonical).Length)}");
        }, [target]);
    }

    [Description("Resize an image.")]
    private Task<string> ResizeAsync(
        [Description("Image to resize.")] string path,
        [Description("Where to write the result.")] string outputPath,
        [Description("Target width in pixels. 0 means derive it from the height.")] int width = 0,
        [Description("Target height in pixels. 0 means derive it from the width.")] int height = 0,
        [Description("Ignore the aspect ratio and use both dimensions exactly.")] bool stretch = false,
        [Description("JPEG/WebP quality, 1-100.")] int quality = 85)
    {
        var source = Locate(path);
        var destination = Locate(outputPath);

        return GuardedAsync("image.resize", $"Resize {PathGuard.Describe(source)}", async () =>
        {
            var canonicalSource = Guard.EnsureReadable(source);
            var canonicalDestination = Guard.EnsureWritable(destination);

            if (!File.Exists(canonicalSource)) return Failed($"{PathGuard.Describe(canonicalSource)} does not exist.");
            if (width <= 0 && height <= 0) return Failed("Give a width, a height, or both.");

            using var image = await Image.LoadAsync(canonicalSource).ConfigureAwait(false);
            var original = $"{image.Width}×{image.Height}";

            image.Mutate(x => x.Resize(BuildResizeOptions(width, height, stretch)));

            Directory.CreateDirectory(Path.GetDirectoryName(canonicalDestination)!);
            await SaveAsync(image, canonicalDestination, quality).ConfigureAwait(false);

            return Ok($"Resized {original} → {image.Width}×{image.Height}, saved to {PathGuard.Describe(canonicalDestination)}.");
        },
        [source, destination], ApprovalKind.WriteFiles);
    }

    private static ResizeOptions BuildResizeOptions(int width, int height, bool stretch) => new()
    {
        Size = new Size(width, height),
        Mode = stretch && width > 0 && height > 0 ? ResizeMode.Stretch : ResizeMode.Max,
        Sampler = KnownResamplers.Lanczos3,
    };

    [Description("Convert an image to another format.")]
    private Task<string> ConvertAsync(
        [Description("Image to convert.")] string path,
        [Description("Where to write the result.")] string outputPath,
        [Description("Target format: png, jpeg or webp.")] string format = "png",
        [Description("JPEG/WebP quality, 1-100.")] int quality = 85)
    {
        var source = Locate(path);
        var destination = Locate(outputPath);

        return GuardedAsync("image.convert", $"Convert {PathGuard.Describe(source)} to {format}", async () =>
        {
            var canonicalSource = Guard.EnsureReadable(source);
            var canonicalDestination = Guard.EnsureWritable(destination);

            if (!File.Exists(canonicalSource)) return Failed($"{PathGuard.Describe(canonicalSource)} does not exist.");

            using var image = await Image.LoadAsync(canonicalSource).ConfigureAwait(false);

            Directory.CreateDirectory(Path.GetDirectoryName(canonicalDestination)!);
            await SaveAsync(image, canonicalDestination, quality, format).ConfigureAwait(false);

            return Ok($"Converted to {format}, saved to {PathGuard.Describe(canonicalDestination)} " +
                      $"({Human(new FileInfo(canonicalDestination).Length)}).");
        },
        [source, destination], ApprovalKind.WriteFiles);
    }

    [Description("Resize every image in a folder.")]
    private Task<string> BatchResizeAsync(
        [Description("Folder containing the images.")] string folder,
        [Description("Folder to write results into.")] string outputFolder,
        [Description("Longest edge in pixels. Images already smaller are copied unchanged.")] int maxDimension = 1600,
        [Description("Output format: keep, png, jpeg or webp.")] string format = "keep",
        [Description("JPEG/WebP quality, 1-100.")] int quality = 85,
        [Description("Preview without writing anything. Do this first.")] bool dryRun = true)
    {
        var source = Locate(folder);
        var destination = Locate(outputFolder);

        return GuardedAsync("image.batch_resize",
            dryRun ? $"Preview resizing {PathGuard.Describe(source)}" : $"Resize images in {PathGuard.Describe(source)}",
            async () =>
            {
                var canonicalSource = Guard.EnsureReadable(source);
                if (!Directory.Exists(canonicalSource)) return Failed($"{PathGuard.Describe(canonicalSource)} is not a folder.");

                var canonicalDestination = dryRun
                    ? PathGuard.Canonicalize(destination)
                    : Guard.EnsureWritable(destination);

                var images = Directory
                    .EnumerateFiles(canonicalSource, "*", SearchOption.TopDirectoryOnly)
                    .Where(f => SupportedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                    .Where(f => Guard.IsAllowed(f, FolderAccess.Read, out _, out _))
                    .Take(Guard.Policy.MaxBatchSize)
                    .ToList();

                if (images.Count == 0)
                    return Ok($"No images found in {PathGuard.Describe(canonicalSource)}.");

                if (dryRun)
                {
                    var totalBytes = images.Sum(f => new FileInfo(f).Length);
                    return Ok($"Would resize {images.Count} images ({Human(totalBytes)}) from " +
                              $"{PathGuard.Describe(canonicalSource)} into {PathGuard.Describe(canonicalDestination)}, " +
                              $"longest edge {maxDimension}px, format {format}.\n\nCall again with dryRun=false to apply.");
                }

                Directory.CreateDirectory(canonicalDestination);

                int processed = 0, skipped = 0;
                long before = 0, after = 0;

                foreach (var file in images)
                {
                    try
                    {
                        using var image = await Image.LoadAsync(file).ConfigureAwait(false);
                        before += new FileInfo(file).Length;

                        if (Math.Max(image.Width, image.Height) > maxDimension)
                        {
                            image.Mutate(x => x.Resize(new ResizeOptions
                            {
                                Size = new Size(maxDimension, maxDimension),
                                Mode = ResizeMode.Max,
                                Sampler = KnownResamplers.Lanczos3,
                            }));
                        }

                        var extension = format.Equals("keep", StringComparison.OrdinalIgnoreCase)
                            ? Path.GetExtension(file)
                            : "." + format.ToLowerInvariant();

                        var output = Path.Combine(canonicalDestination,
                            Path.GetFileNameWithoutExtension(file) + extension);

                        await SaveAsync(image, output, quality).ConfigureAwait(false);
                        after += new FileInfo(output).Length;
                        processed++;
                    }
                    catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or IOException)
                    {
                        // One unreadable file should not abandon the other 399.
                        skipped++;
                    }
                }

                var saved = before > after ? $" Saved {Human(before - after)} ({100.0 * (before - after) / before:0.#}%)." : "";
                var skippedNote = skipped > 0 ? $" {skipped} file(s) skipped as unreadable." : "";

                return Ok($"Resized {processed} images into {PathGuard.Describe(canonicalDestination)}.{saved}{skippedNote}");
            },
            [source, destination],
            dryRun ? null : ApprovalKind.WriteFiles);
    }

    private static async Task SaveAsync(Image image, string path, int quality, string? format = null)
    {
        var extension = (format is null ? Path.GetExtension(path) : "." + format).ToLowerInvariant();
        var clamped = Math.Clamp(quality, 1, 100);

        switch (extension)
        {
            case ".jpg" or ".jpeg":
                await image.SaveAsJpegAsync(path, new JpegEncoder { Quality = clamped }).ConfigureAwait(false);
                break;
            case ".webp":
                await image.SaveAsWebpAsync(path, new WebpEncoder { Quality = clamped }).ConfigureAwait(false);
                break;
            case ".png":
                await image.SaveAsPngAsync(path, new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression })
                    .ConfigureAwait(false);
                break;
            default:
                await image.SaveAsync(path).ConfigureAwait(false);
                break;
        }
    }
}
