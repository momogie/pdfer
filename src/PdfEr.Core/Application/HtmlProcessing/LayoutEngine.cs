using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Enums;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;
using PdfEr.Core.Domain.Typography;
using PdfEr.Core.Domain.ValueObjects;

namespace PdfEr.Core.Application.HtmlProcessing;

public sealed class LayoutEngine
{
    private readonly CssMerger _cssMerger;
    private readonly CssNormalizer _cssNormalizer;
    private readonly IUnitConverter _unitConverter;
    private readonly IFontRegistry? _fontRegistry;

    private DocumentLayout _document = null!;
    private PageLayout _currentPage = null!;
    private float _currentY;
    private float _currentInlineX;
    private float _currentInlineY;
    private float _currentInlineLineHeight;
    public float CurrentY => _currentY;
    public PageLayout CurrentPage => _currentPage;
    private BlockBox? _currentBlockContainer;
    public BlockBox? CurrentFlexContainer { get; set; }
    private float _flexChildStartX;
    private float _flexRowMaxHeight;
    private int _gridColumnIndex;
    private int _gridRowIndex;
    private float _gridRowStartY;

    // Float tracking state
    private readonly List<FloatRegion> _floatRegions = new();
    public IReadOnlyList<FloatRegion> FloatRegions => _floatRegions;

    // Positioned containing block stack
    private readonly Stack<PositionedContainingBlock> _positionedContainingBlocks = new();

    private struct PositionedContainingBlock
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;
    }

    public LayoutEngine(CssMerger cssMerger, CssNormalizer cssNormalizer, IUnitConverter unitConverter, IFontRegistry? fontRegistry = null)
    {
        _cssMerger = cssMerger;
        _cssNormalizer = cssNormalizer;
        _unitConverter = unitConverter;
        _fontRegistry = fontRegistry;
    }

    public DocumentLayout CreateDocumentLayout(PdfConverterConfiguration config)
    {
        var size = PageSize.FromFormat(config.PageFormat);
        size = PageSize.FromOrientation(size, config.Orientation);

        _document = new DocumentLayout
        {
            DefaultPageSize = size,
            DefaultMargins = config.GetMargins(),
            DefaultOrientation = config.Orientation
        };

        AddNewPage(config);

        return _document;
    }

    public void AdvanceY(float amount)
    {
        _currentY += amount;
    }

    public void BreakPage(PdfConverterConfiguration config)
    {
        AddNewPage(config);
    }

    private void AddNewPage(PdfConverterConfiguration config, string? pageName = null)
    {
        var size = PageSize.FromOrientation(
            PageSize.FromFormat(config.PageFormat), config.Orientation);
        var margins = config.GetMargins();

        // Apply @page rule if matched
        CssPageRule? matchedRule = null;
        foreach (var rule in _cssMerger.PageRules)
        {
            if (rule.PseudoClass != null) continue; // skip :first, :left, :right for now
            if (rule.PageName == pageName || (rule.PageName == null && pageName == null))
            {
                matchedRule = rule;
                break;
            }
        }

        if (matchedRule != null)
        {
            var decl = matchedRule.Declarations;
            var sizeVal = decl.GetPropertyValue("size");
            if (!string.IsNullOrWhiteSpace(sizeVal))
            {
                sizeVal = sizeVal.Trim().ToLowerInvariant();
                var parsed = ParsePageSize(sizeVal);
                if (parsed.HasValue)
                    size = parsed.Value;
            }

            float ruleMl = ParseLength(decl.GetPropertyValue("margin-left"));
            float ruleMr = ParseLength(decl.GetPropertyValue("margin-right"));
            float ruleMt = ParseLength(decl.GetPropertyValue("margin-top"));
            float ruleMb = ParseLength(decl.GetPropertyValue("margin-bottom"));
            float ruleMargin = ParseLength(decl.GetPropertyValue("margin"));

            if (ruleMargin > 0)
            {
                if (ruleMl <= 0) ruleMl = ruleMargin;
                if (ruleMr <= 0) ruleMr = ruleMargin;
                if (ruleMt <= 0) ruleMt = ruleMargin;
                if (ruleMb <= 0) ruleMb = ruleMargin;
            }

            float ml = ruleMl > 0 ? ruleMl : margins.Left;
            float mr = ruleMr > 0 ? ruleMr : margins.Right;
            float mt = ruleMt > 0 ? ruleMt : margins.Top;
            float mb = ruleMb > 0 ? ruleMb : margins.Bottom;
            margins = new DocumentMargins(mt, mb, ml, mr, margins.Header, margins.Footer);
        }

        _currentPage = new PageLayout(size, config.Orientation, margins, _document.Pages.Count + 1);
        _currentPage.PageName = pageName;
        _document.Pages.Add(_currentPage);
        _currentY = _currentPage.ContentBox.Y;
        _currentInlineX = _currentPage.ContentBox.X;
        _currentInlineY = _currentY;
        _currentInlineLineHeight = 0;
    }

    private static PageSize? ParsePageSize(string value)
    {
        value = value.Trim().ToLowerInvariant();
        if (value == "a4") return PageSize.FromFormat(PageFormat.A4);
        if (value == "a3") return PageSize.FromFormat(PageFormat.A3);
        if (value == "a5") return PageSize.FromFormat(PageFormat.A5);
        if (value == "letter") return PageSize.FromFormat(PageFormat.Letter);
        if (value == "legal") return PageSize.FromFormat(PageFormat.Legal);
        if (value == "tabloid") return PageSize.FromFormat(PageFormat.Tabloid);

        // Custom size: "width height" in mm/pt/px/in/cm
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            float w = ParseLength(parts[0]);
            float h = ParseLength(parts[1]);
            if (w > 0 && h > 0)
                return new PageSize(w, h);
        }
        return null;
    }

    public BlockBox CreateBlock(string tagName, Dictionary<string, string> attributes, CssDeclarationBlock? parentStyle = null, AngleSharp.Dom.IElement? element = null)
    {
        var styles = _cssMerger.ResolveStyles(tagName, attributes, parentStyle, element);
        styles = _cssNormalizer.ExpandShorthands(styles);

        var box = new BlockBox
        {
            TagName = tagName,
            ComputedStyle = styles,
            X = _currentPage.ContentBox.X,
            Y = _currentY,
            Width = _currentPage.ContentBox.Width
        };

        ApplyBoxModel(box, styles);

        var cssWidth = styles.GetPropertyValue("width");
        if (!string.IsNullOrWhiteSpace(cssWidth) && cssWidth != "auto")
        {
            var parsed = ParseCssLength(cssWidth, _currentPage.ContentBox.Width);
            if (parsed > 0)
                box.Width = parsed;
        }

        // min/max-width
        var cssMinW = styles.GetPropertyValue("min-width");
        if (!string.IsNullOrWhiteSpace(cssMinW) && cssMinW != "auto")
        {
            var parsed = ParseCssLength(cssMinW, _currentPage.ContentBox.Width);
            if (parsed > 0) box.MinWidth = parsed;
        }
        var cssMaxW = styles.GetPropertyValue("max-width");
        if (!string.IsNullOrWhiteSpace(cssMaxW) && cssMaxW != "none" && cssMaxW != "auto")
        {
            var parsed = ParseCssLength(cssMaxW, _currentPage.ContentBox.Width);
            if (parsed > 0) box.MaxWidth = parsed;
        }
        if (box.MinWidth > 0 && box.Width < box.MinWidth) box.Width = box.MinWidth;
        if (box.MaxWidth > 0 && box.Width > box.MaxWidth) box.Width = box.MaxWidth;

        // Opacity
        var cssOpacity = styles.GetPropertyValue("opacity");
        if (!string.IsNullOrWhiteSpace(cssOpacity) && float.TryParse(cssOpacity, out var op) && op >= 0 && op <= 1)
            box.Opacity = op;

        return box;
    }

    public void LayoutBlock(BlockBox box, PdfConverterConfiguration config)
    {
        box.Y = _currentY;

        var display = box.ComputedStyle?.GetPropertyValue("display");
        bool isInlineBlock = display == "inline-block";
        bool isFlex = display == "flex" || display == "inline-flex";
        bool isGrid = display == "grid" || display == "inline-grid";

        if (isFlex || isGrid)
        {
            box.Type = BlockBoxType.FlexContainer;
            box.IsGrid = isGrid;
            box.FlexDirection = box.ComputedStyle?.GetPropertyValue("flex-direction") ?? "row";
            box.JustifyContent = box.ComputedStyle?.GetPropertyValue("justify-content") ?? "flex-start";
            box.AlignItems = box.ComputedStyle?.GetPropertyValue("align-items") ?? "stretch";
            box.AlignContent = box.ComputedStyle?.GetPropertyValue("align-content") ?? "stretch";
            box.FlexWrap = box.ComputedStyle?.GetPropertyValue("flex-wrap") ?? "nowrap";

            var colGap = box.ComputedStyle?.GetPropertyValue("column-gap") ?? box.ComputedStyle?.GetPropertyValue("gap");
            if (!string.IsNullOrWhiteSpace(colGap) && colGap != "normal")
                box.FlexGap = ParseCssLength(colGap, _currentPage.ContentBox.Width);
            var rowGap = box.ComputedStyle?.GetPropertyValue("row-gap") ?? box.ComputedStyle?.GetPropertyValue("gap");
            if (!string.IsNullOrWhiteSpace(rowGap) && rowGap != "normal")
            {
                box.GridRowGap = ParseCssLength(rowGap, _currentPage.ContentBox.Height);
                box.GridColumnGap = box.FlexGap;
            }

            if (isGrid)
            {
                var templateCols = box.ComputedStyle?.GetPropertyValue("grid-template-columns");
                var availableWidth = box.ContentWidth > 0 ? box.ContentWidth : _currentPage.ContentBox.Width;
                box.GridColumnWidths = ParseGridTemplateColumns(templateCols, availableWidth, box.FlexGap);

                var templateRows = box.ComputedStyle?.GetPropertyValue("grid-template-rows");
                box.GridRowHeights = ParseGridTemplateColumns(templateRows, _currentPage.ContentBox.Height, box.GridRowGap);

                box.GridAutoFlow = box.ComputedStyle?.GetPropertyValue("grid-auto-flow") ?? "row";
                box.GridTemplateAreas = box.ComputedStyle?.GetPropertyValue("grid-template-areas");
            }

            _currentY += box.MarginTop;
            box.Y = _currentY;

            if (box.Height <= 0)
                box.Height = 10;

            _currentPage.Blocks.Add(box);
            _currentBlockContainer = box;
            BeginFlexContainer(box);
            return;
        }

        if (isInlineBlock || box.TagName is "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "p" or "div"
            or "li" or "ul" or "ol" or "blockquote" or "pre" or "section" or "article"
            or "header" or "footer" or "nav" or "main" or "td" or "th" or "hr" or "img" or "caption")
        {
            var fontSize = GetFontSize(box.ComputedStyle);

            // page-break-before
            var pbBefore = box.ComputedStyle?.GetPropertyValue("page-break-before");
            if (pbBefore is "always" or "left" or "right")
            {
                if (_currentPage.PageNumber > 1 || _document.Pages.Count > 1)
                    AddNewPage(config, box.ComputedStyle?.GetPropertyValue("page"));
            }

            // page-break-inside: avoid — if block doesn't fit, break before
            var pbInside = box.ComputedStyle?.GetPropertyValue("page-break-inside");
            if (pbInside == "avoid" && box.Height > 0)
            {
                float needed = box.MarginTop + box.Height + box.MarginBottom;
                if (_currentY + needed > _currentPage.ContentBox.Bottom)
                {
                    var blockPageName = box.ComputedStyle?.GetPropertyValue("page");
                    AddNewPage(config, string.IsNullOrWhiteSpace(blockPageName) ? null : blockPageName);
                }
            }

            if (isInlineBlock)
            {
                box.Type = BlockBoxType.InlineBlock;

                // Shrink-to-fit width if not explicitly set
                var cssWidth = box.ComputedStyle?.GetPropertyValue("width");
                if (string.IsNullOrWhiteSpace(cssWidth) || cssWidth == "auto")
                {
                    float textWidth = EstimateTextWidthMm(box.TextContent, box.ComputedStyle, fontSize);
                    float minWidth = fontSize + box.PaddingLeft + box.PaddingRight + box.BorderLeft + box.BorderRight;
                    box.Width = Math.Max(textWidth, minWidth);
                }

                // Check if we need to wrap to next line
                if (_currentInlineX + box.Width > _currentPage.ContentBox.Right)
                {
                    _currentInlineX = _currentPage.ContentBox.X;
                    _currentInlineY += _currentInlineLineHeight;
                    _currentInlineLineHeight = 0;
                }

                box.X = _currentInlineX;
                box.Y = _currentInlineY;
                _currentInlineX += box.Width;

                if (box.Height <= 0)
                    box.Height = fontSize * 1.3f + box.PaddingTop + box.PaddingBottom;

                _currentInlineLineHeight = Math.Max(_currentInlineLineHeight, box.Height);

                if (!string.IsNullOrWhiteSpace(box.TextContent))
                {
                    var inlineBox = new InlineBox
                    {
                        Text = box.TextContent,
                        Type = InlineBoxType.Text,
                        LinkUrl = box.LinkUrl,
                        X = box.X + box.PaddingLeft + box.BorderLeft,
                        Y = box.Y + box.PaddingTop,
                        Width = box.ContentWidth,
                        Height = fontSize * 1.3f,
                        ComputedStyle = box.ComputedStyle
                    };
                    box.InlineContent.Add(inlineBox);
                }

                _currentPage.Blocks.Add(box);
                _currentBlockContainer = box;
                return;
            }

            if (CurrentFlexContainer != null && box != CurrentFlexContainer)
            {
                if (box.Height <= 0)
                    box.Height = fontSize * 1.3f + box.PaddingTop + box.PaddingBottom;

                var flexCssHeight = box.ComputedStyle?.GetPropertyValue("height");
                if (!string.IsNullOrWhiteSpace(flexCssHeight) && flexCssHeight != "auto")
                {
                    var parsedH = ParseCssLength(flexCssHeight, _currentPage.ContentBox.Height);
                    if (parsedH > 0) box.Height = parsedH;
                }

                if (!string.IsNullOrWhiteSpace(box.TextContent))
                {
                    var flexInline = new InlineBox
                    {
                        Text = box.TextContent,
                        Type = InlineBoxType.Text,
                        LinkUrl = box.LinkUrl,
                        X = box.X + box.PaddingLeft + box.BorderLeft,
                        Y = box.Y + box.PaddingTop,
                        Width = box.ContentWidth,
                        Height = fontSize * 1.3f,
                        ComputedStyle = box.ComputedStyle
                    };
                    box.InlineContent.Add(flexInline);
                }

                _currentPage.Blocks.Add(box);
                _currentBlockContainer = box;
                return;
            }

            _currentY += box.MarginTop;
            box.Y = _currentY;

            // CSS float support
            var cssFloat = box.ComputedStyle?.GetPropertyValue("float");
            box.Clear = box.ComputedStyle?.GetPropertyValue("clear");
            if (!string.IsNullOrWhiteSpace(cssFloat) && cssFloat != "none")
            {
                box.Y = _currentY;
                if (cssFloat == "left")
                {
                    box.Type = BlockBoxType.FloatLeft;
                    box.X = _currentPage.ContentBox.X;
                }
                else if (cssFloat == "right")
                {
                    box.Type = BlockBoxType.FloatRight;
                    box.X = _currentPage.ContentBox.Right - box.Width;
                }

                var clear = box.Clear;
                if (!string.IsNullOrWhiteSpace(clear) && clear != "none")
                {
                    float maxClearY = _currentY;
                    foreach (var region in _floatRegions)
                    {
                        bool clears = clear == "both" ||
                            (clear == "left" && region.Side == "left") ||
                            (clear == "right" && region.Side == "right");
                        if (clears)
                            maxClearY = Math.Max(maxClearY, region.Y + region.Height);
                    }
                    if (maxClearY > _currentY)
                    {
                        _currentY = maxClearY;
                        box.Y = _currentY;
                        if (cssFloat == "right")
                            box.X = _currentPage.ContentBox.Right - box.Width;
                        else
                            box.X = _currentPage.ContentBox.X;
                    }
                }

                _floatRegions.Add(new FloatRegion(box.X, box.Y, box.Width, box.Height + box.MarginBottom, cssFloat ?? "left"));
                _floatRegions.RemoveAll(r => r.Y + r.Height < _currentY - _currentPage.ContentBox.Height);
            }

            if (box.Height <= 0)
                box.Height = fontSize * 1.3f + box.PaddingTop + box.PaddingBottom;

            var cssHeight = box.ComputedStyle?.GetPropertyValue("height");
            if (!string.IsNullOrWhiteSpace(cssHeight) && cssHeight != "auto")
            {
                var parsed = ParseCssLength(cssHeight, _currentPage.ContentBox.Height);
                if (parsed > 0)
                    box.Height = parsed;
            }

            // min/max-height
            var cssMinH = box.ComputedStyle?.GetPropertyValue("min-height");
            if (!string.IsNullOrWhiteSpace(cssMinH) && cssMinH != "auto")
            {
                var parsed = ParseCssLength(cssMinH, _currentPage.ContentBox.Height);
                if (parsed > 0) box.MinHeight = parsed;
            }
            var cssMaxH = box.ComputedStyle?.GetPropertyValue("max-height");
            if (!string.IsNullOrWhiteSpace(cssMaxH) && cssMaxH != "none" && cssMaxH != "auto")
            {
                var parsed = ParseCssLength(cssMaxH, _currentPage.ContentBox.Height);
                if (parsed > 0) box.MaxHeight = parsed;
            }
            if (box.MinHeight > 0 && box.Height < box.MinHeight) box.Height = box.MinHeight;
            if (box.MaxHeight > 0 && box.Height > box.MaxHeight) box.Height = box.MaxHeight;

            // CSS position support
            var cssPosition = box.ComputedStyle?.GetPropertyValue("position");
            bool isRelative = cssPosition == "relative";
            bool isAbsolute = cssPosition == "absolute";
            bool isFixed = cssPosition == "fixed";
            bool isSticky = cssPosition == "sticky";

            var zIndexRaw = box.ComputedStyle?.GetPropertyValue("z-index");
            if (!string.IsNullOrWhiteSpace(zIndexRaw) && zIndexRaw != "auto" && float.TryParse(zIndexRaw, out var zi))
            {
                box.ZIndex = zi;
                box.HasZIndex = true;
            }

            float offsetTop = 0, offsetRight = 0, offsetBottom = 0, offsetLeft = 0;
            if (isRelative || isAbsolute || isFixed || isSticky)
            {
                offsetTop = ParseLength(box.ComputedStyle?.GetPropertyValue("top"));
                offsetRight = ParseLength(box.ComputedStyle?.GetPropertyValue("right"));
                offsetBottom = ParseLength(box.ComputedStyle?.GetPropertyValue("bottom"));
                offsetLeft = ParseLength(box.ComputedStyle?.GetPropertyValue("left"));
            }

            if (isAbsolute)
            {
                box.Type = BlockBoxType.Absolute;
                float cbX = _currentPage.ContentBox.X;
                float cbY = _currentPage.ContentBox.Y;
                float cbW = _currentPage.ContentBox.Width;
                float cbH = _currentPage.ContentBox.Height;
                if (_positionedContainingBlocks.Count > 0)
                {
                    var cb = _positionedContainingBlocks.Peek();
                    cbX = cb.X;
                    cbY = cb.Y;
                    cbW = cb.Width;
                    cbH = cb.Height;
                }
                box.ContainingBlockX = cbX;
                box.ContainingBlockY = cbY;
                box.ContainingBlockWidth = cbW;
                box.ContainingBlockHeight = cbH;
                float posX, posY;
                if (box.Width <= 0 && offsetLeft > 0 && offsetRight > 0)
                    box.Width = cbW - offsetLeft - offsetRight;
                if (offsetLeft > 0 && offsetRight > 0)
                {
                    posX = cbX + offsetLeft;
                    posY = cbY + (offsetTop > 0 ? offsetTop : 0);
                }
                else if (offsetLeft > 0)
                {
                    posX = cbX + offsetLeft;
                    posY = cbY + (offsetTop > 0 ? offsetTop : 0);
                }
                else if (offsetRight > 0)
                {
                    posX = cbX + cbW - box.Width - offsetRight;
                    posY = cbY + (offsetTop > 0 ? offsetTop : 0);
                }
                else
                {
                    posX = cbX;
                    posY = cbY;
                }
                if (offsetTop > 0)
                    posY = cbY + offsetTop;
                else if (offsetBottom > 0)
                    posY = cbY + cbH - box.Height - offsetBottom;
                box.X = posX;
                box.Y = posY;
            }
            else if (isFixed)
            {
                box.Type = BlockBoxType.Fixed;
                float posX = _currentPage.ContentBox.X;
                float posY = _currentPage.ContentBox.Y;
                if (offsetLeft > 0) posX += offsetLeft;
                else if (offsetRight > 0) posX = _currentPage.ContentBox.Right - box.Width - offsetRight;
                if (offsetTop > 0) posY += offsetTop;
                else if (offsetBottom > 0) posY = _currentPage.ContentBox.Bottom - box.Height - offsetBottom;
                box.X = posX;
                box.Y = posY;
            }
            else if (isSticky)
            {
                box.Type = BlockBoxType.Sticky;
                isRelative = true;
            }

            if (!string.IsNullOrWhiteSpace(box.TextContent))
            {
                float textY = isAbsolute ? box.Y : _currentY;
                float floatConstrainedWidth = GetFloatConstrainedWidth(box);
                var inlineBox = new InlineBox
                {
                    Text = box.TextContent,
                    Type = InlineBoxType.Text,
                    LinkUrl = box.LinkUrl,
                    X = box.X + box.PaddingLeft + box.BorderLeft,
                    Y = textY + box.PaddingTop,
                    Width = box.ContentWidth,
                    Height = fontSize * 1.3f,
                    ComputedStyle = box.ComputedStyle,
                    FloatConstrainedWidth = floatConstrainedWidth < box.ContentWidth ? floatConstrainedWidth : -1f
                };
                box.InlineContent.Add(inlineBox);
            }

            if (isAbsolute || isFixed)
            {
                _currentPage.Blocks.Add(box);
                _currentBlockContainer = box;
                return;
            }

            if (isRelative)
            {
                box.Type = BlockBoxType.Relative;
                box.X += offsetLeft - offsetRight;
                box.Y += offsetTop - offsetBottom;
            }

            _currentY += box.Height + box.MarginBottom;

            // page-break-after
            var pbAfter = box.ComputedStyle?.GetPropertyValue("page-break-after");
            if (pbAfter is "always" or "left" or "right")
            {
                AddNewPage(config, box.ComputedStyle?.GetPropertyValue("page"));
            }
            else if (_currentY > _currentPage.ContentBox.Bottom)
            {
                var blockPageName = box.ComputedStyle?.GetPropertyValue("page");
                AddNewPage(config, string.IsNullOrWhiteSpace(blockPageName) ? null : blockPageName);
                box.Y = _currentY;
                _currentY += box.Height + box.MarginBottom;
            }

            // Reset inline flow after a block element
            _currentInlineX = _currentPage.ContentBox.X;
            _currentInlineY = _currentY;
            _currentInlineLineHeight = 0;

            _currentPage.Blocks.Add(box);
        }

        _currentBlockContainer = box;
    }

    public void LayoutInlineContent(BlockBox container, string text)
    {
        var fontSize = GetFontSize(container.ComputedStyle);
        float textWidth = EstimateTextWidthMm(text, container.ComputedStyle, fontSize);
        var inlineBox = new InlineBox
        {
            Text = text,
            Type = InlineBoxType.Text,
            X = container.X + container.PaddingLeft + container.BorderLeft,
            Y = container.Y + container.PaddingTop,
            Width = textWidth,
            Height = fontSize * 1.3f,
            ComputedStyle = container.ComputedStyle
        };
        container.InlineContent.Add(inlineBox);
    }

    public static void ApplyBoxModel(BlockBox box, CssDeclarationBlock styles)
    {
        box.PaddingTop = ParseLength(styles.GetPropertyValue("padding-top"));
        box.PaddingBottom = ParseLength(styles.GetPropertyValue("padding-bottom"));
        box.PaddingLeft = ParseLength(styles.GetPropertyValue("padding-left"));
        box.PaddingRight = ParseLength(styles.GetPropertyValue("padding-right"));
        box.MarginTop = ParseLength(styles.GetPropertyValue("margin-top"));
        box.MarginBottom = ParseLength(styles.GetPropertyValue("margin-bottom"));
        box.MarginLeft = ParseLength(styles.GetPropertyValue("margin-left"));
        box.MarginRight = ParseLength(styles.GetPropertyValue("margin-right"));
        box.BorderTop = ParseLength(styles.GetPropertyValue("border-top-width"));
        box.BorderBottom = ParseLength(styles.GetPropertyValue("border-bottom-width"));
        box.BorderLeft = ParseLength(styles.GetPropertyValue("border-left-width"));
        box.BorderRight = ParseLength(styles.GetPropertyValue("border-right-width"));
    }

    // Delegates to CssLengthParser (single shared implementation — see that
    // file's remarks for why the three previously-duplicated copies were
    // consolidated). Kept as thin wrappers so call sites in this file don't change.
    private static float ParseLength(string? value) => CssLengthParser.ParseLengthMm(value);

    private static float ParseCssLength(string? value, float parentDimension) =>
        CssLengthParser.ParseCssLengthMm(value, parentDimension);

    public void PositionFlexChild(BlockBox child)
    {
        if (CurrentFlexContainer == null) return;
        var flex = CurrentFlexContainer;
        var containerLeft = flex.X + flex.PaddingLeft + flex.BorderLeft;
        var containerRight = flex.X + flex.Width - flex.PaddingRight - flex.BorderRight;
        var containerTop = flex.Y + flex.PaddingTop + flex.BorderTop;
        var containerBottom = flex.Y + flex.Height - flex.PaddingBottom - flex.BorderBottom;

        ParseFlexChildProperties(child, flex);

        if (flex.IsGrid && flex.GridColumnWidths.Count > 0)
        {
            PositionGridChild(child, flex, containerLeft, containerRight, containerTop);
            return;
        }

        bool isRow = flex.FlexDirection is null or "row" or "row-reverse";
        bool isReverse = flex.FlexDirection is "row-reverse" or "column-reverse";
        bool wrap = flex.FlexWrap == "wrap" || flex.FlexWrap == "wrap-reverse";

        if (isRow)
        {
            float childMainSize = child.FlexBasisIsAuto ? child.Width : child.FlexBasis;
            bool isOverflowing = false;
            if (isReverse)
                isOverflowing = _flexChildStartX - childMainSize < containerLeft;
            else
                isOverflowing = _flexChildStartX + childMainSize > containerRight;

            if (wrap && isOverflowing && _flexChildStartX > containerLeft)
            {
                _currentY += _flexRowMaxHeight + flex.GridRowGap;
                _flexRowMaxHeight = 0;
                _flexChildStartX = isReverse ? containerRight : containerLeft;
            }

            if (isReverse)
            {
                _flexChildStartX -= childMainSize;
                child.X = _flexChildStartX;
            }
            else
            {
                child.X = _flexChildStartX;
                _flexChildStartX += childMainSize + flex.FlexGap;
            }
            child.Y = _currentY;
            _flexRowMaxHeight = Math.Max(_flexRowMaxHeight, child.TotalHeight);
        }
        else
        {
            float childMainSize = child.FlexBasisIsAuto ? child.Height : child.FlexBasis;
            if (wrap && _currentY + childMainSize > containerBottom && _currentY > containerTop)
            {
                _currentY = containerTop;
                _flexChildStartX += _flexRowMaxHeight + flex.FlexGap;
                _flexRowMaxHeight = 0;
            }
            child.X = _flexChildStartX;
            child.Y = _currentY;
            _currentY += childMainSize + flex.GridRowGap;
            _flexRowMaxHeight = Math.Max(_flexRowMaxHeight, child.TotalWidth);
        }
    }

    private static void ParseFlexChildProperties(BlockBox child, BlockBox flex)
    {
        var style = child.ComputedStyle;
        if (style == null) return;
        var grow = style.GetPropertyValue("flex-grow");
        child.FlexGrow = (!string.IsNullOrWhiteSpace(grow) && float.TryParse(grow, out var g)) ? g : 0;
        var shrink = style.GetPropertyValue("flex-shrink");
        child.FlexShrink = (!string.IsNullOrWhiteSpace(shrink) && float.TryParse(shrink, out var s)) ? s : 1;
        var basis = style.GetPropertyValue("flex-basis");
        if (!string.IsNullOrWhiteSpace(basis) && basis != "auto")
        {
            child.FlexBasisIsAuto = false;
            if (basis.EndsWith('%'))
            {
                float pcbWidth = flex.ContentWidth > 0 ? flex.ContentWidth : 0;
                if (float.TryParse(basis[..^1], out var pct))
                    child.FlexBasis = pcbWidth * pct / 100f;
            }
            else
            {
                child.FlexBasis = CssLengthParser.ParseLengthMm(basis);
            }
        }
        else
        {
            child.FlexBasisIsAuto = true;
            child.FlexBasis = 0;
        }
        var flexShorthand = style.GetPropertyValue("flex");
        if (!string.IsNullOrWhiteSpace(flexShorthand) && flexShorthand != "none")
        {
            var parts = flexShorthand.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 && float.TryParse(parts[0], out var flexGrow))
                child.FlexGrow = flexGrow;
            if (parts.Length >= 2 && float.TryParse(parts[1], out var flexShrink))
                child.FlexShrink = flexShrink;
            if (parts.Length >= 3 && parts[2] != "auto")
            {
                child.FlexBasisIsAuto = false;
                if (parts[2].EndsWith('%'))
                {
                    float pcbWidth = flex.ContentWidth > 0 ? flex.ContentWidth : 0;
                    if (float.TryParse(parts[2][..^1], out var pct))
                        child.FlexBasis = pcbWidth * pct / 100f;
                }
                else
                {
                    child.FlexBasis = CssLengthParser.ParseLengthMm(parts[2]);
                }
            }
        }
        var alignSelf = style.GetPropertyValue("align-self");
        child.AlignSelf = (!string.IsNullOrWhiteSpace(alignSelf) && alignSelf != "auto") ? alignSelf : null;
        var order = style.GetPropertyValue("order");
        child.Order = (!string.IsNullOrWhiteSpace(order) && int.TryParse(order, out var o)) ? o : 0;
        var gridArea = style.GetPropertyValue("grid-area");
        if (!string.IsNullOrWhiteSpace(gridArea) && gridArea != "auto" && !gridArea.Contains('/'))
            child.GridArea = gridArea.Trim();
        var gcs = style.GetPropertyValue("grid-column-start");
        if (!string.IsNullOrWhiteSpace(gcs) && int.TryParse(gcs, out var gcsVal))
            child.GridColumnStart = gcsVal;
        var gce = style.GetPropertyValue("grid-column-end");
        if (!string.IsNullOrWhiteSpace(gce) && int.TryParse(gce, out var gceVal))
            child.GridColumnEnd = gceVal;
        var grs = style.GetPropertyValue("grid-row-start");
        if (!string.IsNullOrWhiteSpace(grs) && int.TryParse(grs, out var grsVal))
            child.GridRowStart = grsVal;
        var gre = style.GetPropertyValue("grid-row-end");
        if (!string.IsNullOrWhiteSpace(gre) && int.TryParse(gre, out var greVal))
            child.GridRowEnd = greVal;
    }

    private void PositionGridChild(BlockBox child, BlockBox flex, float containerLeft, float containerRight, float containerTop)
    {
        int colCount = flex.GridColumnWidths.Count;
        bool hasRowHeights = flex.GridRowHeights.Count > 0;

        // Resolve grid-area name from grid-template-areas
        int areaColStart = 0, areaRowStart = 0, areaColEnd = 0, areaRowEnd = 0;
        bool hasArea = false;
        if (!string.IsNullOrWhiteSpace(child.GridArea) && !string.IsNullOrWhiteSpace(flex.GridTemplateAreas))
        {
            var areaMap = ParseGridTemplateAreas(flex.GridTemplateAreas);
            if (areaMap.TryGetValue(child.GridArea, out var area))
            {
                areaColStart = area.colStart;
                areaRowStart = area.rowStart;
                areaColEnd = area.colEnd;
                areaRowEnd = area.rowEnd;
                hasArea = true;
            }
        }

        int colStart, rowStart;
        if (hasArea)
        {
            colStart = areaColStart;
            rowStart = areaRowStart;
        }
        else
        {
            colStart = child.GridColumnStart > 0 ? child.GridColumnStart - 1 : _gridColumnIndex;
            rowStart = child.GridRowStart > 0 ? child.GridRowStart - 1 : _gridRowIndex;
        }
        if (hasRowHeights && rowStart > _gridRowIndex)
        {
            float rowOffset = 0;
            for (int r = _gridRowIndex; r < rowStart && r < flex.GridRowHeights.Count; r++)
                rowOffset += flex.GridRowHeights[r] + flex.GridRowGap;
            _currentY += rowOffset;
            _flexRowMaxHeight = 0;
            _gridRowIndex = rowStart;
            _gridColumnIndex = 0;
            _flexChildStartX = containerLeft;
        }
        if (_gridColumnIndex >= colCount)
        {
            if (hasRowHeights && _gridRowIndex < flex.GridRowHeights.Count)
                _currentY += flex.GridRowHeights[_gridRowIndex] + flex.GridRowGap;
            else
                _currentY += _flexRowMaxHeight + flex.GridRowGap;
            _flexRowMaxHeight = 0;
            _gridColumnIndex = 0;
            _gridRowIndex++;
            _flexChildStartX = containerLeft;
        }
        int colSpan = hasArea
            ? Math.Max(1, areaColEnd - areaColStart)
            : Math.Max(1, child.GridColumnEnd - child.GridColumnStart);
        float colWidth = 0;
        for (int c = _gridColumnIndex; c < _gridColumnIndex + colSpan && c < colCount; c++)
            colWidth += flex.GridColumnWidths[c];
        child.Width = colWidth > 0 ? colWidth : flex.GridColumnWidths[_gridColumnIndex];
        child.X = _flexChildStartX;
        child.Y = _currentY;
        _flexChildStartX += child.Width + flex.FlexGap;
        _gridColumnIndex += colSpan;
        _flexRowMaxHeight = Math.Max(_flexRowMaxHeight, child.TotalHeight);
    }

    public void BeginFlexContainer(BlockBox flex)
    {
        CurrentFlexContainer = flex;
        _flexChildStartX = flex.X + flex.PaddingLeft + flex.BorderLeft;
        _flexRowMaxHeight = 0;
        _gridColumnIndex = 0;
        _gridRowIndex = 0;
        _gridRowStartY = _currentY;
        _currentY = flex.Y + flex.PaddingTop + flex.BorderTop;
        if (flex.IsGrid && flex.GridRowHeights.Count > 0)
            _gridRowStartY = _currentY;
    }

    public void EndFlexContainer()
    {
        var flex = CurrentFlexContainer;
        CurrentFlexContainer = null;
        if (flex == null) return;

        bool isGrid = flex.IsGrid;

        if (!isGrid)
        {
            var children = _currentPage.Blocks
                .Where(b => b != flex && b.Y >= flex.Y + flex.PaddingTop + flex.BorderTop
                    && b.Y < flex.Y + flex.Height)
                .ToList();

            if (children.Count > 0)
            {
                children.Sort((a, b) => a.Order.CompareTo(b.Order));
                bool isRow = flex.FlexDirection is null or "row" or "row-reverse";
                bool isReverse = flex.FlexDirection is "row-reverse" or "column-reverse";

                if (isRow)
                {
                    var lines = new List<List<BlockBox>>();
                    var currentLine = new List<BlockBox>();
                    float containerWidth = flex.ContentWidth;
                    float currentLineWidth = 0;
                    float gap = flex.FlexGap;

                    foreach (var child in children)
                    {
                        float childSize = child.FlexBasisIsAuto ? child.Width : child.FlexBasis;
                        if (childSize <= 0) childSize = child.Width > 0 ? child.Width : 50;

                        if (flex.FlexWrap is "wrap" or "wrap-reverse" && currentLine.Count > 0
                            && currentLineWidth + childSize + gap > containerWidth)
                        {
                            lines.Add(currentLine);
                            currentLine = new List<BlockBox>();
                            currentLineWidth = 0;
                        }

                        currentLine.Add(child);
                        currentLineWidth += childSize + (currentLine.Count > 1 ? gap : 0);
                    }
                    if (currentLine.Count > 0)
                        lines.Add(currentLine);

                    float containerTop = flex.Y + flex.PaddingTop + flex.BorderTop;
                    float cursorY = containerTop;

                    foreach (var line in lines)
                    {
                        float totalGap = gap * (line.Count - 1);
                        float totalBaseSize = line.Sum(c => c.FlexBasisIsAuto ? c.Width : c.FlexBasis);
                        float availableSpace = containerWidth - totalBaseSize - totalGap;
                        float totalGrow = line.Sum(c => c.FlexGrow);
                        float totalShrink = line.Sum(c => c.FlexShrink);

                        var justifiedPositions = ApplyJustifyContent(
                            line, flex.JustifyContent ?? "flex-start",
                            containerWidth, totalGap, totalBaseSize,
                            flex.X + flex.PaddingLeft + flex.BorderLeft, isReverse);

                        int itemIndex = 0;
                        foreach (var child in line)
                        {
                            float baseSize = child.FlexBasisIsAuto ? child.Width : child.FlexBasis;
                            if (baseSize <= 0) baseSize = child.Width > 0 ? child.Width : 50;

                            if (availableSpace > 0 && totalGrow > 0)
                            {
                                float extra = availableSpace * child.FlexGrow / totalGrow;
                                if (isRow) child.Width = baseSize + extra;
                            }
                            else if (availableSpace < 0 && totalShrink > 0)
                            {
                                float shrinkAmount = Math.Abs(availableSpace) * child.FlexShrink / totalShrink;
                                float minContentWidth = EstimateMinContentWidthMm(child.TextContent, child.ComputedStyle);
                                float minWidth = new float[] { child.MinWidth, minContentWidth }.Max();
                                if (isRow) child.Width = Math.Max(minWidth, baseSize - shrinkAmount);
                            }

                            if (isReverse)
                                child.X = justifiedPositions[itemIndex] - child.Width;
                            else
                                child.X = justifiedPositions[itemIndex];
                            child.Y = cursorY;
                            itemIndex++;
                        }

                        var align = flex.AlignItems ?? "stretch";
                        foreach (var child in line)
                        {
                            string? childAlign = child.AlignSelf;
                            string effectiveAlign = childAlign ?? align;
                            if (effectiveAlign == "stretch")
                            {
                                float maxChildH = line.Max(c => c.Height);
                                if (maxChildH > child.Height)
                                    child.Height = maxChildH;
                            }
                            else if (effectiveAlign == "center")
                            {
                                float maxH = line.Max(c => c.Height);
                                child.Y = cursorY + (maxH - child.Height) / 2;
                            }
                            else if (effectiveAlign == "flex-end")
                            {
                                float maxH = line.Max(c => c.Height);
                                child.Y = cursorY + (maxH - child.Height);
                            }
                        }

                        float lineHeight = line.Max(c => c.TotalHeight);
                        cursorY += lineHeight + (flex.GridRowGap > 0 ? flex.GridRowGap : 0);
                    }

                    _currentY = cursorY;
                    _flexRowMaxHeight = cursorY - containerTop;
                }
                else
                {
                    float cursorY = flex.Y + flex.PaddingTop + flex.BorderTop;
                    foreach (var child in children)
                    {
                        child.X = flex.X + flex.PaddingLeft + flex.BorderLeft;
                        child.Y = cursorY;
                        cursorY += child.Height + flex.GridRowGap;
                    }
                    _currentY = cursorY;
                    _flexRowMaxHeight = cursorY - (flex.Y + flex.PaddingTop + flex.BorderTop);
                }
            }
        }
        else
        {
            if (flex.GridRowHeights.Count > 0)
            {
                float totalGridH = flex.GridRowHeights.Sum() + flex.GridRowGap * (flex.GridRowHeights.Count - 1);
                _currentY = _gridRowStartY + totalGridH;
            }
            else
            {
                _currentY += _flexRowMaxHeight;
            }
        }

        if (!isGrid && flex.FlexWrap is "wrap" or "wrap-reverse")
        {
            var flexChildren = _currentPage.Blocks.Where(b => b != flex && b.Y >= flex.Y + flex.PaddingTop + flex.BorderTop).ToList();
            if (flexChildren.Count > 1)
            {
                var lineYPositions = flexChildren.Select(c => c.Y).Distinct().OrderBy(y => y).ToList();
                int lineCount = lineYPositions.Count;
                if (lineCount > 1)
                {
                    var alignContent = flex.AlignContent ?? "stretch";
                    float totalContentH = _currentY - (flex.Y + flex.PaddingTop + flex.BorderTop);
                    float containerH = flex.ContentHeight;
                    if (containerH > totalContentH)
                    {
                        float extraSpace = containerH - totalContentH;
                        if (alignContent == "center")
                        {
                            float shift = extraSpace / 2;
                            foreach (var child in flexChildren)
                                child.Y += shift;
                            _currentY += shift;
                        }
                        else if (alignContent == "flex-end")
                        {
                            foreach (var child in flexChildren)
                                child.Y += extraSpace;
                            _currentY += extraSpace;
                        }
                        else if (alignContent == "space-between")
                        {
                            float space = extraSpace / (lineCount - 1);
                            var lineShifts = new Dictionary<float, float>();
                            float accShift = 0;
                            foreach (var yPos in lineYPositions)
                            {
                                lineShifts[yPos] = accShift;
                                accShift += space;
                            }
                            foreach (var child in flexChildren)
                                child.Y += lineShifts.GetValueOrDefault(child.Y, 0);
                            _currentY += extraSpace;
                        }
                        else if (alignContent == "space-around")
                        {
                            float space = extraSpace / (lineCount * 2);
                            var lineShifts = new Dictionary<float, float>();
                            float accShift = space;
                            foreach (var yPos in lineYPositions)
                            {
                                lineShifts[yPos] = accShift;
                                accShift += space * 2;
                            }
                            foreach (var child in flexChildren)
                                child.Y += lineShifts.GetValueOrDefault(child.Y, 0);
                            _currentY += extraSpace;
                        }
                    }
                }
            }
        }

        flex.Height = Math.Max(flex.Height, _currentY - flex.Y);
        _currentY += flex.MarginBottom;

        _currentInlineX = _currentPage.ContentBox.X;
        _currentInlineY = _currentY;
        _currentInlineLineHeight = 0;
    }

    private static float[] ApplyJustifyContent(List<BlockBox> line, string justifyContent,
        float containerWidth, float totalGap, float totalBaseSize, float startX, bool isReverse)
    {
        int count = line.Count;
        var positions = new float[count];
        if (count == 0) return positions;
        float totalUsed = totalBaseSize + totalGap;
        float extraSpace = Math.Max(0, containerWidth - totalUsed);
        float cursor = startX;
        float gap = justifyContent switch
        {
            "space-between" when count > 1 => extraSpace / (count - 1),
            "space-around" when count > 0 => extraSpace / count,
            "space-evenly" when count > 0 => extraSpace / (count + 1),
            "center" => 0,
            "flex-end" => 0,
            _ => 0
        };
        if (justifyContent == "space-around" || justifyContent == "space-evenly")
        {
            float baseSpacing = justifyContent == "space-around" ? gap / 2 : gap;
            cursor += baseSpacing;
        }
        else if (justifyContent == "center")
            cursor += extraSpace / 2;
        else if (justifyContent == "flex-end")
            cursor += extraSpace;
        for (int i = 0; i < count; i++)
        {
            positions[i] = cursor;
            float childSize = line[i].FlexBasisIsAuto ? line[i].Width : line[i].FlexBasis;
            if (childSize <= 0) childSize = line[i].Width > 0 ? line[i].Width : 50;
            cursor += childSize + (line[i].FlexGap > 0 ? line[i].FlexGap : 0);
            if (justifyContent == "space-between" && i < count - 1)
                cursor += gap;
            else if (justifyContent == "space-around")
                cursor += gap;
            else if (justifyContent == "space-evenly")
                cursor += gap;
        }
        return positions;
    }

    /// <summary>
    /// Parses CSS grid-template-areas into a map of area name -> (colStart, rowStart, colEnd, rowEnd).
    /// Input format:  "header header" "sidebar main" "footer footer"
    /// Returns empty dictionary if no valid areas found.
    /// </summary>
    internal static Dictionary<string, (int colStart, int rowStart, int colEnd, int rowEnd)> ParseGridTemplateAreas(string? value)
    {
        var areas = new Dictionary<string, (int colStart, int rowStart, int colEnd, int rowEnd)>();
        if (string.IsNullOrWhiteSpace(value)) return areas;

        // Parse quoted strings: "header header" "sidebar main" "footer footer"
        var rows = new List<string[]>();
        int i = 0;
        while (i < value.Length)
        {
            if (value[i] == '"' || value[i] == '\'')
            {
                var quote = value[i];
                i++;
                var rowStr = "";
                while (i < value.Length && value[i] != quote)
                {
                    rowStr += value[i];
                    i++;
                }
                if (i < value.Length) i++; // skip closing quote
                var cells = rowStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (cells.Length > 0)
                    rows.Add(cells);
            }
            else
            {
                i++;
            }
        }

        if (rows.Count == 0) return areas;

        // Find extent of each named area
        var areaExtents = new Dictionary<string, (int minCol, int maxCol, int minRow, int maxRow)>();
        for (int r = 0; r < rows.Count; r++)
        {
            for (int c = 0; c < rows[r].Length; c++)
            {
                var name = rows[r][c];
                if (name == "." || name == "..." || string.IsNullOrWhiteSpace(name)) continue;
                if (areaExtents.TryGetValue(name, out var ext))
                {
                    areaExtents[name] = (
                        Math.Min(ext.minCol, c),
                        Math.Max(ext.maxCol, c),
                        Math.Min(ext.minRow, r),
                        Math.Max(ext.maxRow, r));
                }
                else
                {
                    areaExtents[name] = (c, c, r, r);
                }
            }
        }

        foreach (var kv in areaExtents)
        {
            var ext = kv.Value;
            areas[kv.Key] = (ext.minCol, ext.minRow, ext.maxCol + 1, ext.maxRow + 1);
        }

        return areas;
    }

    private static List<float> ParseGridTemplateColumns(string? template, float availableWidth, float gap)
    {
        var widths = new List<float>();
        if (string.IsNullOrWhiteSpace(template))
        {
            widths.Add(availableWidth);
            return widths;
        }

        template = template.Trim();

        var repeatMatch = System.Text.RegularExpressions.Regex.Match(
            template, @"repeat\(\s*(?:(\d+)|auto-fit|auto-fill)\s*,\s*(.+?)\)\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (repeatMatch.Success)
        {
            var trackSpec = repeatMatch.Groups[2].Value.Trim();
            float trackSize = 0;
            bool isMinMax = false;
            float minTrackSize = 0;
            var minmaxMatch = System.Text.RegularExpressions.Regex.Match(
                trackSpec, @"minmax\(\s*([^,]+)\s*,\s*([^)]+)\s*\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (minmaxMatch.Success)
            {
                isMinMax = true;
                minTrackSize = ParseCssLength(minmaxMatch.Groups[1].Value.Trim(), availableWidth);
                var maxStr = minmaxMatch.Groups[2].Value.Trim();
                if (maxStr.EndsWith("fr", StringComparison.OrdinalIgnoreCase))
                    trackSize = 0;
                else
                    trackSize = ParseCssLength(maxStr, availableWidth);
                if (minTrackSize <= 0) minTrackSize = 50f;
                if (trackSize <= 0) trackSize = minTrackSize;
            }
            else
            {
                trackSize = ParseCssLength(trackSpec, availableWidth);
                if (trackSize <= 0) trackSize = 100f;
            }

            int explicitCount = repeatMatch.Groups[1].Success ? int.Parse(repeatMatch.Groups[1].Value) : 0;

            if (explicitCount > 0)
            {
                for (int i = 0; i < explicitCount; i++)
                    widths.Add(trackSize);
                return widths;
            }

            float minTrack = isMinMax ? minTrackSize : trackSize;
            int colCount = Math.Max(1, (int)((availableWidth + gap) / (minTrack + gap)));
            float colWidth = (availableWidth - gap * (colCount - 1)) / colCount;
            if (colWidth < minTrack && colCount > 1)
            {
                colCount--;
                colWidth = (availableWidth - gap * (colCount - 1)) / colCount;
            }
            float actualSize = isMinMax ? Math.Max(minTrackSize, colWidth) : trackSize;
            if (isMinMax && actualSize > colWidth)
                actualSize = colWidth;

            for (int i = 0; i < colCount; i++)
                widths.Add(actualSize);

            return widths;
        }

        var tracks = SplitTopLevel(template);
        var fixedWidths = new float[tracks.Count];
        var frFactors = new float[tracks.Count];
        float fixedTotal = 0;
        float frTotal = 0;

        for (int i = 0; i < tracks.Count; i++)
        {
            var t = tracks[i].Trim();
            if (t.EndsWith("fr", StringComparison.OrdinalIgnoreCase))
            {
                if (float.TryParse(t[..^2], out var fr)) frFactors[i] = fr;
                else frFactors[i] = 1f;
                frTotal += frFactors[i];
            }
            else
            {
                fixedWidths[i] = ParseCssLength(t, availableWidth);
                fixedTotal += fixedWidths[i];
            }
        }

        float totalGap = gap * Math.Max(0, tracks.Count - 1);
        float remaining = Math.Max(0, availableWidth - fixedTotal - totalGap);

        for (int i = 0; i < tracks.Count; i++)
        {
            if (frFactors[i] > 0)
                widths.Add(frTotal > 0 ? remaining * frFactors[i] / frTotal : 0);
            else
                widths.Add(fixedWidths[i]);
        }

        if (widths.Count == 0)
            widths.Add(availableWidth);

        return widths;
    }

    private static List<string> SplitTopLevel(string value)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '(') depth++;
            else if (value[i] == ')') depth--;
            else if (value[i] == ' ' && depth == 0)
            {
                if (i > start) parts.Add(value[start..i]);
                start = i + 1;
            }
        }
        if (start < value.Length) parts.Add(value[start..]);
        return parts;
    }

    /// <summary>
    /// Computes the available content width for a block at a given Y position,
    /// reduced by any float regions that intrude into its area.
    /// </summary>
    public float GetFloatConstrainedWidth(BlockBox box)
    {
        float available = box.ContentWidth;
        float boxLeft = box.X + box.PaddingLeft + box.BorderLeft;
        float boxTop = box.Y + box.PaddingTop + box.BorderTop;
        float boxBottom = boxTop + box.ContentHeight;

        foreach (var region in _floatRegions)
        {
            if (region.Y + region.Height <= boxTop || region.Y >= boxBottom)
                continue;

            if (region.Side == "left")
            {
                float overlap = (region.X + region.Width) - boxLeft;
                if (overlap > 0)
                    available -= overlap;
            }
            else if (region.Side == "right")
            {
                float overlap = (boxLeft + available) - region.X;
                if (overlap > 0)
                    available -= overlap;
            }
        }

        return Math.Max(0, available);
    }

    public void PushPositionedContainingBlock(float x, float y, float width, float height)
    {
        _positionedContainingBlocks.Push(new PositionedContainingBlock { X = x, Y = y, Width = width, Height = height });
    }

    public void PopPositionedContainingBlock()
    {
        if (_positionedContainingBlocks.Count > 0)
            _positionedContainingBlocks.Pop();
    }

    // Returns the font size expressed in millimetres (layout units).
    // px/pt/keyword inputs are point-based per CSS; convert points -> mm (1pt = 0.3528mm).
    private const float PtToMm = 0.3528f;

    private static float GetFontSize(CssDeclarationBlock? style)
    {
        return GetFontSizePt(style) * PtToMm;
    }

    private static float GetFontSizePt(CssDeclarationBlock? style)
    {
        var val = style?.GetPropertyValue("font-size");
        if (val == null) return 10f;
        val = val.Trim().ToLowerInvariant();

        if (val.EndsWith("pt")) return float.TryParse(val[..^2], out var v) ? v : 10f;
        if (val.EndsWith("px")) return float.TryParse(val[..^2], out var v) ? v * 0.75f : 10f;
        if (val.EndsWith("mm")) return float.TryParse(val[..^2], out var v) ? v * 2.8346f : 10f;
        if (val.EndsWith("rem")) return float.TryParse(val[..^3], out var v) ? v * 10f : 10f;
        if (val.EndsWith("em")) return float.TryParse(val[..^2], out var v) ? v * 10f : 10f;
        if (val.EndsWith("%")) return float.TryParse(val[..^1], out var v) ? v * 0.1f : 10f;

        return val switch
        {
            "xx-small" => 7f,
            "x-small" => 7.7f,
            "small" => 8.6f,
            "medium" => 10f,
            "large" => 12f,
            "x-large" => 15f,
            "xx-large" => 20f,
            "smaller" => 8.3f,
            "larger" => 12f,
            _ => 10f
        };
    }

    /// <summary>
    /// Estimates the min-content width (widest unbreakable word) for a text content.
    /// Used as the shrink floor for flex-shrink (CSS Flexbox §9.2).
    /// </summary>
    private float EstimateMinContentWidthMm(string? text, CssDeclarationBlock? style)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var words = text.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return 0;
        float fontSizeMm = GetFontSize(style);
        float maxWordWidth = 0;
        foreach (var word in words)
            maxWordWidth = Math.Max(maxWordWidth, EstimateTextWidthMm(word, style, fontSizeMm));
        return maxWordWidth;
    }

    private float EstimateTextWidthMm(string? text, CssDeclarationBlock? style, float fontSizeMm)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        if (_fontRegistry == null)
            return text.Length * fontSizeMm * 0.5f;

        var fontFamily = style?.GetPropertyValue("font-family") ?? "sans-serif";
        var fontSizePt = GetFontSizePt(style);
        var (bold, italic) = ResolveFontStyleFlags(style);
        var fs = (bold, italic) switch
        {
            (true, true) => FontStyle.BoldItalic,
            (true, false) => FontStyle.Bold,
            (false, true) => FontStyle.Italic,
            (false, false) => FontStyle.Regular
        };

        var metrics = _fontRegistry.GetMetrics(fontFamily, fs, fontSizePt);
        if (metrics == null)
            return text.Length * fontSizeMm * 0.5f;

        float totalPt = 0;
        foreach (char c in text)
        {
            if (metrics.AdvanceWidths.TryGetValue(c, out var w))
                totalPt += w;
            else
                totalPt += metrics.SizePoints * 0.5f;
        }

        return totalPt * PtToMm;
    }

    private static (bool bold, bool italic) ResolveFontStyleFlags(CssDeclarationBlock? style)
    {
        if (style == null) return (false, false);
        var weight = style.GetPropertyValue("font-weight");
        var fontStyle = style.GetPropertyValue("font-style");
        bool bold = weight is "bold" or "700" or "800" or "900" ||
            (weight != null && int.TryParse(weight, out var w) && w >= 700);
        bool italic = fontStyle is "italic" or "oblique";
        return (bold, italic);
    }
}
