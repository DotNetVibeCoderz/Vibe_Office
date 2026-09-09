// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using System.Xml.Linq;
using OfficeNet.Core.Xml;

namespace PowerPointNet.Animations;

/// <summary>
/// Builds a slide's <c>p:timing</c> element.
/// </summary>
/// <remarks>
/// <para>
/// PresentationML's animation model is SMIL. A slide has one time root; under it sit sequences; under
/// each sequence sit the steps; under each step sit the behaviours that actually change something.
/// Even a single "fade in on click" is four levels of <c>p:par</c> before anything happens, and every
/// time node needs an id unique within the slide.
/// </para>
/// <para>
/// Two structural things are easy to get wrong and produce a file that opens with the animations
/// silently gone rather than an error:
/// </para>
/// <list type="bullet">
/// <item>
/// The <em>main sequence</em> holds everything triggered by clicking the slide. An animation
/// triggered by clicking a particular shape belongs in its own <c>p:seq</c> instead, with
/// <c>nodeType="interactiveSeq"</c> and a condition naming that shape. Putting it in the main
/// sequence makes it fire on the next slide click instead, which looks like the trigger being
/// ignored.
/// </item>
/// <item>
/// Within the main sequence, only an <c>OnClick</c> step opens a new click group. <c>WithPrevious</c>
/// and <c>AfterPrevious</c> join the group already open — that is what those words mean — and a
/// builder that gives each its own group turns a three-part build into three separate clicks.
/// </item>
/// </list>
/// </remarks>
internal static class TimingTree
{
    /// <summary>Builds the whole element, or returns <c>null</c> when there is nothing to animate.</summary>
    internal static XElement? Build(IReadOnlyList<Animation> animations)
    {
        if (animations.Count == 0)
        {
            return null;
        }

        // Node 1 is the time root by convention, and PowerPoint's own files always use it.
        var nodeId = 2;

        var sequences = new List<XElement>();

        var main = animations.Where(a => a.Trigger != AnimationTrigger.OnClickOf).ToList();

        if (main.Count > 0)
        {
            sequences.Add(MainSequence(main, ref nodeId));
        }

        // One interactive sequence per shape that triggers something, in the order first met.
        foreach (var group in animations
                     .Where(a => a.Trigger == AnimationTrigger.OnClickOf && a.TriggerShape is not null)
                     .GroupBy(a => a.TriggerShape!.Id))
        {
            sequences.Add(InteractiveSequence(group.Key, [.. group], ref nodeId));
        }

        if (sequences.Count == 0)
        {
            return null;
        }

        return new XElement(Ns.P + "timing",
            new XElement(Ns.P + "tnLst",
                new XElement(Ns.P + "par",
                    new XElement(Ns.P + "cTn",
                        new XAttribute("id", "1"),
                        new XAttribute("dur", "indefinite"),
                        new XAttribute("restart", "never"),
                        new XAttribute("nodeType", "tmRoot"),
                        new XElement(Ns.P + "childTnLst", sequences)))));
    }

    // ---- Sequences -----------------------------------------------------------------------------

    private static XElement MainSequence(IReadOnlyList<Animation> animations, ref int nodeId)
    {
        var groups = new XElement(Ns.P + "childTnLst");
        XElement? current = null;

        foreach (var animation in animations)
        {
            // Only a click opens a new group. With-previous and after-previous join the one already
            // open, which is the whole difference between a three-part build and three clicks.
            if (current is null || animation.Trigger == AnimationTrigger.OnClick)
            {
                current = ClickGroup(ref nodeId);
                groups.Add(current);
            }

            current.Element(Ns.P + "cTn")!
                .Element(Ns.P + "childTnLst")!
                .Add(Step(animation, ref nodeId, IsFirstOfGroup(current)));
        }

        return new XElement(Ns.P + "seq",
            new XAttribute("concurrent", "1"),
            new XAttribute("nextAc", "seek"),
            new XElement(Ns.P + "cTn",
                new XAttribute("id", nodeId++),
                new XAttribute("dur", "indefinite"),
                new XAttribute("nodeType", "mainSeq"),
                groups),
            // These two are what wire the sequence to the space bar and the arrow keys.
            Condition(Ns.P + "prevCondLst", "onPrev"),
            Condition(Ns.P + "nextCondLst", "onNext"));

        static bool IsFirstOfGroup(XElement group) =>
            !group.Element(Ns.P + "cTn")!.Element(Ns.P + "childTnLst")!.Elements().Any();
    }

    /// <summary>A click group: everything that happens on one press of the space bar.</summary>
    private static XElement ClickGroup(ref int nodeId) =>
        new(Ns.P + "par",
            new XElement(Ns.P + "cTn",
                new XAttribute("id", nodeId++),
                new XAttribute("fill", "hold"),
                new XElement(Ns.P + "stCondLst",
                    // "indefinite" is how a group says it waits for the click rather than a timer.
                    new XElement(Ns.P + "cond", new XAttribute("delay", "indefinite"))),
                new XElement(Ns.P + "childTnLst")));

    private static XElement InteractiveSequence(uint triggerShapeId,
        IReadOnlyList<Animation> animations, ref int nodeId)
    {
        var steps = new XElement(Ns.P + "childTnLst");
        var group = ClickGroup(ref nodeId);
        steps.Add(group);

        var first = true;

        foreach (var animation in animations)
        {
            group.Element(Ns.P + "cTn")!
                .Element(Ns.P + "childTnLst")!
                .Add(Step(animation, ref nodeId, first));

            first = false;
        }

        return new XElement(Ns.P + "seq",
            new XAttribute("concurrent", "1"),
            new XAttribute("nextAc", "seek"),
            new XElement(Ns.P + "cTn",
                new XAttribute("id", nodeId++),
                new XAttribute("restart", "whenNotActive"),
                new XAttribute("fill", "hold"),
                new XAttribute("nodeType", "interactiveSeq"),
                new XElement(Ns.P + "stCondLst",
                    // The condition names the shape. Without it the sequence never starts, and the
                    // trigger looks like it was ignored.
                    new XElement(Ns.P + "cond",
                        new XAttribute("evt", "onClick"),
                        new XAttribute("delay", "0"),
                        new XElement(Ns.P + "tgtEl",
                            new XElement(Ns.P + "spTgt",
                                new XAttribute("spid", triggerShapeId))))),
                new XElement(Ns.P + "endSync",
                    new XAttribute("evt", "end"),
                    new XAttribute("delay", "0"),
                    new XElement(Ns.P + "tgtEl",
                        new XElement(Ns.P + "sldTgt"))),
                steps),
            Condition(Ns.P + "prevCondLst", "onPrev"),
            Condition(Ns.P + "nextCondLst", "onNext"));
    }

    private static XElement Condition(XName list, string @event) =>
        new(list,
            new XElement(Ns.P + "cond",
                new XAttribute("evt", @event),
                new XAttribute("delay", "0"),
                new XElement(Ns.P + "tgtEl",
                    new XElement(Ns.P + "sldTgt"))));

    // ---- One animation -------------------------------------------------------------------------

    /// <summary>
    /// Wraps one animation's behaviours in the time node that schedules them.
    /// </summary>
    /// <remarks>
    /// <c>firstOfGroup</c> says whether this is the first animation of its click group. The first
    /// waits for the click and the rest start relative to it, which is how <c>WithPrevious</c> and
    /// <c>AfterPrevious</c> differ once the group has already begun.
    /// </remarks>
    private static XElement Step(Animation animation, ref int nodeId, bool firstOfGroup)
    {
        var delay = (int)animation.Delay.TotalMilliseconds;

        var nodeType = animation.Trigger switch
        {
            AnimationTrigger.WithPrevious when !firstOfGroup => "withEffect",
            AnimationTrigger.AfterPrevious when !firstOfGroup => "afterEffect",
            AnimationTrigger.OnClickOf => "clickEffect",
            _ => "clickEffect",
        };

        // afterEffect starts when the previous one ends, which SMIL expresses as a delay measured
        // from the group's start rather than as a reference to the previous node.
        var start = nodeType == "afterEffect" ? "0" : delay.ToString(CultureInfo.InvariantCulture);

        var inner = new XElement(Ns.P + "childTnLst");

        var node = new XElement(Ns.P + "par",
            new XElement(Ns.P + "cTn",
                new XAttribute("id", nodeId++),
                new XAttribute("presetClass", PresetClass(animation.Kind)),
                new XAttribute("fill", animation.Kind == AnimationKind.Emphasis ? "remove" : "hold"),
                new XAttribute("nodeType", nodeType),
                PresetId(animation) is { } preset ? new XAttribute("presetID", preset) : null,
                new XElement(Ns.P + "stCondLst",
                    new XElement(Ns.P + "cond", new XAttribute("delay", start))),
                inner));

        // An entrance has to make the shape visible first, and an exit has to hide it at the end, or
        // the effect plays against a shape that was already in its final state.
        if (animation.Kind == AnimationKind.Entrance)
        {
            inner.Add(Visibility(animation.Shape.Id, "visible", ref nodeId));
        }

        foreach (var behaviour in Behaviours(animation, ref nodeId))
        {
            inner.Add(behaviour);
        }

        if (animation.Kind == AnimationKind.Exit)
        {
            inner.Add(Visibility(animation.Shape.Id, "hidden", ref nodeId));
        }

        return node;
    }

    private static string PresetClass(AnimationKind kind) => kind switch
    {
        AnimationKind.Emphasis => "emph",
        AnimationKind.Exit => "exit",
        AnimationKind.MotionPath => "path",
        _ => "entr",
    };

    /// <summary>
    /// PowerPoint's own preset number, where it is known.
    /// </summary>
    /// <remarks>
    /// The preset id tells PowerPoint's animation pane which entry in its gallery this is; it does
    /// <em>not</em> decide what plays, which comes entirely from the behaviour elements below. So a
    /// preset that is not one of the documented entrance and exit numbers is left off rather than
    /// guessed: PowerPoint then labels the effect "Custom" and plays it exactly as written, which is
    /// a better outcome than a wrong label on an effect the user cannot then edit sensibly.
    /// </remarks>
    private static int? PresetId(Animation animation) =>
        animation.Kind is AnimationKind.Entrance or AnimationKind.Exit
            ? animation.Effect switch
            {
                AnimationEffectKind.Appear => 1,
                AnimationEffectKind.Fly => 2,
                AnimationEffectKind.Fade => 10,
                AnimationEffectKind.Wipe => 22,
                AnimationEffectKind.Zoom => 23,
                _ => null,
            }
            : null;

    /// <summary>Flips a shape's visibility, held for the rest of the slide.</summary>
    private static XElement Visibility(uint shapeId, string value, ref int nodeId) =>
        new(Ns.P + "set",
            new XElement(Ns.P + "cBhvr",
                new XElement(Ns.P + "cTn",
                    new XAttribute("id", nodeId++),
                    new XAttribute("dur", "1"),
                    new XAttribute("fill", "hold")),
                Target(shapeId),
                new XElement(Ns.P + "attrNameLst",
                    new XElement(Ns.P + "attrName", "style.visibility"))),
            new XElement(Ns.P + "to",
                new XElement(Ns.P + "strVal", new XAttribute("val", value))));

    private static XElement Target(uint shapeId) =>
        new(Ns.P + "tgtEl",
            new XElement(Ns.P + "spTgt", new XAttribute("spid", shapeId)));

    /// <summary>
    /// The elements that actually change something.
    /// </summary>
    /// <remarks>
    /// This is where the work happens. <c>p:animEffect</c> applies a named filter, <c>p:animRot</c>
    /// turns, <c>p:animScale</c> resizes, and <c>p:animMotion</c> moves — and PowerPoint plays what
    /// these say regardless of what the preset id claims.
    /// </remarks>
    private static List<XElement> Behaviours(Animation animation, ref int nodeId)
    {
        var duration = Math.Max(1, (int)animation.Duration.TotalMilliseconds);
        var shapeId = animation.Shape.Id;
        var entering = animation.Kind == AnimationKind.Entrance;

        switch (animation.Kind)
        {
            case AnimationKind.MotionPath:
                return [MotionElement(animation.Path!.Data, shapeId, duration, 0, ref nodeId)];

            case AnimationKind.Emphasis when animation.Effect == AnimationEffectKind.Spin:
                return [Rotation(shapeId, duration, ref nodeId)];

            case AnimationKind.Emphasis when animation.Effect == AnimationEffectKind.Grow:
                return [Scale(shapeId, duration, 150_000, ref nodeId)];

            case AnimationKind.Emphasis:
            {
                // A pulse is a grow and a shrink back, so it is two behaviours and not one.
                var half = Math.Max(1, duration / 2);
                var grow = Scale(shapeId, half, 110_000, ref nodeId);
                var shrink = Scale(shapeId, half, 100_000, ref nodeId, delay: half);

                return [grow, shrink];
            }
        }

        if (animation.Effect == AnimationEffectKind.Appear)
        {
            // Nothing to animate: the visibility flip around this is the whole effect.
            return [];
        }

        if (animation.Effect == AnimationEffectKind.Fly)
        {
            return [FlyMotion(animation, shapeId, duration, entering, ref nodeId)];
        }

        return
        [
            new XElement(Ns.P + "animEffect",
                new XAttribute("transition", entering ? "in" : "out"),
                new XAttribute("filter", Filter(animation, entering)),
                new XElement(Ns.P + "cBhvr",
                    new XElement(Ns.P + "cTn",
                        new XAttribute("id", nodeId++),
                        new XAttribute("dur", duration.ToString(CultureInfo.InvariantCulture))),
                    Target(shapeId))),
        ];
    }

    private static string Filter(Animation animation, bool entering) => animation.Effect switch
    {
        AnimationEffectKind.Wipe => $"wipe({Edge(animation.Direction, entering)})",
        AnimationEffectKind.Zoom => "fade",
        _ => "fade",
    };

    /// <summary>
    /// Which edge a directional effect uses.
    /// </summary>
    /// <remarks>
    /// An entrance comes <em>from</em> the named edge and an exit goes <em>to</em> it, but the filter
    /// names the edge the effect moves towards either way. So the direction is flipped on the way in
    /// and not on the way out, which is the opposite of what reading the attribute name suggests.
    /// </remarks>
    private static string Edge(AnimationDirection direction, bool entering)
    {
        var effective = entering ? Opposite(direction) : direction;

        return effective switch
        {
            AnimationDirection.Top => "up",
            AnimationDirection.Left => "left",
            AnimationDirection.Right => "right",
            _ => "down",
        };
    }

    private static AnimationDirection Opposite(AnimationDirection direction) => direction switch
    {
        AnimationDirection.Top => AnimationDirection.Bottom,
        AnimationDirection.Bottom => AnimationDirection.Top,
        AnimationDirection.Left => AnimationDirection.Right,
        _ => AnimationDirection.Left,
    };

    /// <summary>A fly, expressed as a motion from or to just off the slide.</summary>
    private static XElement FlyMotion(Animation animation, uint shapeId, int duration,
        bool entering, ref int nodeId)
    {
        var (x, y) = animation.Direction switch
        {
            AnimationDirection.Top => (0.0, -1.0),
            AnimationDirection.Left => (-1.0, 0.0),
            AnimationDirection.Right => (1.0, 0.0),
            _ => (0.0, 1.0),
        };

        // Entering means starting off the slide and arriving; exiting is the reverse.
        var path = entering
            ? $"M {Coordinate(-x)} {Coordinate(-y)} L 0 0 E"
            : $"M 0 0 L {Coordinate(x)} {Coordinate(y)} E";

        return MotionElement(path, shapeId, duration, 0, ref nodeId);
    }

    /// <summary>Formats a path coordinate.</summary>
    /// <remarks>
    /// Negating a zero gives negative zero, which formats as "-0" — legal to a parser that reads it
    /// as a number and noise to anyone reading the file. Adding zero folds it back.
    /// </remarks>
    private static string Coordinate(double value) =>
        (value + 0).ToString("0.#####", CultureInfo.InvariantCulture);

    private static XElement MotionElement(string path, uint shapeId, int duration, int delay,
        ref int nodeId) =>
        new(Ns.P + "animMotion",
            new XAttribute("origin", "layout"),
            new XAttribute("path", path),
            // Relative, because the path's coordinates are offsets from where the shape already is.
            // The absolute mode measures from the slide's corner and sends the shape somewhere else.
            new XAttribute("pathEditMode", "relative"),
            new XAttribute("rAng", "0"),
            new XAttribute("ptsTypes", string.Empty),
            new XElement(Ns.P + "cBhvr",
                new XElement(Ns.P + "cTn",
                    new XAttribute("id", nodeId++),
                    new XAttribute("dur", duration.ToString(CultureInfo.InvariantCulture)),
                    delay == 0
                        ? null
                        : new XElement(Ns.P + "stCondLst",
                            new XElement(Ns.P + "cond",
                                new XAttribute("delay", delay.ToString(CultureInfo.InvariantCulture)))),
                    new XAttribute("fill", "hold")),
                Target(shapeId),
                // ppt_x and ppt_y are the shape's position in PowerPoint's own coordinate space.
                // Without naming them the motion has nothing to drive and the shape stays put.
                new XElement(Ns.P + "attrNameLst",
                    new XElement(Ns.P + "attrName", "ppt_x"),
                    new XElement(Ns.P + "attrName", "ppt_y"))),
            new XElement(Ns.P + "rCtr",
                new XAttribute("x", "0"),
                new XAttribute("y", "0")));

    private static XElement Rotation(uint shapeId, int duration, ref int nodeId) =>
        new(Ns.P + "animRot",
            new XAttribute("by", "21600000"),   // 360 degrees, in sixtieth-thousandths
            new XElement(Ns.P + "cBhvr",
                new XElement(Ns.P + "cTn",
                    new XAttribute("id", nodeId++),
                    new XAttribute("dur", duration.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("fill", "hold")),
                Target(shapeId),
                new XElement(Ns.P + "attrNameLst",
                    new XElement(Ns.P + "attrName", "r"))));

    private static XElement Scale(uint shapeId, int duration, int percent, ref int nodeId,
        int delay = 0)
    {
        var value = percent.ToString(CultureInfo.InvariantCulture);

        return new XElement(Ns.P + "animScale",
            new XElement(Ns.P + "cBhvr",
                new XElement(Ns.P + "cTn",
                    new XAttribute("id", nodeId++),
                    new XAttribute("dur", Math.Max(1, duration).ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("fill", "hold"),
                    delay == 0
                        ? null
                        : new XElement(Ns.P + "stCondLst",
                            new XElement(Ns.P + "cond",
                                new XAttribute("delay", delay.ToString(CultureInfo.InvariantCulture))))),
                Target(shapeId)),
            // Scale is in thousandths of a percent, so 100% is 100000 and not 100.
            new XElement(Ns.P + "to",
                new XAttribute("x", value),
                new XAttribute("y", value)));
    }
}
