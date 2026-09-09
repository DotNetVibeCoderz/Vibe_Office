// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Xml.Linq;
using OfficeNet.Core;
using OfficeNet.Core.Xml;
using PowerPointNet.Animations;
using PowerPointNet.Shapes;
using Xunit;

namespace PowerPointNet.Tests;

public class AnimationTests
{
    private static Presentation Deck(out Slide slide, out Shape[] shapes, int count = 3)
    {
        var deck = Presentation.Create();
        var page = deck.AddSlide(3);

        shapes = [.. Enumerable.Range(0, count).Select(i =>
            page.AddTextBox($"Kotak {i}", Units.Cm(1), Units.Cm(1 + (i * 2)),
                Units.Cm(8), Units.Cm(1.5)))];

        slide = page;
        return deck;
    }

    private static XElement Timing(Slide slide) =>
        slide.Root.Element(Ns.P + "timing")
        ?? throw new OfficeNetException("The slide has no timing tree.");

    private static IEnumerable<XElement> ClickGroups(Slide slide) =>
        Timing(slide).Descendants(Ns.P + "seq")
            .Single(s => s.Element(Ns.P + "cTn")?.Attr("nodeType") == "mainSeq")
            .Element(Ns.P + "cTn")!
            .Element(Ns.P + "childTnLst")!
            .Elements(Ns.P + "par");

    private static byte[] Save(Presentation deck)
    {
        using var stream = new MemoryStream();
        deck.Save(stream);
        return stream.ToArray();
    }

    [Fact]
    public void AnAnimatedSlideIsAValidPresentation()
    {
        using var deck = Deck(out var slide, out var shapes);

        slide.Animate(
            Animation.Entrance(shapes[0], AnimationEffectKind.Fade),
            Animation.Entrance(shapes[1], AnimationEffectKind.Fly).From(AnimationDirection.Left),
            Animation.Emphasis(shapes[2], AnimationEffectKind.Spin));

        PptxValidator.AssertValid(Save(deck));
        Assert.True(slide.HasAnimations);
    }

    [Fact]
    public void OnlyAClickOpensANewGroup()
    {
        // This is what WithPrevious and AfterPrevious mean. A builder that gives each its own group
        // turns a three-part build into three separate clicks, and nothing about the file says so.
        using var deck = Deck(out var slide, out var shapes);

        slide.Animate(
            Animation.Entrance(shapes[0]),
            Animation.Entrance(shapes[1]).WithPrevious(),
            Animation.Entrance(shapes[2]).AfterPrevious());

        Assert.Single(ClickGroups(slide));

        slide.Animate(
            Animation.Entrance(shapes[0]),
            Animation.Entrance(shapes[1]),
            Animation.Entrance(shapes[2]));

        Assert.Equal(3, ClickGroups(slide).Count());
    }

    [Fact]
    public void TheTriggerBecomesTheNodeType()
    {
        using var deck = Deck(out var slide, out var shapes);

        slide.Animate(
            Animation.Entrance(shapes[0]),
            Animation.Entrance(shapes[1]).WithPrevious(),
            Animation.Entrance(shapes[2]).AfterPrevious());

        var steps = ClickGroups(slide).Single()
            .Element(Ns.P + "cTn")!
            .Element(Ns.P + "childTnLst")!
            .Elements(Ns.P + "par")
            .Select(p => p.Element(Ns.P + "cTn")!.Attr("nodeType"))
            .ToList();

        Assert.Equal(["clickEffect", "withEffect", "afterEffect"], steps);
    }

    [Fact]
    public void TheFirstOfAGroupIsAlwaysAClickEffect()
    {
        // WithPrevious on the first animation has nothing to be "with". Writing withEffect there
        // gives a group that never starts, because nothing in it waits for the click.
        using var deck = Deck(out var slide, out var shapes);

        slide.Animate(Animation.Entrance(shapes[0]).WithPrevious());

        var step = ClickGroups(slide).Single()
            .Element(Ns.P + "cTn")!
            .Element(Ns.P + "childTnLst")!
            .Elements(Ns.P + "par")
            .Single();

        Assert.Equal("clickEffect", step.Element(Ns.P + "cTn")!.Attr("nodeType"));
    }

    [Fact]
    public void ATriggeredAnimationGoesInItsOwnSequence()
    {
        // In the main sequence it would fire on the next slide click instead, which looks exactly
        // like the trigger being ignored.
        using var deck = Deck(out var slide, out var shapes);

        slide.Animate(
            Animation.Entrance(shapes[0]),
            Animation.Entrance(shapes[1]).OnClickOf(shapes[2]));

        var sequences = Timing(slide).Descendants(Ns.P + "seq").ToList();

        Assert.Equal(2, sequences.Count);

        var interactive = sequences.Single(s =>
            s.Element(Ns.P + "cTn")?.Attr("nodeType") == "interactiveSeq");

        // And it has to name the shape whose click starts it.
        Assert.Equal(shapes[2].Id.ToString(),
            interactive.Descendants(Ns.P + "spTgt").First().Attr("spid"));

        // The main sequence keeps only the one animation that belongs to it.
        Assert.Single(ClickGroups(slide));
    }

    [Fact]
    public void SeveralAnimationsOnOneTriggerShareOneSequence()
    {
        using var deck = Deck(out var slide, out var shapes, count: 4);

        slide.Animate(
            Animation.Entrance(shapes[0]).OnClickOf(shapes[3]),
            Animation.Entrance(shapes[1]).OnClickOf(shapes[3]),
            Animation.Entrance(shapes[2]).OnClickOf(shapes[0]));

        var interactive = Timing(slide).Descendants(Ns.P + "seq")
            .Where(s => s.Element(Ns.P + "cTn")?.Attr("nodeType") == "interactiveSeq")
            .ToList();

        Assert.Equal(2, interactive.Count);
    }

    [Fact]
    public void AnEntranceRevealsTheShapeAndAnExitHidesIt()
    {
        // Without the visibility flip an entrance plays against a shape that was already visible,
        // and an exit leaves one behind that should have gone.
        using var deck = Deck(out var slide, out var shapes);

        slide.Animate(
            Animation.Entrance(shapes[0]),
            Animation.Exit(shapes[1]));

        var sets = Timing(slide).Descendants(Ns.P + "set")
            .Select(s => s.Element(Ns.P + "to")!.Element(Ns.P + "strVal")!.Attr("val"))
            .ToList();

        Assert.Equal(["visible", "hidden"], sets);
    }

    [Fact]
    public void AnEmphasisTouchesVisibilityAtAll()
    {
        // An emphasis assumes the shape is already there. Flipping its visibility would make it
        // appear on the click, which is an entrance and not an emphasis.
        using var deck = Deck(out var slide, out var shapes);

        slide.Animate(Animation.Emphasis(shapes[0], AnimationEffectKind.Pulse));

        Assert.Empty(Timing(slide).Descendants(Ns.P + "set"));
    }

    [Fact]
    public void EachClassGetsItsOwnPresetClass()
    {
        using var deck = Deck(out var slide, out var shapes, count: 4);

        slide.Animate(
            Animation.Entrance(shapes[0]),
            Animation.Emphasis(shapes[1]),
            Animation.Exit(shapes[2]),
            Animation.Motion(shapes[3], MotionPath.Line(0.2, 0)));

        var classes = Timing(slide).Descendants(Ns.P + "cTn")
            .Select(c => c.Attr("presetClass"))
            .Where(c => c is not null)
            .ToList();

        Assert.Equal(["entr", "emph", "exit", "path"], classes);
    }

    [Fact]
    public void ASpinTurnsAndAPulseScalesTwice()
    {
        using var deck = Deck(out var slide, out var shapes);

        slide.Animate(
            Animation.Emphasis(shapes[0], AnimationEffectKind.Spin),
            Animation.Emphasis(shapes[1], AnimationEffectKind.Pulse));

        var rotation = Assert.Single(Timing(slide).Descendants(Ns.P + "animRot"));

        // 360 degrees, in sixtieth-thousandths.
        Assert.Equal("21600000", rotation.Attr("by"));
        Assert.Equal("r", rotation.Descendants(Ns.P + "attrName").Single().Value);

        var scales = Timing(slide).Descendants(Ns.P + "animScale").ToList();

        // Grow, then shrink back. One alone leaves the shape permanently larger.
        Assert.Equal(2, scales.Count);
        Assert.Equal("110000", scales[0].Element(Ns.P + "to")!.Attr("x"));
        Assert.Equal("100000", scales[1].Element(Ns.P + "to")!.Attr("x"));
    }

    [Fact]
    public void AMotionPathNamesTheAttributesItDrives()
    {
        // Without ppt_x and ppt_y the motion has nothing to drive and the shape stays exactly where
        // it was, with no error anywhere.
        using var deck = Deck(out var slide, out var shapes);

        slide.Animate(Animation.Motion(shapes[0], MotionPath.Line(0.3, -0.1)));

        var motion = Assert.Single(Timing(slide).Descendants(Ns.P + "animMotion"));

        Assert.Equal("M 0 0 L 0.3 -0.1 E", motion.Attr("path"));
        Assert.Equal(["ppt_x", "ppt_y"],
            motion.Descendants(Ns.P + "attrName").Select(a => a.Value));

        // Relative, because the coordinates are offsets from where the shape already is. Absolute
        // measures from the slide's corner and sends the shape somewhere else entirely.
        Assert.Equal("relative", motion.Attr("pathEditMode"));
    }

    [Theory]
    [InlineData("M 0 0 L 0.3 -0.1 E")]
    [InlineData("M 0 0 L 0.3 -0.1")]
    public void APathAlwaysEndsWithE(string data)
    {
        // PowerPoint needs the terminator, and a path without it is one of the ways a slide opens
        // with the animation quietly dropped.
        Assert.EndsWith("E", MotionPath.Custom(data).Data, StringComparison.Ordinal);
    }

    [Fact]
    public void APathThroughSeveralPointsKeepsThemInOrder()
    {
        Assert.Equal("M 0 0 L 0.1 0 L 0.1 0.2 L 0 0.2 E",
            MotionPath.Through((0.1, 0), (0.1, 0.2), (0, 0.2)).Data);

        Assert.Equal("M 0 0 L 0.25 0 L 0.25 0.1 L 0 0.1 Z E",
            MotionPath.Rectangle(0.25, 0.1).Data);
    }

    [Fact]
    public void APathWithNoPointsIsRefused()
    {
        var exception = Assert.Throws<OfficeNetException>(() => MotionPath.Through());
        Assert.Contains("at least one point", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFlyEntersFromTheEdgeItNames()
    {
        // Entering means starting off the slide and arriving; exiting is the reverse. Getting the
        // sign wrong sends an entrance out of the slide before it arrives.
        using var deck = Deck(out var slide, out var shapes);

        slide.Animate(
            Animation.Entrance(shapes[0], AnimationEffectKind.Fly).From(AnimationDirection.Left),
            Animation.Exit(shapes[1], AnimationEffectKind.Fly).From(AnimationDirection.Right));

        var paths = Timing(slide).Descendants(Ns.P + "animMotion")
            .Select(m => m.Attr("path"))
            .ToList();

        Assert.Equal("M 1 0 L 0 0 E", paths[0]);
        Assert.Equal("M 0 0 L 1 0 E", paths[1]);
    }

    [Fact]
    public void AnAppearHasNothingToAnimate()
    {
        // The visibility flip is the whole effect. A zero-length fade next to it would be a
        // behaviour that does nothing and shows up in PowerPoint's pane as a second effect.
        using var deck = Deck(out var slide, out var shapes);

        slide.Animate(Animation.Entrance(shapes[0], AnimationEffectKind.Appear));

        Assert.Empty(Timing(slide).Descendants(Ns.P + "animEffect"));
        Assert.Single(Timing(slide).Descendants(Ns.P + "set"));
    }

    [Fact]
    public void EveryTimeNodeIdIsUniqueWithinTheSlide()
    {
        // PowerPoint tolerates a duplicate in some versions and reports the file as corrupt in
        // others, which makes it the kind of bug that only appears on someone else's machine.
        using var deck = Deck(out var slide, out var shapes, count: 5);

        slide.Animate(
            Animation.Entrance(shapes[0]),
            Animation.Entrance(shapes[1]).WithPrevious(),
            Animation.Emphasis(shapes[2], AnimationEffectKind.Pulse).AfterPrevious(),
            Animation.Exit(shapes[3]),
            Animation.Motion(shapes[4], MotionPath.Rectangle(0.2, 0.2)).OnClickOf(shapes[0]));

        var ids = Timing(slide).Descendants(Ns.P + "cTn")
            .Select(c => c.Attr("id"))
            .ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Contains("1", ids);
    }

    [Fact]
    public void DurationAndDelayReachTheFile()
    {
        using var deck = Deck(out var slide, out var shapes);

        slide.Animate(Animation.Entrance(shapes[0])
            .Lasting(TimeSpan.FromMilliseconds(1200))
            .After(TimeSpan.FromMilliseconds(300)));

        var step = ClickGroups(slide).Single()
            .Element(Ns.P + "cTn")!
            .Element(Ns.P + "childTnLst")!
            .Elements(Ns.P + "par")
            .Single();

        Assert.Equal("300",
            step.Element(Ns.P + "cTn")!.Element(Ns.P + "stCondLst")!
                .Element(Ns.P + "cond")!.Attr("delay"));

        Assert.Equal("1200",
            step.Descendants(Ns.P + "animEffect").Single()
                .Element(Ns.P + "cBhvr")!.Element(Ns.P + "cTn")!.Attr("dur"));
    }

    [Fact]
    public void AnIllegalPairingIsRefusedUpFront()
    {
        // PowerPoint opens a file that says otherwise and plays nothing, which is much harder to
        // diagnose than an exception at the call site.
        using var deck = Deck(out _, out var shapes);

        Assert.Throws<OfficeNetException>(() =>
            Animation.Entrance(shapes[0], AnimationEffectKind.Spin));

        Assert.Throws<OfficeNetException>(() =>
            Animation.Emphasis(shapes[0], AnimationEffectKind.Wipe));

        Assert.Throws<OfficeNetException>(() =>
            Animation.Exit(shapes[0], AnimationEffectKind.Move));
    }

    [Fact]
    public void AnimatingAgainReplacesRatherThanAdds()
    {
        using var deck = Deck(out var slide, out var shapes);

        slide.Animate(Animation.Entrance(shapes[0]));
        slide.Animate(Animation.Entrance(shapes[1]), Animation.Entrance(shapes[2]));

        Assert.Single(slide.Root.Elements(Ns.P + "timing"));
        Assert.Equal(2, ClickGroups(slide).Count());

        slide.ClearAnimations();

        Assert.False(slide.HasAnimations);
        Assert.Empty(slide.Root.Elements(Ns.P + "timing"));
    }

    [Fact]
    public void TheOlderCallStillDoesWhatItDid()
    {
        using var deck = Deck(out var slide, out var shapes);

        slide.AnimateOnClick(AnimationEffect.Fade, shapes);

        Assert.Equal(3, ClickGroups(slide).Count());
        Assert.Equal(3, Timing(slide).Descendants(Ns.P + "animEffect").Count());

        slide.AnimateOnClick(AnimationEffect.None, shapes);

        Assert.False(slide.HasAnimations);
    }

    [Fact]
    public void AnimationsSurviveASaveAndReopen()
    {
        byte[] bytes;

        using (var deck = Deck(out var slide, out var shapes))
        {
            slide.Animate(
                Animation.Entrance(shapes[0]),
                Animation.Motion(shapes[1], MotionPath.Line(0.4, 0.1)).OnClickOf(shapes[2]));

            bytes = Save(deck);
        }

        using var reopened = Presentation.Open(new MemoryStream(bytes, writable: false));
        var reloaded = reopened.Slides[0];

        Assert.True(reloaded.HasAnimations);
        Assert.Equal(2, Timing(reloaded).Descendants(Ns.P + "seq").Count());
        Assert.Equal("M 0 0 L 0.4 0.1 E",
            Timing(reloaded).Descendants(Ns.P + "animMotion").Single().Attr("path"));
    }
}
