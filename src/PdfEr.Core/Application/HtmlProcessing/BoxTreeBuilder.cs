using AngleSharp.Dom;
using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;

namespace PdfEr.Core.Application.HtmlProcessing;

/// <summary>
/// Box generation stage (CSS 2.1 §9.2) of the box-tree layout pipeline
/// (docs/plans/phase-01-foundation.md). Walks the DOM once and produces a
/// <see cref="LayoutBox"/> tree with resolved <see cref="ComputedStyle"/> per
/// box — sizing/placement happens in later passes (IntrinsicSizeCalculator,
/// BlockPlacer), not here.
///
/// Mirrors PdfConverterService.WalkDom's tag-skipping and display:none
/// handling so the two pipelines agree on which elements produce boxes at all.
/// Handles a subset of tags with dedicated behavior, matching their
/// TagHandlers/ counterparts: ul/ol/li (list markers via ListState counters),
/// img (image loading via IImageService), a (link href/anchor name), br (line
/// break marker). Tables, flex, and grid content still fall through to plain
/// block generation — their layout algorithms are separate, larger pieces of
/// work (Phase 5 tables, Phase 7 flex/grid), not a box-generation concern.
/// </summary>
public sealed class BoxTreeBuilder
{
    private static readonly HashSet<string> SkipContentTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "style", "script", "noscript"
    };

    private readonly CssMerger _cssMerger;
    private readonly CssNormalizer _cssNormalizer;
    private readonly IImageService? _imageService;

    public BoxTreeBuilder(CssMerger cssMerger, CssNormalizer cssNormalizer, IImageService? imageService = null)
    {
        _cssMerger = cssMerger;
        _cssNormalizer = cssNormalizer;
        _imageService = imageService;
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

        var listStack = new Stack<ListState>();
        BuildChildren(body, root, bodyStyle.Declarations, listStack);
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

    private void BuildChildren(INode domNode, LayoutBox parentBox, CssDeclarationBlock parentStyle, Stack<ListState> listStack)
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
                var attrs = GetAttributes(el);

                if (tagName == "img")
                {
                    var imgBox = BuildImageBox(attrs, style);
                    if (imgBox != null) parentBox.AddChild(imgBox);
                    continue; // <img> has no children to walk
                }

                if (tagName == "br")
                {
                    parentBox.AddChild(new LayoutBox { TagName = "br", Kind = LayoutBoxKind.LineBreak, Style = style });
                    continue;
                }

                var box = new LayoutBox
                {
                    TagName = tagName,
                    Kind = KindFromDisplay(style.Display),
                    Style = style,
                };

                if (tagName is "ul" or "ol")
                {
                    var isOrdered = tagName == "ol";
                    var start = 1;
                    if (attrs.TryGetValue("start", out var s) && int.TryParse(s, out var parsed))
                        start = parsed;
                    var listStyleType = style.GetPropertyValue("list-style-type");
                    if (string.IsNullOrWhiteSpace(listStyleType))
                        listStyleType = isOrdered ? "decimal" : "disc";
                    listStack.Push(new ListState(isOrdered, start, listStyleType));
                }
                else if (tagName == "li")
                {
                    var listState = listStack.Count > 0 ? listStack.Peek() : null;
                    box.ListMarker = listState?.GetMarker() ?? "•";
                    if (listState != null && listState.IsOrdered)
                        listState.Counter++;
                }
                else if (tagName == "a")
                {
                    if (attrs.TryGetValue("href", out var href) && !string.IsNullOrWhiteSpace(href))
                        box.LinkUrl = href;
                    if (attrs.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name))
                        box.AnchorName = name;
                }

                parentBox.AddChild(box);

                BuildChildren(el, box, resolvedDecl, listStack);
                WrapAnonymousBlocksInPlace(box);

                if (tagName is "ul" or "ol" && listStack.Count > 0)
                    listStack.Pop();
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

    /// <summary>
    /// Mirrors ImgHandler.Open: resolves pixel dimensions (attribute override,
    /// falling back to the loaded image's natural size), converts px -> mm at
    /// 96 DPI, and stores raw image bytes on the box for the paint stage.
    /// Returns null (produces no box) if there's no src, no registered
    /// IImageService, or the image fails to load -- same as the legacy handler.
    /// </summary>
    private LayoutBox? BuildImageBox(Dictionary<string, string> attrs, ComputedStyle style)
    {
        if (_imageService == null) return null;

        var src = attrs.GetValueOrDefault("src", "");
        if (string.IsNullOrWhiteSpace(src)) return null;

        var result = _imageService.LoadImage(src, null);
        if (result == null) return null;

        var attrWidth = TryParsePixel(attrs.GetValueOrDefault("width"));
        var attrHeight = TryParsePixel(attrs.GetValueOrDefault("height"));
        int imgWidth = attrWidth ?? result.PixelWidth;
        int imgHeight = attrHeight ?? result.PixelHeight;
        if (imgWidth <= 0 || imgHeight <= 0) return null;

        const float PxToMm = 25.4f / 96f;

        var box = new LayoutBox
        {
            TagName = "img",
            Kind = LayoutBoxKind.Image,
            Style = style,
            ImageSource = src,
            ImageData = result.Data,
            ImagePixelWidth = imgWidth,
            ImagePixelHeight = imgHeight,
        };
        box.Intrinsic = new IntrinsicSizes(imgWidth * PxToMm, imgWidth * PxToMm);
        return box;
    }

    private static int? TryParsePixel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = value.Trim().ToLowerInvariant();
        if (clean.EndsWith("px")) clean = clean[..^2].Trim();
        return int.TryParse(clean, out var result) ? result : null;
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
