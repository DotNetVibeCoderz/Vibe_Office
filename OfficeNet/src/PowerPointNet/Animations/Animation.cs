// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using System.Globalization;
using OfficeNet.Core;
using PowerPointNet.Shapes;

namespace PowerPointNet.Animations;

/// <summary>What an animation does to a shape.</summary>
/// <remarks>
/// The four classes are PowerPoint's own, and they are not interchangeable: an entrance leaves the
/// shape visible, an exit leaves it hidden, and an emphasis assumes it was visible to begin with.
/// Choosing the wrong one gives an animation that plays and leaves the slide in the wrong state.
/// </remarks>
public enum AnimationKind
{
    /// <summary>The shape arrives. It is hidden until its turn.</summary>
    Entrance,

    /// <summary>The shape, already visible, draws attention to itself.</summary>
    Emphasis,

    /// <summary>The shape leaves, and stays gone.</summary>
    Exit,

    /// <summary>The shape moves along a path.</summary>
    MotionPath,
}

/// <summary>What starts an animation.</summary>
public enum AnimationTrigger
{
    /// <summary>The next click. Each one waits its turn in the sequence.</summary>
    OnClick,

    /// <summary>The same moment as the animation before it.</summary>
    WithPrevious,

    /// <summary>As soon as the animation before it finishes.</summary>
    AfterPrevious,

    /// <summary>
    /// A click on some other shape, rather than anywhere on the slide.
    /// </summary>
    /// <remarks>
    /// This is what makes a slide interactive — a button that reveals an answer. It lives in a
    /// separate sequence from the main one, keyed to the shape that was clicked, which is why
    /// <see cref="Animation.TriggerShape"/> has to be set for it and why such an animation does not
    /// take a turn in the click order.
    /// </remarks>
    OnClickOf,
}

/// <summary>
/// The effects an animation can apply.
/// </summary>
/// <remarks>
/// Which of these are legal depends on the <see cref="AnimationKind"/>: <c>Spin</c> is an emphasis
/// and not an entrance, <c>Appear</c> an entrance and not an emphasis. <see cref="Animation"/>
/// checks the pairing rather than writing a file PowerPoint opens and ignores.
/// </remarks>
public enum AnimationEffectKind
{
    /// <summary>Instant. An entrance or an exit.</summary>
    Appear,

    /// <summary>A fade. An entrance or an exit.</summary>
    Fade,

    /// <summary>A fly from or to an edge. An entrance or an exit.</summary>
    Fly,

    /// <summary>A wipe. An entrance or an exit.</summary>
    Wipe,

    /// <summary>A zoom. An entrance or an exit.</summary>
    Zoom,

    /// <summary>A grow and shrink back. An emphasis.</summary>
    Pulse,

    /// <summary>A full turn. An emphasis.</summary>
    Spin,

    /// <summary>A grow that stays grown. An emphasis.</summary>
    Grow,

    /// <summary>Movement along a path. Only for <see cref="AnimationKind.MotionPath"/>.</summary>
    Move,
}

/// <summary>Which edge a fly or wipe comes from.</summary>
public enum AnimationDirection
{
    /// <summary>From or to the bottom.</summary>
    Bottom,

    /// <summary>From or to the top.</summary>
    Top,

    /// <summary>From or to the left.</summary>
    Left,

    /// <summary>From or to the right.</summary>
    Right,
}

/// <summary>
/// A path a shape moves along, in fractions of the slide.
/// </summary>
/// <remarks>
/// <para>
/// PowerPoint stores a motion path in its own coordinate space: 0 to 1 across the slide and down it,
/// <b>relative to where the shape already is</b>. So <c>0.25</c> means a quarter of the slide's
/// width from the shape's own position, not a quarter of the way across the slide. Reading it as an
/// absolute position is why a hand-written motion path so often sends the shape off the edge.
/// </para>
/// <para>
/// The path string itself is a cut-down SVG: <c>M</c> to start, <c>L</c> to line, <c>C</c> to curve,
/// <c>Z</c> to close, and a final <c>E</c> that marks the end and that PowerPoint requires.
/// </para>
/// </remarks>
public sealed record MotionPath
{
    private MotionPath(string data)
    {
        Data = data;
    }

    /// <summary>The path, in PowerPoint's cut-down SVG.</summary>
    public string Data { get; }

    /// <summary>A straight line to a point, as a fraction of the slide from where the shape is.</summary>
    public static MotionPath Line(double x, double y) =>
        new(FormattableString.Invariant($"M 0 0 L {Round(x)} {Round(y)} E"));

    /// <summary>A path through a series of points, each a fraction of the slide from the start.</summary>
    /// <exception cref="OfficeNetException">Fewer than one point was given.</exception>
    public static MotionPath Through(params (double X, double Y)[] points)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Length == 0)
        {
            throw new OfficeNetException("A motion path needs at least one point to move to.");
        }

        var steps = points.Select(p =>
            FormattableString.Invariant($"L {Round(p.X)} {Round(p.Y)}"));

        return new MotionPath($"M 0 0 {string.Join(' ', steps)} E");
    }

    /// <summary>
    /// A closed loop: right, down, left, up, back to the start.
    /// </summary>
    /// <param name="width">How far across, as a fraction of the slide.</param>
    /// <param name="height">How far down, as a fraction of the slide.</param>
    public static MotionPath Rectangle(double width, double height) =>
        new(FormattableString.Invariant(
            $"M 0 0 L {Round(width)} 0 L {Round(width)} {Round(height)} L 0 {Round(height)} Z E"));

    /// <summary>A path written by hand, for shapes this class does not build.</summary>
    /// <remarks>
    /// The trailing <c>E</c> is added when it is missing: PowerPoint needs it, and a path without it
    /// is one of the ways a slide opens with the animation quietly dropped.
    /// </remarks>
    public static MotionPath Custom(string data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(data);

        var trimmed = data.TrimEnd();

        return new MotionPath(trimmed.EndsWith('E') ? trimmed : trimmed + " E");
    }

    private static string Round(double value) =>
        Math.Round(value, 5).ToString("0.#####", CultureInfo.InvariantCulture);

    public override string ToString() => Data;
}

/// <summary>
/// One animation: a shape, what happens to it, and what starts it.
/// </summary>
/// <remarks>
/// Built through the static factories rather than by hand, because the legal combinations are not
/// obvious — a spin is an emphasis, a fly can be an entrance or an exit, and a motion path takes a
/// path and nothing else. The factories make the legal ones easy and the illegal ones unsayable.
/// </remarks>
public sealed record Animation
{
    private Animation(Shape shape, AnimationKind kind, AnimationEffectKind effect)
    {
        Shape = shape;
        Kind = kind;
        Effect = effect;
    }

    /// <summary>The shape being animated.</summary>
    public Shape Shape { get; }

    /// <summary>What class of animation this is.</summary>
    public AnimationKind Kind { get; }

    /// <summary>What it does.</summary>
    public AnimationEffectKind Effect { get; }

    /// <summary>What starts it.</summary>
    public AnimationTrigger Trigger { get; init; } = AnimationTrigger.OnClick;

    /// <summary>How long it takes.</summary>
    public TimeSpan Duration { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>How long after its trigger it waits.</summary>
    public TimeSpan Delay { get; init; }

    /// <summary>Which edge a fly or wipe uses.</summary>
    public AnimationDirection Direction { get; init; } = AnimationDirection.Bottom;

    /// <summary>The path, for <see cref="AnimationKind.MotionPath"/>.</summary>
    public MotionPath? Path { get; init; }

    /// <summary>
    /// The shape whose click starts this one, for <see cref="AnimationTrigger.OnClickOf"/>.
    /// </summary>
    public Shape? TriggerShape { get; init; }

    // ---- Factories -----------------------------------------------------------------------------

    /// <summary>The shape arrives.</summary>
    public static Animation Entrance(Shape shape, AnimationEffectKind effect = AnimationEffectKind.Fade)
    {
        ArgumentNullException.ThrowIfNull(shape);
        Require(effect, AnimationKind.Entrance);

        return new Animation(shape, AnimationKind.Entrance, effect);
    }

    /// <summary>The shape, already visible, draws attention to itself.</summary>
    public static Animation Emphasis(Shape shape, AnimationEffectKind effect = AnimationEffectKind.Pulse)
    {
        ArgumentNullException.ThrowIfNull(shape);
        Require(effect, AnimationKind.Emphasis);

        return new Animation(shape, AnimationKind.Emphasis, effect);
    }

    /// <summary>The shape leaves.</summary>
    public static Animation Exit(Shape shape, AnimationEffectKind effect = AnimationEffectKind.Fade)
    {
        ArgumentNullException.ThrowIfNull(shape);
        Require(effect, AnimationKind.Exit);

        return new Animation(shape, AnimationKind.Exit, effect);
    }

    /// <summary>The shape moves along a path.</summary>
    public static Animation Motion(Shape shape, MotionPath path)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(path);

        return new Animation(shape, AnimationKind.MotionPath, AnimationEffectKind.Move)
        {
            Path = path,
            Duration = TimeSpan.FromSeconds(2),
        };
    }

    // ---- Fluent options ------------------------------------------------------------------------

    /// <summary>Starts this animation on the next click.</summary>
    public Animation OnClick() => this with { Trigger = AnimationTrigger.OnClick };

    /// <summary>Starts this animation at the same moment as the one before it.</summary>
    public Animation WithPrevious() => this with { Trigger = AnimationTrigger.WithPrevious };

    /// <summary>Starts this animation as soon as the one before it finishes.</summary>
    public Animation AfterPrevious() => this with { Trigger = AnimationTrigger.AfterPrevious };

    /// <summary>Starts this animation when a particular shape is clicked.</summary>
    public Animation OnClickOf(Shape trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        return this with { Trigger = AnimationTrigger.OnClickOf, TriggerShape = trigger };
    }

    /// <summary>Sets how long the animation takes.</summary>
    public Animation Lasting(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);

        return this with { Duration = duration };
    }

    /// <summary>Sets how long after its trigger the animation waits.</summary>
    public Animation After(TimeSpan delay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);

        return this with { Delay = delay };
    }

    /// <summary>Sets which edge a fly or wipe uses.</summary>
    public Animation From(AnimationDirection direction) => this with { Direction = direction };

    // ---- Checks --------------------------------------------------------------------------------

    private static void Require(AnimationEffectKind effect, AnimationKind kind)
    {
        var legal = kind switch
        {
            AnimationKind.Emphasis =>
                effect is AnimationEffectKind.Pulse or AnimationEffectKind.Spin
                    or AnimationEffectKind.Grow,

            AnimationKind.MotionPath => effect is AnimationEffectKind.Move,

            // Entrance and exit take the same five, mirrored.
            _ => effect is AnimationEffectKind.Appear or AnimationEffectKind.Fade
                or AnimationEffectKind.Fly or AnimationEffectKind.Wipe or AnimationEffectKind.Zoom,
        };

        if (!legal)
        {
            throw new OfficeNetException(
                $"{effect} is not {(kind == AnimationKind.Emphasis ? "an" : "a")} {kind} effect. " +
                "PowerPoint opens a file that says otherwise and plays nothing, which is harder to " +
                "diagnose than this message.");
        }
    }

    public override string ToString() =>
        $"{Kind} {Effect} on \"{Shape.Name}\" ({Trigger})";
}
