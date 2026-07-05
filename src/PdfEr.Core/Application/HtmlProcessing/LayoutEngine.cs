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
    public float CurrentY => _currentY;
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

    private void AddNewPage(PdfConverterConfiguration config)
    {
        var size = PageSize.FromOrientation(
            PageSize.FromFormat(config.PageFormat), config.Orientation);

        _currentPage = new PageLayout(size, config.Orientation, config.GetMargins(), _document.Pages.Count + 1);
        _document.Pages.Add(_currentPage);
        _currentY = _currentPage.ContentBox.Y;
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

        return box;
    }

    public void LayoutBlock(BlockBox box, PdfConverterConfiguration config)
    {
        box.Y = _currentY;

        if (box.TagName is "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "p" or "div"
            or "li" or "ul" or "ol" or "blockquote" or "pre" or "section" or "article"
            or "header" or "footer" or "nav" or "main" or "td" or "th" or "hr")
        {
            _currentY += box.MarginTop;
            box.Y = _currentY;

            var fontSize = GetFontSize(box.ComputedStyle);
            if (box.Height <= 0)
                box.Height = fontSize * 1.3f + box.PaddingTop + box.PaddingBottom;

            if (!string.IsNullOrWhiteSpace(box.TextContent))
            {
                var inlineBox = new InlineBox
                {
                    Text = box.TextContent,
                    Type = InlineBoxType.Text,
                    X = box.X + box.PaddingLeft + box.BorderLeft,
                    Y = _currentY + box.PaddingTop,
                    Width = box.ContentWidth,
                    Height = fontSize * 1.3f,
                    ComputedStyle = box.ComputedStyle
                };
                box.InlineContent.Add(inlineBox);
            }

            _currentY += box.Height + box.MarginBottom;

            if (_currentY > _currentPage.ContentBox.Bottom)
            {
                AddNewPage(config);
                box.Y = _currentY;
                _currentY += box.Height + box.MarginBottom;
            }

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
