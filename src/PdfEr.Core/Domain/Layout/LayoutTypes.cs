using PdfEr.Core.Domain.Enums;
using PdfEr.Core.Domain.Styles;
using PdfEr.Core.Domain.ValueObjects;

namespace PdfEr.Core.Domain.Layout;

public sealed class DocumentLayout
{
    public List<PageLayout> Pages { get; } = new();
    public DocumentMargins DefaultMargins { get; set; } = DocumentMargins.Default;
    public PageSize DefaultPageSize { get; set; } = PageSize.FromFormat(PageFormat.A4);
    public PageOrientation DefaultOrientation { get; set; } = PageOrientation.Portrait;
}

public sealed class PageLayout
{
    public PageSize Size { get; set; }
    public PageOrientation Orientation { get; set; }
    public DocumentMargins Margins { get; set; }
    public ContentArea ContentBox { get; set; }
    public List<BlockBox> Blocks { get; } = new();
    public HeaderFooterBox? Header { get; set; }
    public HeaderFooterBox? Footer { get; set; }
    public int PageNumber { get; set; }
    public string? PageName { get; set; }
    public string? PageLabelPrefix { get; set; }
    public string? PageLabelStyle { get; set; }

    public PageLayout(PageSize size, PageOrientation orientation, DocumentMargins margins, int pageNumber)
    {
        Size = size;
        Orientation = orientation;
        Margins = margins;
        PageNumber = pageNumber;
        ContentBox = new ContentArea(
            margins.Left,
            margins.Top + margins.Header,
            size.WidthMillimeters - margins.Left - margins.Right,
            size.HeightMillimeters - margins.Top - margins.Bottom - margins.Header - margins.Footer
        );
    }
}

public readonly struct ContentArea
{
    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }

    public ContentArea(float x, float y, float width, float height)
    {
        X = x; Y = y; Width = width; Height = height;
    }

    public float Bottom => Y + Height;
    public float Right => X + Width;
}

public sealed class BlockBox
{
    public string? TagName { get; set; }
    public string? TextContent { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public CssDeclarationBlock? ComputedStyle { get; set; }
    public List<BlockBox> Children { get; } = new();
    public List<InlineBox> InlineContent { get; } = new();
    public BlockBoxType Type { get; set; } = BlockBoxType.Normal;
    public float PaddingTop { get; set; }
    public float PaddingBottom { get; set; }
    public float PaddingLeft { get; set; }
    public float PaddingRight { get; set; }
    public float MarginTop { get; set; }
    public float MarginBottom { get; set; }
    public float MarginLeft { get; set; }
    public float MarginRight { get; set; }
    public float BorderTop { get; set; }
    public float BorderBottom { get; set; }
    public float BorderLeft { get; set; }
    public float BorderRight { get; set; }
    public string? LinkUrl { get; set; }
    public string? AnchorName { get; set; }
    public float Opacity { get; set; } = 1f;
    public float MinWidth { get; set; }
    public float MaxWidth { get; set; }
    public float MinHeight { get; set; }
    public float MaxHeight { get; set; }
    public string? FlexDirection { get; set; }
    public string? JustifyContent { get; set; }
    public string? AlignItems { get; set; }
    public float FlexGap { get; set; }
    public string? FlexWrap { get; set; }

    public float ContentWidth => Width - PaddingLeft - PaddingRight - BorderLeft - BorderRight;
    public float ContentHeight => Height - PaddingTop - PaddingBottom - BorderTop - BorderBottom;
    public float TotalWidth => Width + MarginLeft + MarginRight;
    public float TotalHeight => Height + MarginTop + MarginBottom;
}

public enum BlockBoxType
{
    Normal,
    FloatLeft,
    FloatRight,
    Absolute,
    Relative,
    Table,
    TableRow,
    TableCell,
    InlineBlock,
    FlexContainer
}

public sealed class InlineBox
{
    public string? TagName { get; set; }
    public string? Text { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public CssDeclarationBlock? ComputedStyle { get; set; }
    public InlineBoxType Type { get; set; } = InlineBoxType.Text;
    public string? LinkUrl { get; set; }
    public string? ImageSource { get; set; }
    public byte[]? ImageData { get; set; }
    public int ImagePixelWidth { get; set; }
    public int ImagePixelHeight { get; set; }
}

public enum InlineBoxType
{
    Text,
    Image,
    Link,
    LineBreak,
    ForceBreak
}

public sealed class HeaderFooterBox
{
    public string? HtmlContent { get; set; }
    public float Margin { get; set; }
    public bool ShowOnFirstPage { get; set; } = true;
    public bool ShowOnOddPages { get; set; } = true;
    public bool ShowOnEvenPages { get; set; } = true;
    public BlockBox? RenderedContent { get; set; }
}
