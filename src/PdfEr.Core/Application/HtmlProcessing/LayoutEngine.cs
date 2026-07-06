using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Enums;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;
using PdfEr.Core.Domain.ValueObjects;

namespace PdfEr.Core.Application.HtmlProcessing;

public sealed class LayoutEngine
{
    private readonly CssMerger _cssMerger;
    private readonly CssNormalizer _cssNormalizer;
    private readonly IUnitConverter _unitConverter;

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

    public LayoutEngine(CssMerger cssMerger, CssNormalizer cssNormalizer, IUnitConverter unitConverter)
    {
        _cssMerger = cssMerger;
        _cssNormalizer = cssNormalizer;
        _unitConverter = unitConverter;
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
            box.FlexWrap = box.ComputedStyle?.GetPropertyValue("flex-wrap") ?? "nowrap";

            var gap = box.ComputedStyle?.GetPropertyValue("gap") ?? box.ComputedStyle?.GetPropertyValue("column-gap");
            if (!string.IsNullOrWhiteSpace(gap) && gap != "normal")
                box.FlexGap = ParseCssLength(gap, _currentPage.ContentBox.Width);

            var rowGap = box.ComputedStyle?.GetPropertyValue("row-gap") ?? box.ComputedStyle?.GetPropertyValue("gap");
            if (!string.IsNullOrWhiteSpace(rowGap) && rowGap != "normal")
                box.GridRowGap = ParseCssLength(rowGap, _currentPage.ContentBox.Height);

            if (isGrid)
            {
                var template = box.ComputedStyle?.GetPropertyValue("grid-template-columns");
                var availableWidth = box.ContentWidth > 0 ? box.ContentWidth : _currentPage.ContentBox.Width;
                box.GridColumnWidths = ParseGridTemplateColumns(template, availableWidth, box.FlexGap);
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
                    int textLen = box.TextContent?.Length ?? (box.InlineContent.Count > 0 ? 1 : 0);
                    box.Width = Math.Max(textLen * fontSize * 0.5f, fontSize) + box.PaddingLeft + box.PaddingRight + box.BorderLeft + box.BorderRight;
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
            if (!string.IsNullOrWhiteSpace(cssFloat) && cssFloat != "none")
            {
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

            float offsetTop = 0, offsetRight = 0, offsetBottom = 0, offsetLeft = 0;
            if (isRelative || isAbsolute)
            {
                offsetTop = ParseLength(box.ComputedStyle?.GetPropertyValue("top"));
                offsetRight = ParseLength(box.ComputedStyle?.GetPropertyValue("right"));
                offsetBottom = ParseLength(box.ComputedStyle?.GetPropertyValue("bottom"));
                offsetLeft = ParseLength(box.ComputedStyle?.GetPropertyValue("left"));
            }

            if (isAbsolute)
            {
                box.Type = BlockBoxType.Absolute;
                float posX = _currentPage.ContentBox.X;
                float posY = _currentPage.ContentBox.Y;
                if (offsetLeft > 0) posX += offsetLeft;
                else if (offsetRight > 0) posX = _currentPage.ContentBox.Right - box.Width - offsetRight;
                if (offsetTop > 0) posY += offsetTop;
                else if (offsetBottom > 0) posY = _currentPage.ContentBox.Bottom - box.Height - offsetBottom;
                box.X = posX;
                box.Y = posY;
            }

            if (!string.IsNullOrWhiteSpace(box.TextContent))
            {
                var inlineBox = new InlineBox
                {
                    Text = box.TextContent,
                    Type = InlineBoxType.Text,
                    LinkUrl = box.LinkUrl,
                    X = box.X + box.PaddingLeft + box.BorderLeft,
                    Y = (isAbsolute ? box.Y : _currentY) + box.PaddingTop,
                    Width = box.ContentWidth,
                    Height = fontSize * 1.3f,
                    ComputedStyle = box.ComputedStyle
                };
                box.InlineContent.Add(inlineBox);
            }

            if (isAbsolute)
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
        var inlineBox = new InlineBox
        {
            Text = text,
            Type = InlineBoxType.Text,
            X = container.X + container.PaddingLeft + container.BorderLeft,
            Y = container.Y + container.PaddingTop,
            Width = text.Length * fontSize * 0.5f,
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

        if (flex.IsGrid && flex.GridColumnWidths.Count > 0)
        {
            int colCount = flex.GridColumnWidths.Count;
            if (_gridColumnIndex >= colCount)
            {
                // wrap to next row
                _currentY += _flexRowMaxHeight + flex.GridRowGap;
                _flexRowMaxHeight = 0;
                _gridColumnIndex = 0;
                _flexChildStartX = containerLeft;
            }

            child.Width = flex.GridColumnWidths[_gridColumnIndex];
            child.X = _flexChildStartX;
            child.Y = _currentY;

            _flexChildStartX += child.Width + flex.FlexGap;
            _gridColumnIndex++;
            _flexRowMaxHeight = Math.Max(_flexRowMaxHeight, child.TotalHeight);
            return;
        }

        bool wrap = flex.FlexWrap == "wrap" || flex.FlexWrap == "wrap-reverse";
        if (wrap && _flexChildStartX + child.TotalWidth > containerRight && _flexChildStartX > containerLeft)
        {
            _currentY += _flexRowMaxHeight + flex.GridRowGap;
            _flexRowMaxHeight = 0;
            _flexChildStartX = containerLeft;
        }

        child.X = _flexChildStartX;
        child.Y = _currentY;
        _flexChildStartX += child.Width + child.MarginLeft + child.MarginRight + flex.FlexGap;
        _flexRowMaxHeight = Math.Max(_flexRowMaxHeight, child.TotalHeight);
    }

    public void BeginFlexContainer(BlockBox flex)
    {
        CurrentFlexContainer = flex;
        _flexChildStartX = flex.X + flex.PaddingLeft + flex.BorderLeft;
        _flexRowMaxHeight = 0;
        _gridColumnIndex = 0;
        _currentY = flex.Y + flex.PaddingTop + flex.BorderTop;
    }

    public void EndFlexContainer()
    {
        var flex = CurrentFlexContainer;
        CurrentFlexContainer = null;
        if (flex == null) return;

        _currentY += _flexRowMaxHeight;
        flex.Height = Math.Max(flex.Height, _currentY - flex.Y);
        _currentY += flex.MarginBottom;

        _currentInlineX = _currentPage.ContentBox.X;
        _currentInlineY = _currentY;
        _currentInlineLineHeight = 0;
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
            float minTrack = 100f;
            var minmaxMatch = System.Text.RegularExpressions.Regex.Match(
                trackSpec, @"minmax\(\s*([^,]+)\s*,\s*([^)]+)\s*\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (minmaxMatch.Success)
                minTrack = ParseCssLength(minmaxMatch.Groups[1].Value.Trim(), availableWidth);
            else
                minTrack = ParseCssLength(trackSpec, availableWidth);

            if (minTrack <= 0) minTrack = 100f;

            int explicitCount = repeatMatch.Groups[1].Success ? int.Parse(repeatMatch.Groups[1].Value) : 0;
            int colCount = explicitCount > 0
                ? explicitCount
                : Math.Max(1, (int)((availableWidth + gap) / (minTrack + gap)));

            float colWidth = (availableWidth - gap * (colCount - 1)) / colCount;
            if (colWidth < minTrack && explicitCount == 0)
            {
                colCount = Math.Max(1, colCount - 1);
                colWidth = (availableWidth - gap * (colCount - 1)) / colCount;
            }

            for (int i = 0; i < colCount; i++)
                widths.Add(colWidth);

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
}
