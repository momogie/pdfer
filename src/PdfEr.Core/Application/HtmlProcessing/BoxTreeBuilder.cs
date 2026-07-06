using AngleSharp.Dom;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;

namespace PdfEr.Core.Application.HtmlProcessing;

/// <summary>
/// Box generation stage (CSS 2.1 §9.2) of the box-tree layout pipeline
/// (docs/plans/phase-01-foundation.md). Walks the DOM once and produces a
/// <see cref="LayoutBox"/> tree with resolved <see cref="ComputedStyle"/> per
/// box — no sizing or placement happens here (that is Pass 1/Pass 2, not yet
/// implemented). Not wired into PdfConverterService yet; exists so it can be
/// unit-tested against DOM shape in isolation before anything downstream
/// depends on it.
///
/// Mirrors PdfConverterService.WalkDom's tag-skipping and display:none
/// handling so the two pipelines agree on which elements produce boxes at all,
/// even though this builder does not yet dispatch to ITagHandler.
/// </summary>
public sealed class BoxTreeBuilder
{
    private static readonly HashSet<string> SkipContentTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "style", "script", "noscript"
    };

    private readonly CssMerger _cssMerger;
    private readonly CssNormalizer _cssNormalizer;

    public BoxTreeBuilder(CssMerger cssMerger, CssNormalizer cssNormalizer)
    {
        _cssMerger = cssMerger;
        _cssNormalizer = cssNormalizer;
    }

    /// <summary>Builds a box tree rooted at the document body. Returns null if there is no body.</summary>
    public LayoutBox? BuildFromDocument(IDocument document)
    {
        var body = document.Body;
        if (body == null) return null;

        var bodyStyle = ComputedStyle.Resolve(ResolveElementStyle(body, null));
        var root = new LayoutBox
        {
            TagName = "body",
            Kind = LayoutBoxKind.Block,
            Style = bodyStyle,
        };

        BuildChildren(body, root, bodyStyle.Declarations);
        WrapAnonymousBlocksInPlace(root);

        return root;
    }

    private CssDeclarationBlock ResolveElementStyle(IElement element, CssDeclarationBlock? parentStyle)
    {
        var attrs = GetAttributes(element);
        var tagName = element.TagName.ToLowerInvariant();
        var resolved = _cssMerger.ResolveStyles(tagName, attrs, parentStyle, element);
        return _cssNormalizer.ExpandShorthands(resolved);
    }

    private void BuildChildren(INode domNode, LayoutBox parentBox, CssDeclarationBlock parentStyle)
    {
        foreach (var child in domNode.ChildNodes)
        {
            if (child is IElement el)
            {
                var tagName = el.TagName.ToLowerInvariant();
                if (SkipContentTags.Contains(tagName)) continue;

                var resolvedDecl = ResolveElementStyle(el, parentStyle);
                if (IsDisplayNone(resolvedDecl)) continue;

                var style = ComputedStyle.Resolve(resolvedDecl);
                var box = new LayoutBox
                {
                    TagName = tagName,
                    Kind = KindFromDisplay(style.Display),
                    Style = style,
                };
                parentBox.AddChild(box);

                BuildChildren(el, box, resolvedDecl);
                WrapAnonymousBlocksInPlace(box);
            }
            else if (child is IText textNode)
            {
                var text = textNode.TextContent;
                if (string.IsNullOrWhiteSpace(text)) continue;

                var textBox = new LayoutBox
                {
                    Kind = LayoutBoxKind.Text,
                    TextContent = text,
                    Style = ComputedStyle.Resolve(parentStyle),
                };
                parentBox.AddChild(textBox);
            }
        }
    }

    private static LayoutBoxKind KindFromDisplay(string display) => display switch
    {
        "inline" => LayoutBoxKind.Inline,
        "inline-block" => LayoutBoxKind.InlineBlock,
        "table" => LayoutBoxKind.Table,
        "table-row" => LayoutBoxKind.TableRow,
        "table-cell" => LayoutBoxKind.TableCell,
        "flex" or "inline-flex" => LayoutBoxKind.FlexContainer,
        "grid" or "inline-grid" => LayoutBoxKind.GridContainer,
        _ => LayoutBoxKind.Block,
    };

    private static bool IsInlineLevel(LayoutBox box) =>
        box.Kind is LayoutBoxKind.Inline or LayoutBoxKind.Text;

    /// <summary>
    /// CSS 2.1 §9.2.1.1: when a block container mixes block-level and
    /// inline-level children, runs of consecutive inline-level children are
    /// wrapped in an anonymous block box so the container's children are
    /// either "all blocks" or "all inlines", never mixed.
    ///
    /// Only applies when the parent itself establishes a block formatting
    /// context (plain Block boxes) — inline containers, table rows/cells, and
    /// flex/grid containers each have their own child-content rules that this
    /// first slice does not yet implement, so they are left untouched.
    /// </summary>
    private static void WrapAnonymousBlocksInPlace(LayoutBox box)
    {
        if (box.Kind != LayoutBoxKind.Block) return;
        if (box.Children.Count == 0) return;

        bool hasBlock = box.Children.Any(c => !IsInlineLevel(c));
        bool hasInline = box.Children.Any(IsInlineLevel);
        if (!hasBlock || !hasInline) return;

        var newChildren = new List<LayoutBox>();
        List<LayoutBox>? currentRun = null;

        foreach (var child in box.Children)
        {
            if (IsInlineLevel(child))
            {
                currentRun ??= new List<LayoutBox>();
                currentRun.Add(child);
            }
            else
            {
                if (currentRun != null)
                {
                    newChildren.Add(WrapRun(currentRun, box.Style));
                    currentRun = null;
                }
                newChildren.Add(child);
            }
        }
        if (currentRun != null)
            newChildren.Add(WrapRun(currentRun, box.Style));

        box.Children.Clear();
        box.Children.AddRange(newChildren);
    }

    private static LayoutBox WrapRun(List<LayoutBox> run, ComputedStyle parentStyle)
    {
        var anonBlock = new LayoutBox
        {
            IsAnonymous = true,
            Kind = LayoutBoxKind.Anonymous,
            Style = parentStyle,
        };
        foreach (var child in run)
            anonBlock.AddChild(child);
        return anonBlock;
    }

    private static bool IsDisplayNone(CssDeclarationBlock style)
    {
        var display = style.GetPropertyValue("display");
        return display != null && display.Trim().ToLowerInvariant() == "none";
    }

    private static Dictionary<string, string> GetAttributes(IElement el)
    {
        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attr in el.Attributes)
            attrs[attr.Name] = attr.Value;
        return attrs;
    }
}
