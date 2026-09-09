// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using PdfNet.Document;
using PdfNet.Objects;

namespace PdfNet.Content;

/// <summary>
/// A PDF function of one variable, which is the shape every shading needs.
/// </summary>
/// <remarks>
/// <para>
/// The format defines four kinds and a gradient may use any of them. Rather than teach a consumer
/// to tell them apart, this reduces all of them to one question — "what comes out at <c>t</c>" —
/// which is the only question a gradient asks.
/// </para>
/// <para>
/// Functions of more than one input exist and are used by shading types 1, 4 and higher. Those
/// shadings are not drawn, so the extra inputs would be untested code and are left out.
/// </para>
/// <para>
/// Type 4 is a small PostScript calculator language, and evaluating it means writing an interpreter
/// for that language. It is deliberately not supported: <see cref="Read"/> returns <c>null</c>, and
/// a caller falls back rather than drawing a gradient that is quietly the wrong colour.
/// </para>
/// </remarks>
public abstract class PdfFunction
{
    /// <summary>The input range the function is defined over.</summary>
    public double Domain0 { get; private init; } = 0;

    /// <summary>The upper end of the input range.</summary>
    public double Domain1 { get; private init; } = 1;

    /// <summary>Evaluates the function, clamping the input to its domain.</summary>
    public double[] Evaluate(double t) =>
        EvaluateCore(Math.Clamp(t, Math.Min(Domain0, Domain1), Math.Max(Domain0, Domain1)));

    /// <summary>Evaluates at an input already known to be inside the domain.</summary>
    protected abstract double[] EvaluateCore(double t);

    /// <summary>
    /// Reads a function, or an array of single-output functions acting as one.
    /// </summary>
    /// <returns><c>null</c> when the entry is missing or of a kind that is not supported.</returns>
    public static PdfFunction? Read(PdfObject? entry, PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var resolved = document.Follow(entry);

        // "/Function [f1 f2 f3]" is one function per output component, and is as common in the wild
        // as the single-function form. Presenting it as anything other than one function would push
        // the distinction onto every caller.
        if (resolved is PdfArray array)
        {
            var parts = new List<PdfFunction>();

            foreach (var item in array)
            {
                if (Read(item, document) is not { } part)
                {
                    return null;
                }

                parts.Add(part);
            }

            return parts.Count == 0
                ? null
                : new Combined(parts) { Domain0 = parts[0].Domain0, Domain1 = parts[0].Domain1 };
        }

        // A stream is a dictionary here — a stream-backed function keeps its parameters in its
        // own dictionary rather than in a separate one.
        var dictionary = resolved as PdfDictionary;

        if (dictionary is null)
        {
            return null;
        }

        var domain = Numbers(dictionary.Get(PdfName.Get("Domain")), document);
        var d0 = domain.Length > 0 ? domain[0] : 0;
        var d1 = domain.Length > 1 ? domain[1] : 1;

        return dictionary.GetInt(PdfName.Get("FunctionType"), -1) switch
        {
            0 when resolved is PdfStream sampled =>
                Sampled.Read(sampled, document, d0, d1),
            2 => new Exponential(
                Numbers(dictionary.Get(PdfName.Get("C0")), document) is { Length: > 0 } c0 ? c0 : [0],
                Numbers(dictionary.Get(PdfName.Get("C1")), document) is { Length: > 0 } c1 ? c1 : [1],
                dictionary.GetDouble(PdfName.Get("N"), 1))
            {
                Domain0 = d0,
                Domain1 = d1,
            },
            3 => Stitching.Read(dictionary, document, d0, d1),
            _ => null,
        };
    }

    /// <summary>Reads an array of numbers, following references.</summary>
    private protected static double[] Numbers(PdfObject? entry, PdfDocument document)
    {
        if (document.Follow(entry) is not PdfArray array)
        {
            return [];
        }

        var values = new double[array.Count];

        for (var i = 0; i < array.Count; i++)
        {
            values[i] = document.Follow(array[i]) is PdfNumber number ? number.DoubleValue : 0;
        }

        return values;
    }

    /// <summary>Linear interpolation, which every function type is defined in terms of.</summary>
    private protected static double Interpolate(double x, double from0, double from1, double to0,
        double to1) =>
        from1 == from0 ? to0 : to0 + ((x - from0) * (to1 - to0) / (from1 - from0));

    /// <summary>Type 2: one output that moves from C0 to C1 as t^N.</summary>
    private sealed class Exponential(double[] c0, double[] c1, double n) : PdfFunction
    {
        protected override double[] EvaluateCore(double t)
        {
            var count = Math.Max(c0.Length, c1.Length);
            var result = new double[count];

            // The domain is not always 0..1, and the exponent applies to the normalised position.
            var x = Interpolate(t, Domain0, Domain1, 0, 1);
            var factor = n == 1 ? x : Math.Pow(x, n);

            for (var i = 0; i < count; i++)
            {
                var a = i < c0.Length ? c0[i] : 0;
                var b = i < c1.Length ? c1[i] : 0;

                result[i] = a + (factor * (b - a));
            }

            return result;
        }
    }

    /// <summary>Type 3: a row of functions, each covering one stretch of the domain.</summary>
    private sealed class Stitching(PdfFunction[] functions, double[] bounds, double[] encode)
        : PdfFunction
    {
        public static PdfFunction? Read(PdfDictionary dictionary, PdfDocument document, double d0,
            double d1)
        {
            if (document.Follow(dictionary.Get(PdfName.Get("Functions"))) is not PdfArray array)
            {
                return null;
            }

            var functions = new PdfFunction[array.Count];

            for (var i = 0; i < array.Count; i++)
            {
                if (Read(array[i], document) is not { } function)
                {
                    return null;
                }

                functions[i] = function;
            }

            return functions.Length == 0
                ? null
                : new Stitching(
                    functions,
                    Numbers(dictionary.Get(PdfName.Get("Bounds")), document),
                    Numbers(dictionary.Get(PdfName.Get("Encode")), document))
                {
                    Domain0 = d0,
                    Domain1 = d1,
                };
        }

        protected override double[] EvaluateCore(double t)
        {
            // Which sub-function owns t. Bounds holds the interior edges only, so k sub-functions
            // have k-1 of them.
            var index = 0;

            while (index < bounds.Length && t >= bounds[index])
            {
                index++;
            }

            index = Math.Min(index, functions.Length - 1);

            var low = index == 0 ? Domain0 : bounds[index - 1];
            var high = index == bounds.Length ? Domain1 : bounds[index];

            // Encode remaps each stretch onto its sub-function's own domain, and reversing a pair
            // is how a producer mirrors one gradient stop against the next.
            var e0 = encode.Length > index * 2 ? encode[index * 2] : 0;
            var e1 = encode.Length > (index * 2) + 1 ? encode[(index * 2) + 1] : 1;

            return functions[index].Evaluate(Interpolate(t, low, high, e0, e1));
        }
    }

    /// <summary>An array of single-output functions presented as one multi-output function.</summary>
    private sealed class Combined(List<PdfFunction> parts) : PdfFunction
    {
        protected override double[] EvaluateCore(double t)
        {
            var result = new double[parts.Count];

            for (var i = 0; i < parts.Count; i++)
            {
                var value = parts[i].Evaluate(t);
                result[i] = value.Length > 0 ? value[0] : 0;
            }

            return result;
        }
    }

    /// <summary>Type 0: a table of samples, interpolated between.</summary>
    /// <remarks>
    /// The samples are packed at an arbitrary bit width — 1, 2, 4, 8, 12, 16, 24 or 32 — with no
    /// padding between them or between rows, so reading one means counting bits rather than bytes.
    /// </remarks>
    private sealed class Sampled(byte[] data, int size, int bits, int outputs, double[] encode,
        double[] decode, double[] range) : PdfFunction
    {
        public static PdfFunction? Read(PdfStream stream, PdfDocument document, double d0, double d1)
        {
            var dictionary = stream;
            var sizes = Numbers(dictionary.Get(PdfName.Get("Size")), document);
            var range = Numbers(dictionary.Get(PdfName.Get("Range")), document);

            // One input only, which is what a shading uses. A higher-dimensional sampled function
            // is legal but belongs to shading types this renderer does not draw.
            if (sizes.Length != 1 || range.Length < 2)
            {
                return null;
            }

            var size = (int)sizes[0];
            var bits = dictionary.GetInt(PdfName.Get("BitsPerSample"), 8);

            if (size < 1 || bits is not (1 or 2 or 4 or 8 or 12 or 16 or 24 or 32))
            {
                return null;
            }

            byte[] data;

            try
            {
                data = stream.Decoded;
            }
            catch (OfficeNet.Core.OfficeNetException)
            {
                return null;
            }

            var encode = Numbers(dictionary.Get(PdfName.Get("Encode")), document);
            var decode = Numbers(dictionary.Get(PdfName.Get("Decode")), document);

            return new Sampled(data, size, bits, range.Length / 2,
                encode.Length >= 2 ? encode : [0, size - 1],
                decode.Length >= range.Length ? decode : range,
                range)
            {
                Domain0 = d0,
                Domain1 = d1,
            };
        }

        protected override double[] EvaluateCore(double t)
        {
            var position = Math.Clamp(Interpolate(t, Domain0, Domain1, encode[0], encode[1]),
                0, size - 1);

            var low = (int)Math.Floor(position);
            var high = Math.Min(low + 1, size - 1);
            var fraction = position - low;

            var result = new double[outputs];
            var max = Math.Pow(2, bits) - 1;

            for (var i = 0; i < outputs; i++)
            {
                var a = SampleAt((low * outputs) + i) / max;
                var b = SampleAt((high * outputs) + i) / max;
                var value = a + (fraction * (b - a));

                var d0 = decode.Length > i * 2 ? decode[i * 2] : 0;
                var d1 = decode.Length > (i * 2) + 1 ? decode[(i * 2) + 1] : 1;

                result[i] = Math.Clamp(d0 + (value * (d1 - d0)),
                    Math.Min(range[i * 2], range[(i * 2) + 1]),
                    Math.Max(range[i * 2], range[(i * 2) + 1]));
            }

            return result;
        }

        /// <summary>Reads one sample by bit offset, big-endian within the stream.</summary>
        private double SampleAt(int index)
        {
            var bitOffset = (long)index * bits;
            var value = 0UL;

            for (var i = 0; i < bits; i++)
            {
                var at = bitOffset + i;
                var byteIndex = (int)(at >> 3);

                if (byteIndex >= data.Length)
                {
                    return 0;
                }

                var bit = (data[byteIndex] >> (7 - (int)(at & 7))) & 1;
                value = (value << 1) | (uint)bit;
            }

            return value;
        }
    }
}
