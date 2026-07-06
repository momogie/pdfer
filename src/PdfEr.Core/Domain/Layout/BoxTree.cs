using PdfEr.Core.Domain.Styles;

namespace PdfEr.Core.Domain.Layout;

// New box-tree layout model (Phase 1 of docs/plans/phase-01-foundation.md).
// Lives alongside the existing streaming-layout types in LayoutTypes.cs
// (BlockBox/InlineBox) without replacing them yet — the old pipeline keeps
// running unchanged until box-tree layout reaches parity, gated by
// PdfConverterConfiguration.UseBoxTreeLayout. Only mm/pt unit rules from
// LayoutEngine apply here too: box-tree geometry is in millimetres.

/// <summary>
/// A single box in the generated box tree (CSS 2.1 §9.2 box generation).
/// Produced by walking the DOM once per element/anonymous box; distinct from
/// <see cref="BlockBox"/>, which is the streaming pipeline's combined
/// "style + geometry" node.
/// </summary>
public sealed class LayoutBox
{
    public string? TagName { get; set; }
    public bool IsAnonymous { get; set; }
    public LayoutBoxKind Kind { get; set; } = LayoutBoxKind.Block;
    public ComputedStyle Style { get; set; } = null!;

    public LayoutBox? Parent { get; set; }
    public List<LayoutBox> Children { get; } = new();

    /// <summary>Text content for a box generated from a DOM text node (Kind == Text).</summary>
    public string? TextContent { get; set; }

    /// <summary>List marker text (e.g. "1.", "•") for a box generated from an &lt;li&gt;.</summary>
    public string? ListMarker { get; set; }

    /// <summary>Link target for a box generated from an &lt;a href&gt;.</summary>
    public string? LinkUrl { get; set; }

    /// <summary>Anchor name for a box generated from an &lt;a name&gt;.</summary>
    public string? AnchorName { get; set; }

    /// <summary>Original src attribute for a box generated from &lt;img&gt; (Kind == Image).</summary>
    public string? ImageSource { get; set; }
    public byte[]? ImageData { get; set; }
    public int ImagePixelWidth { get; set; }
    public int ImagePixelHeight { get; set; }

    /// <summary>Populated by Pass 1 (intrinsic sizing), consumed by Pass 2 (placement).</summary>
    public IntrinsicSizes? Intrinsic { get; set; }

    /// <summary>Populated by Pass 2 (placement) relative to this box's containing block.</summary>
    public BoxGeometry Geometry { get; set; }

    public void AddChild(LayoutBox child)
    {
        child.Parent = this;
        Children.Add(child);
    }
}

public enum LayoutBoxKind
{
    Block,
    Inline,
    InlineBlock,
    Text,
    Anonymous,
    Table,
    TableRow,
    TableCell,
    FlexContainer,
    GridContainer,
    Image,
    LineBreak,
}

/// <summary>
/// min-content / max-content width for a box (CSS Intrinsic and Extrinsic Sizing).
/// Computed bottom-up in Pass 1; replaces ad-hoc guesses like
/// <c>textLen * fontSize * 0.5f</c> used by the streaming pipeline
/// (LayoutEngine.cs shrink-to-fit width for inline-block).
/// </summary>
public readonly struct IntrinsicSizes
{
    public float MinContentWidth { get; }
    public float MaxContentWidth { get; }

    public IntrinsicSizes(float minContentWidth, float maxContentWidth)
    {
        MinContentWidth = minContentWidth;
        MaxContentWidth = maxContentWidth;
    }
}

/// <summary>
/// Final resolved geometry for a box after Pass 2 placement, in millimetres.
/// Mirrors the box-model fields on the legacy <see cref="BlockBox"/> so a
/// paint-time adapter can translate LayoutBox -> BlockBox/InlineBox without
/// the PDF writer needing to change (see Phase 1 "Adapter paint" checklist item).
/// </summary>
public struct BoxGeometry
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }

    public float PaddingTop { get; set; }
    public float PaddingBottom { get; set; }
    public float PaddingLeft { get; set; }
    public float PaddingRight { get; set; }

    public float BorderTop { get; set; }
    public float BorderBottom { get; set; }
    public float BorderLeft { get; set; }
    public float BorderRight { get; set; }

    public float MarginTop { get; set; }
    public float MarginBottom { get; set; }
    public float MarginLeft { get; set; }
    public float MarginRight { get; set; }

    public readonly float ContentWidth => Width - PaddingLeft - PaddingRight - BorderLeft - BorderRight;
    public readonly float ContentHeight => Height - PaddingTop - PaddingBottom - BorderTop - BorderBottom;
    public readonly float TotalWidth => Width + MarginLeft + MarginRight;
    public readonly float TotalHeight => Height + MarginTop + MarginBottom;
}

/// <summary>
/// The rectangle a box resolves percentages and "auto" against (CSS 2.1 §10.1).
/// Replaces implicit use of <c>_currentPage.ContentBox.Width</c> as a stand-in
/// containing block everywhere in the streaming LayoutEngine.
/// </summary>
public readonly struct ContainingBlock
{
    public float Width { get; }
    public float Height { get; }
    public bool HeightIsDefinite { get; }

    public ContainingBlock(float width, float height, bool heightIsDefinite)
    {
        Width = width;
        Height = height;
        HeightIsDefinite = heightIsDefinite;
    }
}

/// <summary>
/// Marks which formatting context a box establishes for its children
/// (CSS 2.1 §9.4). Replaces the streaming engine's global cursor fields
/// (_currentInlineX/_currentInlineY on LayoutEngine) with an explicit,
/// per-container object so nested contexts don't clobber each other.
/// </summary>
public enum FormattingContextKind
{
    /// <summary>Block Formatting Context: children stack vertically, margins collapse.</summary>
    Block,

    /// <summary>Inline Formatting Context: children flow into line boxes.</summary>
    Inline,

    Flex,
    Grid,
    Table,
}
