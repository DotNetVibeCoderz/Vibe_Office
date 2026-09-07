// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace OfficeNet.TestKit;

/// <summary>
/// A temporary file that deletes itself, so a test that saves to disk leaves nothing behind.
/// </summary>
/// <remarks>
/// Round-tripping through a real file rather than through a <see cref="MemoryStream"/> is
/// deliberate: the file path is what exercises the atomic temp-file-and-move save, and a bug in
/// that path would be invisible to a stream-only test.
/// </remarks>
public sealed class TempFile : IDisposable
{
    /// <summary>Creates a temp file path with the given extension. The file is not created.</summary>
    public TempFile(string extension = ".tmp")
    {
        var name = "officenet-test-" + Guid.NewGuid().ToString("N")[..12] + extension;
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name);
    }

    /// <summary>The full path.</summary>
    public string Path { get; }

    /// <summary>True once something has written to the path.</summary>
    public bool Exists => File.Exists(Path);

    /// <summary>The file's length in bytes.</summary>
    public long Length => new FileInfo(Path).Length;

    /// <summary>The file's bytes.</summary>
    public byte[] ReadAllBytes() => File.ReadAllBytes(Path);

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch (IOException)
        {
            // A file still held open by a failed test is not worth failing the run over; the
            // operating system cleans the temp directory eventually.
        }
    }

    /// <inheritdoc />
    public override string ToString() => Path;
}

/// <summary>Small images built in code, so tests need no binary fixtures.</summary>
public static class TestImages
{
    /// <summary>
    /// A minimal valid PNG of one solid colour.
    /// </summary>
    /// <remarks>
    /// Generated rather than checked in so the tests carry no binary files, and so the pixel data
    /// is verifiable by reading this method instead of by trusting a blob.
    /// </remarks>
    public static byte[] SolidPng(int width, int height, byte red, byte green, byte blue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var rgb = new byte[width * height * 3];

        for (var i = 0; i < rgb.Length; i += 3)
        {
            rgb[i] = red;
            rgb[i + 1] = green;
            rgb[i + 2] = blue;
        }

        // PdfNet's PNG encoder is the one under test elsewhere; reusing it here would make a
        // fixture depend on the code it is meant to exercise. This writes its own.
        return EncodePng(rgb, width, height);
    }

    private static byte[] EncodePng(byte[] rgb, int width, int height)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8;
        header[9] = 2;
        WriteChunk(output, "IHDR"u8, header);

        var stride = width * 3;
        var raw = new byte[(stride + 1) * height];

        for (var y = 0; y < height; y++)
        {
            raw[y * (stride + 1)] = 0;
            Array.Copy(rgb, y * stride, raw, y * (stride + 1) + 1, stride);
        }

        using var compressed = new MemoryStream();

        using (var deflate = new System.IO.Compression.ZLibStream(compressed,
                   System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw, 0, raw.Length);
        }

        WriteChunk(output, "IDAT"u8, compressed.ToArray());
        WriteChunk(output, "IEND"u8, []);

        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);
        output.Write(type);
        output.Write(data);

        var crc = Crc32(type, data);
        Span<byte> crcBytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (uint n = 0; n < 256; n++)
        {
            var c = n;

            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ c >> 1 : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var b in type)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ crc >> 8;
        }

        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ crc >> 8;
        }

        return crc ^ 0xFFFFFFFF;
    }
}
