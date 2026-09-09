// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

using OfficeNet.Core;

namespace PowerPointNet.Diagrams;

/// <summary>A shape the layout decided to draw, in coordinates relative to the diagram's frame.</summary>
internal readonly record struct PlacedNode(
    string Text,
    Length Left,
    Length Top,
    Length Width,
    Length Height,
    string Geometry,
    Length FontSize)
{
    /// <summary>Whether this is a line or arrow between nodes rather than a node.</summary>
    public bool IsConnector { get; init; }

    /// <summary>Rotation clockwise, in degrees.</summary>
    public double Rotation { get; init; }

    /// <summary>
    /// The geometry's own adjustment, as a fraction, or <c>null</c> for the preset's default.
    /// </summary>
    /// <remarks>
    /// What it adjusts depends on the shape: a chevron's notch depth, a trapezoid's slope, a rounded
    /// rectangle's corner. It is stored as a fraction here and written as DrawingML's hundred
    /// thousandths, which is the only place that unit needs to appear.
    /// </remarks>
    public double? Adjust { get; init; }
}

/// <summary>
/// Works out where a diagram's nodes go.
/// </summary>
/// <remarks>
/// <para>
/// This is the part PowerPoint does with a constraint solver driven by the layout definition. What
/// is here instead is arithmetic: each kind has a shape, and the nodes are divided into it. The
/// results are close enough to PowerPoint's that a diagram written by this library and one built in
/// PowerPoint look like the same diagram, and they are not identical — PowerPoint tunes spacing
/// against the actual text metrics, and this does not.
/// </para>
/// <para>
/// The trade is deliberate. A diagram laid out here renders in every consumer, including the ones
/// that never run PowerPoint's engine. A diagram left for PowerPoint to lay out renders as an empty
/// rectangle in all of them.
/// </para>
/// </remarks>
internal static class DiagramLayout
{
    /// <summary>Text is scaled down as nodes multiply, but never below this.</summary>
    private static readonly Length MinimumFont = Length.FromPoints(9);

    private static readonly Length MaximumFont = Length.FromPoints(18);

    internal static IReadOnlyList<PlacedNode> Place(DiagramKind kind,
        IReadOnlyList<DiagramNode> nodes, Length width, Length height)
    {
        if (nodes.Count == 0)
        {
            return [];
        }

        return kind switch
        {
            DiagramKind.Process => Process(Flat(nodes), width, height),
            DiagramKind.Cycle => Cycle(Flat(nodes), width, height),
            DiagramKind.Hierarchy => Hierarchy(nodes, width, height),
            DiagramKind.Pyramid => Pyramid(Flat(nodes), width, height),
            _ => List(Flat(nodes), width, height),
        };
    }

    /// <summary>Every node in the tree. Only a hierarchy draws the tree as a tree.</summary>
    private static List<DiagramNode> Flat(IReadOnlyList<DiagramNode> nodes) =>
        [.. DiagramXml.Flatten(nodes)];

    // ---- The kinds -----------------------------------------------------------------------------

    /// <summary>Boxes stacked top to bottom, each the full width.</summary>
    private static List<PlacedNode> List(List<DiagramNode> nodes, Length width, Length height)
    {
        var gap = height / (nodes.Count * 8.0);
        var boxHeight = (height - (gap * (nodes.Count - 1))) / nodes.Count;
        var font = FontFor(boxHeight, nodes);

        return
        [
            .. nodes.Select((node, i) => new PlacedNode(
                node.Text,
                Length.Zero,
                (boxHeight + gap) * i,
                width,
                boxHeight,
                "roundRect",
                font)),
        ];
    }

    /// <summary>Chevrons left to right, so the direction of travel is visible without an arrow.</summary>
    private static List<PlacedNode> Process(List<DiagramNode> nodes, Length width, Length height)
    {
        var gap = width / (nodes.Count * 14.0);
        var boxWidth = (width - (gap * (nodes.Count - 1))) / nodes.Count;

        // A chevron taller than it is wide reads as an arrowhead rather than as a step, and its
        // notch then eats the label. Capping the height against the width keeps the row a band.
        var boxHeight = Length.Min(height * 0.45, boxWidth * 0.62);
        var top = (height - boxHeight) / 2;

        // The notch takes a quarter of the box off each end, so the label still has half the width
        // to sit in. PowerPoint's own default is deeper and assumes much wider boxes.
        const double Notch = 0.25;

        var font = FontFor(boxHeight, nodes, boxWidth * (1 - (2 * Notch)));

        return
        [
            .. nodes.Select((node, i) => new PlacedNode(
                node.Text,
                (boxWidth + gap) * i,
                top,
                boxWidth,
                boxHeight,
                // The first step is a plain block; the rest are notched, so the sequence reads
                // forwards and the leftmost shape does not point at nothing.
                i == 0 ? "homePlate" : "chevron",
                font)
            {
                Adjust = Notch,
            }),
        ];
    }

    /// <summary>Boxes round a ring, with arrows between them.</summary>
    private static List<PlacedNode> Cycle(List<DiagramNode> nodes, Length width, Length height)
    {
        var placed = new List<PlacedNode>();

        // The ring is inscribed in the frame, and the boxes sit on it, so the radius has to leave
        // room for half a box on each side or the top and bottom ones are clipped.
        var boxWidth = width / Math.Max(3.2, nodes.Count * 0.75);
        var boxHeight = height / Math.Max(3.2, nodes.Count * 0.75);

        var radiusX = (width - boxWidth) / 2;
        var radiusY = (height - boxHeight) / 2;

        var centreX = width / 2;
        var centreY = height / 2;
        var font = FontFor(boxHeight, nodes, boxWidth);

        for (var i = 0; i < nodes.Count; i++)
        {
            // Starting at the top and going clockwise, which is how a cycle is read.
            var angle = (-Math.PI / 2) + (2 * Math.PI * i / nodes.Count);

            var x = centreX + (radiusX * Math.Cos(angle)) - (boxWidth / 2);
            var y = centreY + (radiusY * Math.Sin(angle)) - (boxHeight / 2);

            placed.Add(new PlacedNode(nodes[i].Text, x, y, boxWidth, boxHeight, "ellipse", font));
        }

        // One arc of arrow between each pair, drawn as a curved band behind the boxes.
        for (var i = 0; i < nodes.Count && nodes.Count > 1; i++)
        {
            var from = (-Math.PI / 2) + (2 * Math.PI * i / nodes.Count);
            var to = (-Math.PI / 2) + (2 * Math.PI * (i + 1) / nodes.Count);
            var mid = (from + to) / 2;

            var connectorSize = Length.Min(boxWidth, boxHeight) / 3;

            placed.Add(new PlacedNode(
                string.Empty,
                centreX + (radiusX * 0.55 * Math.Cos(mid)) - (connectorSize / 2),
                centreY + (radiusY * 0.55 * Math.Sin(mid)) - (connectorSize / 2),
                connectorSize,
                connectorSize,
                "rightArrow",
                font)
            {
                IsConnector = true,

                // Tangent to the ring at this point, which is a quarter turn on from the radius.
                // An arrow that always points right makes the bottom of the cycle read backwards.
                Rotation = (mid * 180 / Math.PI) + 90,
            });
        }

        return placed;
    }

    /// <summary>A tree, each level a row, children spread under their parent.</summary>
    private static List<PlacedNode> Hierarchy(IReadOnlyList<DiagramNode> nodes,
        Length width, Length height)
    {
        var depth = Depth(nodes);
        var rowHeight = height / depth;
        var boxHeight = rowHeight * 0.6;
        var placed = new List<PlacedNode>();
        var font = FontFor(boxHeight, [.. DiagramXml.Flatten(nodes)]);

        // Each subtree gets a slice of the width proportional to how many leaves it has, so a
        // manager with six reports is not squeezed into the same space as one with two.
        Layout(nodes, Length.Zero, width, 0);

        return placed;

        void Layout(IReadOnlyList<DiagramNode> level, Length left, Length span, int row)
        {
            var total = level.Sum(Leaves);
            var offset = left;

            foreach (var node in level)
            {
                var share = span * (Leaves(node) / (double)Math.Max(1, total));
                var boxWidth = Length.Min(share * 0.82, width / 3);
                var boxLeft = offset + ((share - boxWidth) / 2);
                var boxTop = (rowHeight * row) + ((rowHeight - boxHeight) / 2);

                placed.Add(new PlacedNode(node.Text, boxLeft, boxTop, boxWidth, boxHeight,
                    "roundRect", font));

                if (node.Nodes.Count > 0)
                {
                    Layout(node.Nodes, offset, share, row + 1);
                    Connect(boxLeft + (boxWidth / 2), boxTop + boxHeight, node, offset, share, row);
                }

                offset += share;
            }
        }

        // Draws the elbow from a parent down to its children. A single vertical line is not an org
        // chart — it stops in the gap and connects to nothing. The three-part elbow (down, across,
        // down to each) is what makes the tree readable, and it needs the children's positions,
        // which is why it runs after them.
        void Connect(Length parentX, Length parentBottom, DiagramNode node, Length left, Length span,
            int row)
        {
            var barY = parentBottom + ((rowHeight - boxHeight) / 2);
            var thickness = Length.FromPoints(1.25);

            // Down from the parent to the crossbar.
            placed.Add(Line(parentX - (thickness / 2), parentBottom, thickness, barY - parentBottom));

            var centres = ChildCentres(node, left, span);

            if (centres.Count == 0)
            {
                return;
            }

            var first = Length.Min(centres[0], parentX);
            var last = Length.Max(centres[^1], parentX);

            // Across, but only when there is more than one child to reach.
            if (last.Emu > first.Emu)
            {
                placed.Add(Line(first, barY - (thickness / 2), last - first, thickness));
            }

            // Down again, into each child.
            foreach (var centre in centres)
            {
                placed.Add(Line(centre - (thickness / 2), barY, thickness,
                    (rowHeight * (row + 1)) + ((rowHeight - boxHeight) / 2) - barY));
            }
        }

        // Where a node's children were placed, recomputed rather than remembered. The same
        // arithmetic as Layout, which is a duplication worth the alternative: threading the
        // placements back out of a recursive void would mean returning a tree with no other use.
        List<Length> ChildCentres(DiagramNode node, Length left, Length span)
        {
            var centres = new List<Length>();
            var total = node.Nodes.Sum(Leaves);
            var offset = left;

            foreach (var child in node.Nodes)
            {
                var share = span * (Leaves(child) / (double)Math.Max(1, total));
                centres.Add(offset + (share / 2));
                offset += share;
            }

            return centres;
        }

        PlacedNode Line(Length x, Length y, Length w, Length h) =>
            new(string.Empty, x, y, w, h, "rect", font) { IsConnector = true };
    }

    /// <summary>Stacked bands, narrowest at the top.</summary>
    private static List<PlacedNode> Pyramid(List<DiagramNode> nodes, Length width, Length height)
    {
        var bandHeight = height / nodes.Count;
        var font = FontFor(bandHeight, nodes);
        var placed = new List<PlacedNode>();

        for (var i = 0; i < nodes.Count; i++)
        {
            // Each band is a slice of one triangle: its top edge is as wide as the band above it is
            // at the bottom. Getting that from a fixed slope instead only lines up at one band
            // count, and looks like a stack of unrelated boxes at every other.
            var topWidth = width * ((double)i / nodes.Count);
            var bottomWidth = width * ((i + 1.0) / nodes.Count);

            placed.Add(new PlacedNode(
                nodes[i].Text,
                (width - bottomWidth) / 2,
                bandHeight * i,
                bottomWidth,
                bandHeight * 0.94,
                i == 0 ? "triangle" : "trapezoid",
                font)
            {
                // How far in each top corner sits, as a fraction of this band's own width. The
                // vertical gap between bands means the slope is very slightly steeper than the
                // triangle's, which is what PowerPoint does too.
                Adjust = i == 0 ? null : (bottomWidth - topWidth).Emu / (2.0 * bottomWidth.Emu),
            });
        }

        return placed;
    }

    // ---- Text ----------------------------------------------------------------------------------

    /// <summary>
    /// Picks a font size that fits the boxes.
    /// </summary>
    /// <remarks>
    /// PowerPoint measures the real text and shrinks until it fits. This estimates from the box
    /// height and the longest label, which lands close and never below <see cref="MinimumFont"/> —
    /// a diagram with unreadable text is worse than one with text that overflows a little.
    /// </remarks>
    private static Length FontFor(Length boxHeight, IReadOnlyList<DiagramNode> nodes,
        Length? boxWidth = null)
    {
        var byHeight = boxHeight * 0.34;

        if (boxWidth is { } available)
        {
            var longest = Math.Max(1, nodes.Max(n => n.Text.Length));

            // A character is roughly half its point size wide in a proportional face, so a box of
            // w points fits about 2w/n points of type across n characters.
            var byWidth = available * (1.9 / longest);
            byHeight = Length.Min(byHeight, byWidth);
        }

        return Length.Max(MinimumFont, Length.Min(MaximumFont, byHeight));
    }

    private static int Depth(IReadOnlyList<DiagramNode> nodes) =>
        nodes.Count == 0 ? 0 : 1 + nodes.Max(n => Depth(n.Nodes));

    private static int Leaves(DiagramNode node) =>
        node.Nodes.Count == 0 ? 1 : node.Nodes.Sum(Leaves);
}
