// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using PdfNet.Content;
using PdfNet.Document;
using PdfNet.Objects;
using Xunit;

namespace PdfNet.Tests;

/// <summary>
/// Covers PDF functions and the two shading types a gradient is actually written as.
/// </summary>
/// <remarks>
/// These live apart from the renderer on purpose. A gradient that comes out the wrong colour can be
/// a fault in the function arithmetic or in the drawing, and separating them is the difference
/// between a failing test that says which and one that says a page looks wrong.
/// </remarks>
public class FunctionAndShadingTests
{
    private static PdfDocument Document() => PdfDocument.Create();

    private static PdfDictionary Exponential(double[] c0, double[] c1, double n = 1) => new()
    {
        [PdfName.Get("FunctionType")] = new PdfNumber(2),
        [PdfName.Get("Domain")] = new PdfArray(0, 1),
        [PdfName.Get("C0")] = new PdfArray(c0),
        [PdfName.Get("C1")] = new PdfArray(c1),
        [PdfName.Get("N")] = new PdfNumber(n),
    };

    [Fact]
    public void AnExponentialFunctionRunsFromC0ToC1()
    {
        using var document = Document();

        var function = PdfFunction.Read(Exponential([0, 0, 0], [1, 0.5, 0.25]), document);

        Assert.NotNull(function);
        Assert.Equal([0, 0, 0], function!.Evaluate(0));
        Assert.Equal([1, 0.5, 0.25], function.Evaluate(1));

        var middle = function.Evaluate(0.5);
        Assert.Equal(0.5, middle[0], 6);
        Assert.Equal(0.25, middle[1], 6);
    }

    [Fact]
    public void TheExponentIsAppliedToThePositionNotTheColour()
    {
        // N=2 bends the ramp, and getting it the wrong way round — squaring the colour rather than
        // the position — happens to agree at both ends and disagree everywhere between.
        using var document = Document();

        var function = PdfFunction.Read(Exponential([0], [1], n: 2), document)!;

        Assert.Equal(0.25, function.Evaluate(0.5)[0], 6);
        Assert.Equal(0.0625, function.Evaluate(0.25)[0], 6);
    }

    [Fact]
    public void InputOutsideTheDomainIsClampedRatherThanExtrapolated()
    {
        // Extending a ramp past its ends is the shading's decision, not the function's, and a
        // function that extrapolates produces colours outside the range it declared.
        using var document = Document();

        var function = PdfFunction.Read(Exponential([0], [1]), document)!;

        Assert.Equal(0, function.Evaluate(-5)[0]);
        Assert.Equal(1, function.Evaluate(5)[0]);
    }

    [Fact]
    public void AStitchingFunctionHandsEachStretchToItsOwnSubFunction()
    {
        // Two ramps meeting at 0.5: black to white, then white to black. A reader that ignores
        // Encode gets the second half backwards, which looks like a plausible gradient.
        using var document = Document();

        var stitched = new PdfDictionary
        {
            [PdfName.Get("FunctionType")] = new PdfNumber(3),
            [PdfName.Get("Domain")] = new PdfArray(0, 1),
            [PdfName.Get("Functions")] = new PdfArray(
            [
                Exponential([0], [1]),
                Exponential([1], [0]),
            ]),
            [PdfName.Get("Bounds")] = new PdfArray(0.5),
            [PdfName.Get("Encode")] = new PdfArray(0, 1, 0, 1),
        };

        var function = PdfFunction.Read(stitched, document)!;

        Assert.Equal(0, function.Evaluate(0)[0], 6);
        Assert.Equal(1, function.Evaluate(0.499)[0], 2);
        Assert.Equal(1, function.Evaluate(0.5)[0], 2);
        Assert.Equal(0, function.Evaluate(1)[0], 6);
    }

    [Fact]
    public void ASampledFunctionInterpolatesBetweenItsSamples()
    {
        // Three 8-bit samples: 0, 128, 255. Between them the value has to move smoothly; a reader
        // that rounds to the nearest sample gives a visibly stepped gradient.
        using var document = Document();

        var sampled = new PdfStream();
        sampled[PdfName.Get("FunctionType")] = new PdfNumber(0);
        sampled[PdfName.Get("Domain")] = new PdfArray(0, 1);
        sampled[PdfName.Get("Range")] = new PdfArray(0, 1);
        sampled[PdfName.Get("Size")] = new PdfArray(3);
        sampled[PdfName.Get("BitsPerSample")] = new PdfNumber(8);
        sampled.SetDecoded([0, 128, 255], compress: false);

        var function = PdfFunction.Read(sampled, document)!;

        Assert.Equal(0, function.Evaluate(0)[0], 3);
        Assert.Equal(128 / 255.0, function.Evaluate(0.5)[0], 3);
        Assert.Equal(1, function.Evaluate(1)[0], 3);

        // A quarter of the way is halfway between the first two samples.
        Assert.Equal(64 / 255.0, function.Evaluate(0.25)[0], 3);
    }

    [Fact]
    public void APostScriptFunctionIsRefusedRatherThanGuessedAt()
    {
        // Type 4 is a programming language. Reading it as anything else would produce a gradient
        // that is confidently the wrong colour, which is worse than not drawing one.
        using var document = Document();

        var calculator = new PdfStream();
        calculator[PdfName.Get("FunctionType")] = new PdfNumber(4);
        calculator[PdfName.Get("Domain")] = new PdfArray(0, 1);
        calculator[PdfName.Get("Range")] = new PdfArray(0, 1);
        calculator.SetDecoded("{ dup mul }"u8.ToArray(), compress: false);

        Assert.Null(PdfFunction.Read(calculator, document));
    }

    [Fact]
    public void AnAxialShadingIsReadAsGeometryPlusARamp()
    {
        using var document = Document();

        var shading = PdfShading.Read(new PdfDictionary
        {
            [PdfName.Get("ShadingType")] = new PdfNumber(2),
            [PdfName.Get("ColorSpace")] = PdfName.Get("DeviceRGB"),
            [PdfName.Get("Coords")] = new PdfArray(10, 20, 110, 20),
            [PdfName.Get("Function")] = Exponential([1, 0, 0], [0, 0, 1]),
            [PdfName.Get("Extend")] = new PdfArray([PdfBoolean.True, PdfBoolean.False]),
        }, document);

        Assert.NotNull(shading);
        Assert.Equal(ShadingKind.Axial, shading!.Kind);
        Assert.Equal([10d, 20d, 110d, 20d], shading.Coords);
        Assert.True(shading.ExtendStart);
        Assert.False(shading.ExtendEnd);

        Assert.Equal(255, shading.Ramp[0].R);
        Assert.Equal(0, shading.Ramp[0].B);
        Assert.Equal(0, shading.Ramp[^1].R);
        Assert.Equal(255, shading.Ramp[^1].B);
    }

    [Fact]
    public void ACmykShadingIsConvertedRatherThanTakenAsRgb()
    {
        // Four components read as three gives a colour built from the wrong channels, and cyan read
        // as RGB comes out as a plausible blue rather than an obviously broken one.
        using var document = Document();

        var shading = PdfShading.Read(new PdfDictionary
        {
            [PdfName.Get("ShadingType")] = new PdfNumber(2),
            [PdfName.Get("ColorSpace")] = PdfName.Get("DeviceCMYK"),
            [PdfName.Get("Coords")] = new PdfArray(0, 0, 100, 0),
            [PdfName.Get("Function")] = Exponential([1, 0, 0, 0], [0, 0, 0, 1]),
        }, document)!;

        // Pure cyan at one end, pure black at the other.
        Assert.Equal(0, shading.Ramp[0].R);
        Assert.Equal(255, shading.Ramp[0].G);
        Assert.Equal(255, shading.Ramp[0].B);

        Assert.Equal(0, shading.Ramp[^1].R);
        Assert.Equal(0, shading.Ramp[^1].G);
        Assert.Equal(0, shading.Ramp[^1].B);
    }

    [Fact]
    public void AMeshShadingIsRefusedRatherThanFlattened()
    {
        // Types 4 to 7 are meshes. Drawing one as a linear ramp between its extreme colours is a
        // plausible-looking wrong answer, so it is not read at all.
        using var document = Document();

        foreach (var type in new[] { 1, 4, 5, 6, 7 })
        {
            Assert.Null(PdfShading.Read(new PdfDictionary
            {
                [PdfName.Get("ShadingType")] = new PdfNumber(type),
                [PdfName.Get("ColorSpace")] = PdfName.Get("DeviceRGB"),
                [PdfName.Get("Coords")] = new PdfArray(0, 0, 1, 1),
                [PdfName.Get("Function")] = Exponential([0], [1]),
            }, document));
        }
    }
}
