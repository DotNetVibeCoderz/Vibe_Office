// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Buffers.Binary;
using System.Text;

namespace PdfNet.Document;

/// <summary>
/// An sRGB ICC profile, built rather than shipped.
/// </summary>
/// <remarks>
/// <para>
/// PDF/A needs an output intent whose profile says what the file's RGB means. A binary profile
/// copied from somewhere carries somebody's licence; this one is computed from the numbers in
/// IEC 61966-2-1 itself, in the same shape as the long-standing <c>sRGB IEC61966-2.1</c> profile:
/// ICC version 2, a display (<c>mntr</c>) profile, primaries adapted to D50 by Bradford, and the
/// standard's piecewise transfer curve sampled at 1024 points rather than approximated by a gamma.
/// </para>
/// <para>
/// Built once and cached; a few kilobytes.
/// </para>
/// </remarks>
internal static class SrgbProfile
{
    public const string Description = "sRGB IEC61966-2.1";

    private static readonly Lazy<byte[]> Profile = new(Build);

    public static byte[] Bytes => Profile.Value;

    private static byte[] Build()
    {
        // Each tag's data, padded to four bytes. The three curves share one copy.
        var description = TextDescription(Description);
        var copyright = Text("No copyright, use freely");
        var whitePoint = Xyz(0.9505, 1.0000, 1.0891);
        var red = Xyz(0.4361, 0.2225, 0.0139);
        var green = Xyz(0.3851, 0.7169, 0.0971);
        var blue = Xyz(0.1431, 0.0606, 0.7141);
        var curve = Curve();

        (string Signature, byte[] Data)[] tags =
        [
            ("desc", description), ("cprt", copyright), ("wtpt", whitePoint),
            ("rXYZ", red), ("gXYZ", green), ("bXYZ", blue),
            ("rTRC", curve), ("gTRC", curve), ("bTRC", curve),
        ];

        var tableSize = 4 + tags.Length * 12;
        var offsets = new Dictionary<byte[], int>(ReferenceEqualityComparer.Instance);
        var data = new MemoryStream();
        var position = 128 + tableSize;

        foreach (var (_, bytes) in tags)
        {
            if (offsets.ContainsKey(bytes))
            {
                continue;
            }

            offsets[bytes] = position;
            data.Write(bytes);
            var padding = (4 - bytes.Length % 4) % 4;
            data.Write(new byte[padding]);
            position += bytes.Length + padding;
        }

        var profile = new byte[position];
        var span = profile.AsSpan();

        // Header.
        BinaryPrimitives.WriteUInt32BigEndian(span[0..], (uint)profile.Length);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], 0x02100000);
        Ascii("mntr").CopyTo(span[12..]);
        Ascii("RGB ").CopyTo(span[16..]);
        Ascii("XYZ ").CopyTo(span[20..]);

        // A fixed creation date, so the profile — and every file carrying it — is reproducible.
        ushort[] date = [2026, 1, 1, 0, 0, 0];

        for (var i = 0; i < date.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(span[(24 + i * 2)..], date[i]);
        }

        Ascii("acsp").CopyTo(span[36..]);
        BinaryPrimitives.WriteUInt32BigEndian(span[64..], 0);   // perceptual
        WriteXyzNumber(span[68..], 0.9642, 1.0000, 0.8249);       // the PCS illuminant, D50

        // Tag table.
        BinaryPrimitives.WriteUInt32BigEndian(span[128..], (uint)tags.Length);

        for (var i = 0; i < tags.Length; i++)
        {
            var entry = span[(132 + i * 12)..];
            Ascii(tags[i].Signature).CopyTo(entry);
            BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)offsets[tags[i].Data]);
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)tags[i].Data.Length);
        }

        data.ToArray().CopyTo(span[(128 + tableSize)..]);
        return profile;
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static void WriteXyzNumber(Span<byte> target, double x, double y, double z)
    {
        BinaryPrimitives.WriteInt32BigEndian(target, S15Fixed16(x));
        BinaryPrimitives.WriteInt32BigEndian(target[4..], S15Fixed16(y));
        BinaryPrimitives.WriteInt32BigEndian(target[8..], S15Fixed16(z));
    }

    private static int S15Fixed16(double value) => (int)Math.Round(value * 65536);

    private static byte[] Xyz(double x, double y, double z)
    {
        var bytes = new byte[20];
        Ascii("XYZ ").CopyTo(bytes, 0);
        WriteXyzNumber(bytes.AsSpan(8), x, y, z);
        return bytes;
    }

    private static byte[] Text(string text)
    {
        var bytes = new byte[8 + text.Length + 1];
        Ascii("text").CopyTo(bytes, 0);
        Ascii(text).CopyTo(bytes, 8);
        return bytes;
    }

    /// <summary>The version 2 <c>textDescriptionType</c>: ASCII, then empty Unicode and ScriptCode parts.</summary>
    private static byte[] TextDescription(string text)
    {
        var ascii = text.Length + 1;
        var bytes = new byte[12 + ascii + 8 + 3 + 67];
        Ascii("desc").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), (uint)ascii);
        Ascii(text).CopyTo(bytes, 12);
        return bytes;
    }

    /// <summary>IEC 61966-2-1's transfer function, sampled: linear near black, a 2.4 power above.</summary>
    private static byte[] Curve()
    {
        const int Points = 1024;
        var bytes = new byte[12 + Points * 2];
        Ascii("curv").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), Points);

        for (var i = 0; i < Points; i++)
        {
            var encoded = i / (double)(Points - 1);
            var linear = encoded <= 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12 + i * 2), (ushort)Math.Round(linear * 65535));
        }

        return bytes;
    }
}
