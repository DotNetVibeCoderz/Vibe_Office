// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Diagnostics;
using OfficeNet;
using OfficeNet.Core;
using OfficeNet.Rendering;

// A batch converter: point it at a folder, get PDFs (and optionally thumbnails) out.
//
// The interesting part of a batch job is not the conversion — that is one call — it is what happens
// when one file in a thousand is malformed. This sample is built around that: every failure is
// caught, recorded and reported at the end, and the process exit code says whether anything failed
// so a scheduler can act on it.

var options = CommandLine.Parse(args);

if (options is null)
{
    CommandLine.PrintUsage();
    return 2;
}

if (!Directory.Exists(options.Input))
{
    Console.Error.WriteLine($"Folder masukan tidak ditemukan: {options.Input}");
    return 2;
}

Directory.CreateDirectory(options.Output);

var files = Directory
    .EnumerateFiles(options.Input, "*.*",
        options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
    .Where(Office.IsSupportedExtension)
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    .ToList();

if (files.Count == 0)
{
    Console.WriteLine($"Tidak ada berkas yang didukung di {options.Input}.");
    Console.WriteLine($"Ekstensi yang didukung: {string.Join(", ", Office.SupportedExtensions)}");
    return 0;
}

Console.WriteLine($"OfficeNet batch converter — {files.Count} berkas");
Console.WriteLine($"  masukan  : {Path.GetFullPath(options.Input)}");
Console.WriteLine($"  keluaran : {Path.GetFullPath(options.Output)}");
Console.WriteLine();

var succeeded = 0;
var failures = new List<(string File, string Reason)>();
var stopwatch = Stopwatch.StartNew();

// Parallelism is opt-in rather than the default. Conversion is CPU-bound and each document holds a
// full object graph, so running one per core is fast but multiplies peak memory by the core count —
// which is how a batch job that worked on a laptop dies on a small container.
var parallelism = options.Parallel ? Environment.ProcessorCount : 1;

Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, path =>
{
    var name = Path.GetRelativePath(options.Input, path);

    try
    {
        var target = Path.Combine(options.Output,
            Path.ChangeExtension(name, ".pdf"));

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        if (options.SkipExisting && File.Exists(target) &&
            File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(path))
        {
            lock (failures)
            {
                Console.WriteLine($"  lewati  {name}");
            }

            return;
        }

        Office.ConvertToPdf(path, target);

        if (options.Thumbnails)
        {
            var thumbnail = Path.ChangeExtension(target, ".png");
            File.WriteAllBytes(thumbnail, DocumentRenderer.RenderThumbnail(path, options.ThumbnailWidth));
        }

        var size = new FileInfo(target).Length / 1024.0;

        lock (failures)
        {
            succeeded++;
            Console.WriteLine($"  ok      {name,-50} {size,8:0.0} KB");
        }
    }
    catch (OfficeNetException ex)
    {
        // A document this library cannot read is a data problem, not a bug in the job. Record it
        // and keep going: the other 999 files still need converting.
        lock (failures)
        {
            failures.Add((name, ex.Message));
            Console.WriteLine($"  GAGAL   {name,-50} {ex.Message}");
        }
    }
    catch (IOException ex)
    {
        lock (failures)
        {
            failures.Add((name, ex.Message));
            Console.WriteLine($"  GAGAL   {name,-50} {ex.Message}");
        }
    }
});

stopwatch.Stop();

Console.WriteLine();
Console.WriteLine($"Selesai dalam {stopwatch.Elapsed.TotalSeconds:0.0} detik — " +
                  $"{succeeded} berhasil, {failures.Count} gagal, " +
                  $"{files.Count - succeeded - failures.Count} dilewati.");

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Kegagalan:");

    foreach (var (file, reason) in failures)
    {
        Console.WriteLine($"  {file}");
        Console.WriteLine($"    {reason}");
    }
}

// A non-zero exit code is what lets cron, a CI step or a scheduler notice that something went
// wrong. Printing the failures and exiting 0 would hide them.
return failures.Count == 0 ? 0 : 1;

/// <summary>Parsed command line.</summary>
internal sealed record ConverterOptions(
    string Input,
    string Output,
    bool Recursive,
    bool Parallel,
    bool Thumbnails,
    int ThumbnailWidth,
    bool SkipExisting);

internal static class CommandLine
{
    public static ConverterOptions? Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            return null;
        }

        var input = args[0];
        var output = Path.Combine(input, "pdf");
        var recursive = false;
        var parallel = false;
        var thumbnails = false;
        var width = 400;
        var skipExisting = false;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o" or "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;

                case "-r" or "--recursive":
                    recursive = true;
                    break;

                case "-p" or "--parallel":
                    parallel = true;
                    break;

                case "-t" or "--thumbnails":
                    thumbnails = true;
                    break;

                case "--thumbnail-width" when i + 1 < args.Length:
                    width = int.TryParse(args[++i], out var parsed) ? parsed : width;
                    break;

                case "--skip-existing":
                    skipExisting = true;
                    break;

                default:
                    Console.Error.WriteLine($"Opsi tidak dikenal: {args[i]}");
                    return null;
            }
        }

        return new ConverterOptions(input, output, recursive, parallel, thumbnails, width, skipExisting);
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            OfficeNet batch converter — Word, Excel, PowerPoint dan PDF ke PDF.
            Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

              batchconvert <folder> [opsi]

            Opsi:
              -o, --output <folder>     Folder keluaran (baku: <folder>/pdf)
              -r, --recursive           Termasuk subfolder
              -p, --parallel            Satu berkas per core
                                        (lebih cepat, tetapi memori puncak dikali jumlah core)
              -t, --thumbnails          Tulis juga thumbnail PNG per berkas
                  --thumbnail-width <n> Lebar thumbnail dalam piksel (baku: 400)
                  --skip-existing       Lewati berkas yang PDF-nya sudah lebih baru
              -h, --help                Tampilkan bantuan ini

            Contoh:
              batchconvert ./dokumen -r -o ./keluaran -t

            Kode keluar: 0 bila semua berhasil, 1 bila ada yang gagal, 2 bila argumennya salah.
            """);
    }
}
