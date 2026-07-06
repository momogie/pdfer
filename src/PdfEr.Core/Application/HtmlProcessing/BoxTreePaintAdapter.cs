using PdfEr.Core.Domain.Layout;

namespace PdfEr.Core.Application.HtmlProcessing;

/// <summary>
/// Paint-stage adapter for the box-tree pipeline (docs/plans/phase-01-foundation.md):
/// translates a placed <see cref="LayoutBox"/> tree (geometry already resolved by
/// <see cref="BlockPlacer"/>) into the legacy <see cref="BlockBox"/>/<see cref="InlineBox"/>
/// domain objects the existing <c>PdfWriter</c> already knows how to emit — so the PDF
/// writer does not need to change while the box-tree pipeline is being built out
/// (writer rework is Phase 4/painting work, not this phase).
///
/// Text/Inline/Anonymous LayoutBoxes do not become their own BlockBox (the legacy
/// model keeps inline content as a flat list on the containing block, not as nested
/// tree nodes) — they become an InlineBox attached to the nearest Block-kind ancestor.
/// </summary>
public sealed class BoxTreePaintAdapter
{
    public BlockBox Adapt(LayoutBox root)
    {
        var block = CreateBlockBox(root);
        AdaptChildren(root, block);
        return block;
    }

    private void AdaptChildren(LayoutBox box, BlockBox parentBlock)
    {
        foreach (var child in box.Children)
        {
            if (IsInlineLevel(child))
            {
                AddInlineContent(child, parentBlock);
            }
            else
            {
                var childBlock = CreateBlockBox(child);
                parentBlock.Children.Add(childBlock);
                AdaptChildren(child, childBlock);
            }
        }
    }

    /// <summary>
    /// Inline-level content (Text/Inline/Anonymous/Image/LineBreak) is
    /// flattened onto the parent BlockBox's InlineContent list. An
    /// Inline/Anonymous container's own children are walked so their content
    /// ends up on the same parent; Text/Image/LineBreak become one InlineBox
    /// directly, mirroring how ImgHandler/BreakHandler attach InlineBox
    /// entries in the streaming pipeline.
    /// </summary>
    private void AddInlineContent(LayoutBox box, BlockBox parentBlock)
    {
        switch (box.Kind)
        {
            case LayoutBoxKind.Text:
                parentBlock.InlineContent.Add(new InlineBox
                {
                    TagName = box.TagName,
                    Text = box.TextContent,
                    X = box.Geometry.X,
                    Y = box.Geometry.Y,
                    Width = box.Geometry.Width,
                    Height = box.Geometry.Height,
                    ComputedStyle = box.Style.Declarations,
                    Type = InlineBoxType.Text,
                    LinkUrl = box.LinkUrl,
                });
                return;

            case LayoutBoxKind.Image:
                parentBlock.InlineContent.Add(new InlineBox
                {
                    TagName = "img",
                    X = box.Geometry.X,
                    Y = box.Geometry.Y,
                    Width = box.Geometry.Width,
                    Height = box.Geometry.Height,
                    ComputedStyle = box.Style.Declarations,
                    Type = InlineBoxType.Image,
                    ImageSource = box.ImageSource,
                    ImageData = box.ImageData,
                    ImagePixelWidth = box.ImagePixelWidth,
                    ImagePixelHeight = box.ImagePixelHeight,
                });
                return;

            case LayoutBoxKind.LineBreak:
                parentBlock.InlineContent.Add(new InlineBox
                {
                    TagName = "br",
                    X = box.Geometry.X,
                    Y = box.Geometry.Y,
                    Height = box.Geometry.Height,
                    ComputedStyle = box.Style.Declarations,
                    Type = InlineBoxType.LineBreak,
                });
                return;

            default:
                // Inline/Anonymous container: no visual box of its own in the
                // legacy model, so recurse into its children to reach the leaves.
                foreach (var child in box.Children)
                    AddInlineContent(child, parentBlock);
                return;
        }
    }

    private static bool IsInlineLevel(LayoutBox box) =>
        box.Kind is LayoutBoxKind.Text or LayoutBoxKind.Inline or LayoutBoxKind.Anonymous
            or LayoutBoxKind.Image or LayoutBoxKind.LineBreak;

    private static BlockBox CreateBlockBox(LayoutBox box)
    {
        var geometry = box.Geometry;
        return new BlockBox
        {
            TagName = box.TagName,
            X = geometry.X,
            Y = geometry.Y,
            Width = geometry.Width,
            Height = geometry.Height,
            ComputedStyle = box.Style.Declarations,
            PaddingTop = geometry.PaddingTop,
            PaddingBottom = geometry.PaddingBottom,
            PaddingLeft = geometry.PaddingLeft,
            PaddingRight = geometry.PaddingRight,
            BorderTop = geometry.BorderTop,
            BorderBottom = geometry.BorderBottom,
            BorderLeft = geometry.BorderLeft,
            BorderRight = geometry.BorderRight,
            MarginTop = geometry.MarginTop,
            MarginBottom = geometry.MarginBottom,
            MarginLeft = geometry.MarginLeft,
            MarginRight = geometry.MarginRight,
            Type = BlockBoxType.Normal,
            LinkUrl = box.LinkUrl,
            AnchorName = box.AnchorName,
            // Mirrors ListItemHandler: the marker ("1.", "•") is the <li> box's
            // own TextContent, separate from its children's inline text.
            TextContent = box.Kind == LayoutBoxKind.Block ? box.ListMarker : null,
        };
    }
}
