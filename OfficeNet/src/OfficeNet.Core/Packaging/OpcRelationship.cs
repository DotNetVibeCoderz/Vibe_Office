// OfficeNet - Dibuat oleh Gravicode Studios, dipimpin oleh Kang Fadhil.

namespace OfficeNet.Core.Packaging;

/// <summary>Whether a relationship target is inside the package or an external URI.</summary>
public enum TargetMode
{
    /// <summary>The target names another part in the same package.</summary>
    Internal,

    /// <summary>The target is a URI outside the package — a hyperlink, or a linked image.</summary>
    External,
}

/// <summary>
/// A directed, identified edge from one part (or the package root) to a target.
/// </summary>
/// <remarks>
/// Relationships, not paths, are how OOXML parts refer to each other. A run in a document says
/// <c>r:embed="rId7"</c>, never <c>media/image1.png</c> — which is what makes a part renameable and
/// what makes hyperlinks and embedded images use the same mechanism. Resolving <c>rId7</c> means
/// looking it up in the relationship part belonging to the part that used it, so relationship ids
/// are only unique within one source part.
/// </remarks>
public sealed class OpcRelationship
{
    /// <summary>The relationship id, unique within the source part (<c>rId1</c>, <c>rId2</c>, ...).</summary>
    public string Id { get; }

    /// <summary>The relationship type URI, from <see cref="RelationshipTypes"/>.</summary>
    public string Type { get; }

    /// <summary>The target, relative to the source part's directory, or an absolute URI when external.</summary>
    public string Target { get; }

    /// <summary>Whether <see cref="Target"/> points inside the package.</summary>
    public TargetMode TargetMode { get; }

    /// <summary>The part this relationship starts from; the package root has an empty name.</summary>
    public OpcPartName Source { get; }

    /// <summary>Creates a relationship.</summary>
    public OpcRelationship(OpcPartName source, string id, string type, string target, TargetMode targetMode = TargetMode.Internal)
    {
        Source = source;
        Id = id;
        Type = type;
        Target = target;
        TargetMode = targetMode;
    }

    /// <summary>
    /// The absolute part name this relationship points at.
    /// </summary>
    /// <exception cref="InvalidOperationException">The relationship is external.</exception>
    public OpcPartName TargetPartName => TargetMode == TargetMode.External
        ? throw new InvalidOperationException(
            $"Relationship '{Id}' is external ('{Target}') and has no part name.")
        : Source.Resolve(Target);

    public override string ToString() => $"{Id} -> {Target} ({Type.Split('/').Last()})";
}
