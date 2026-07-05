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

    public BlockBox CreateBlock(string tagName, Dictionary<string, string> attributes, CssDeclarationBlock? parentStyle = null)
    {
        var styles = _cssMerger.ResolveStyles(tagName, attributes, parentStyle);
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

        return box;
    }

    public void LayoutBlock(BlockBox box, PdfConverterConfiguration config)
    {
        box.Y = _currentY;

        var display = box.ComputedStyle?.GetPropertyValue("display");
        bool isInlineBlock = display == "inline-block";

        if (isInlineBlock || box.TagName is "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "p" or "div"
            or "li" or "ul" or "ol" or "blockquote" or "pre" or "section" or "article"
            or "header" or "footer" or "nav" or "main" or "td" or "th" or "hr" or "img")
        {
            var fontSize = GetFontSize(box.ComputedStyle);

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
                // Position relative to page content box (nearest positioned ancestor)
                float posX = _currentPage.ContentBox.X;
                float posY = _currentPage.ContentBox.Y;
                if (offsetLeft > 0) posX += offsetLeft;
                else if (offsetRight > 0) posX = _currentPage.ContentBox.Right - box.Width - offsetRight;
                if (offsetTop > 0) posY += offsetTop;
                else if (offsetBottom > 0) posY = _currentPage.ContentBox.Bottom - box.Height - offsetBottom;
                box.X = posX;
                box.Y = posY;
                // Do not advance _currentY — removed from normal flow
            }

            if (!string.IsNullOrWhiteSpace(box.TextContent))
            {
                var inlineBox = new InlineBox
                {
                    Text = box.TextContent,
                    Type = InlineBoxType.Text,
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

            if (_currentY > _currentPage.ContentBox.Bottom)
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

    private static float ParseLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        value = value.Trim().ToLowerInvariant();
        if (value == "0" || value == "0px" || value == "0pt" || value == "0mm") return 0;

        if (value is "thin") return 0.5f;
        if (value is "medium") return 1f;
        if (value is "thick") return 2f;

        if (value.EndsWith("mm")) return float.TryParse(value[..^2], out var v) ? v : 0;
        if (value.EndsWith("pt")) return float.TryParse(value[..^2], out var v) ? v * 0.3528f : 0;
        if (value.EndsWith("px")) return float.TryParse(value[..^2], out var v) ? v * 0.2646f : 0;
        if (value.EndsWith("cm")) return float.TryParse(value[..^2], out var v) ? v * 10f : 0;
        if (value.EndsWith("in")) return float.TryParse(value[..^2], out var v) ? v * 25.4f : 0;

        if (float.TryParse(value, out var num)) return num;

        return 0;
    }

    private static float ParseCssLength(string? value, float parentDimension)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        value = value.Trim().ToLowerInvariant();
        if (value == "auto") return 0;

        if (value.EndsWith("%"))
        {
            if (float.TryParse(value[..^1].Trim(), out var pct))
                return parentDimension * pct / 100f;
            return 0;
        }

        return ParseLength(value);
    }

    private static float GetFontSize(CssDeclarationBlock? style)
    {
        var val = style?.GetPropertyValue("font-size");
        if (val == null) return 10f;
        val = val.Trim().ToLowerInvariant();

        if (val.EndsWith("pt")) return float.TryParse(val[..^2], out var v) ? v : 10f;
        if (val.EndsWith("px")) return float.TryParse(val[..^2], out var v) ? v * 0.75f : 10f;
        if (val.EndsWith("mm")) return float.TryParse(val[..^2], out var v) ? v * 2.8346f : 10f;
        if (val.EndsWith("em")) return float.TryParse(val[..^2], out var v) ? v * 10f : 10f;
        if (val.EndsWith("%")) return float.TryParse(val[..^1], out var v) ? v * 0.1f : 10f;
        if (val.EndsWith("rem")) return float.TryParse(val[..^3], out var v) ? v * 10f : 10f;

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
