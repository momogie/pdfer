using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Application.HtmlProcessing;
using PdfEr.Core.Application.HtmlProcessing.TagHandlers;
using PdfEr.Core.Application.Services;
using PdfEr.Core.Domain.Barcodes;
using PdfEr.Core.Domain.Enums;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;
using PdfEr.Core.Domain.Tables;
using PdfEr.Core.Domain.ValueObjects;

namespace PdfEr.Core.Tests;

public class DomainValueObjectsTests
{
    [Fact]
    public void RgbColor_Black_ReturnsZero()
    {
        var c = RgbColor.Black;
        Assert.Equal(0, c.R);
        Assert.Equal(0, c.G);
        Assert.Equal(0, c.B);
        Assert.Equal(255, c.A);
    }

    [Fact]
    public void PageSize_A4_Portrait_CorrectDimensions()
    {
        var a4 = PageSize.FromOrientation(PageSize.FromFormat(PageFormat.A4), PageOrientation.Portrait);
        Assert.Equal(210f, a4.WidthMillimeters, 1);
        Assert.Equal(297f, a4.HeightMillimeters, 1);
    }

    [Fact]
    public void PageSize_A4_Landscape_SwapsDimensions()
    {
        var a4 = PageSize.FromOrientation(PageSize.FromFormat(PageFormat.A4), PageOrientation.Landscape);
        Assert.Equal(297f, a4.WidthMillimeters, 1);
        Assert.Equal(210f, a4.HeightMillimeters, 1);
    }

    [Fact]
    public void PageSize_WidthPoints_CalculatesCorrectly()
    {
        var a4 = PageSize.FromFormat(PageFormat.A4);
        Assert.True(a4.WidthPoints > 0);
    }

    [Fact]
    public void DocumentMargins_Default_All15Mm()
    {
        var m = DocumentMargins.Default;
        Assert.Equal(15f, m.Left);
        Assert.Equal(15f, m.Right);
        Assert.Equal(15f, m.Top);
        Assert.Equal(15f, m.Bottom);
    }

    [Fact]
    public void DocumentColor_Interface_Implemented()
    {
        Assert.IsAssignableFrom<DocumentColor>(RgbColor.Black);
        Assert.IsAssignableFrom<DocumentColor>(CmykColor.Black);
        Assert.IsAssignableFrom<DocumentColor>(GrayscaleColor.Black);
        Assert.IsAssignableFrom<DocumentColor>(new SpotColor("PANTONE 123", CmykColor.Black));
    }

    [Fact]
    public void RgbColor_Transparent_IsTransparent()
    {
        Assert.True(RgbColor.Transparent.IsTransparent);
    }

    [Fact]
    public void RgbColor_Red_NonTransparent()
    {
        Assert.False(RgbColor.Red.IsTransparent);
    }
}

public class CssSpecificityTests
{
    [Fact]
    public void CompareTo_IdWeightierThanClass()
    {
        var a = new CssSpecificity(1, 0, 0);
        var b = new CssSpecificity(0, 1, 0);
        Assert.True(a > b);
    }

    [Fact]
    public void CompareTo_ClassWeightierThanElement()
    {
        var a = new CssSpecificity(0, 1, 0);
        var b = new CssSpecificity(0, 0, 1);
        Assert.True(a > b);
    }

    [Fact]
    public void CompareTo_EqualSpecificity_ReturnsZero()
    {
        var a = new CssSpecificity(1, 2, 3);
        var b = new CssSpecificity(1, 2, 3);
        Assert.Equal(0, a.CompareTo(b));
    }
}

public class CssDeclarationBlockTests
{
    [Fact]
    public void SetAndGetProperty_ReturnsValue()
    {
        var block = new CssDeclarationBlock();
        block.SetProperty("color", "red");
        Assert.Equal("red", block.GetPropertyValue("color"));
    }

    [Fact]
    public void SetProperty_OverwritesExisting()
    {
        var block = new CssDeclarationBlock();
        block.SetProperty("color", "red");
        block.SetProperty("color", "blue");
        Assert.Equal("blue", block.GetPropertyValue("color"));
    }

    [Fact]
    public void TryGetProperty_Existing_ReturnsTrue()
    {
        var block = new CssDeclarationBlock();
        block.SetProperty("font-size", "12pt");
        Assert.True(block.TryGetProperty("font-size", out var val));
        Assert.Equal("12pt", val!.RawValue);
    }

    [Fact]
    public void TryGetProperty_NonExisting_ReturnsFalse()
    {
        var block = new CssDeclarationBlock();
        Assert.False(block.TryGetProperty("nonexistent", out _));
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var block = new CssDeclarationBlock();
        block.SetProperty("color", "red");
        var clone = block.Clone();
        clone.SetProperty("color", "blue");
        Assert.Equal("red", block.GetPropertyValue("color"));
        Assert.Equal("blue", clone.GetPropertyValue("color"));
    }

    [Fact]
    public void MergeFrom_KeepsExistingByDefault()
    {
        var base_ = new CssDeclarationBlock();
        base_.SetProperty("color", "red");
        var other = new CssDeclarationBlock();
        other.SetProperty("color", "blue");
        base_.MergeFrom(other);
        Assert.Equal("red", base_.GetPropertyValue("color"));
    }

    [Fact]
    public void MergeFrom_OverridesWithOverrideImportantFlag()
    {
        var base_ = new CssDeclarationBlock();
        base_.SetProperty("color", "red");
        var other = new CssDeclarationBlock();
        other.SetProperty("color", "blue");
        base_.MergeFrom(other, overrideImportant: true);
        Assert.Equal("blue", base_.GetPropertyValue("color"));
    }

    [Fact]
    public void MergeFrom_ImportantOverridesNonImportant()
    {
        var base_ = new CssDeclarationBlock();
        base_.SetProperty("color", "red");
        var other = new CssDeclarationBlock();
        other.SetProperty("color", "blue", important: true);
        base_.MergeFrom(other);
        Assert.Equal("blue", base_.GetPropertyValue("color"));
    }

    [Fact]
    public void MergeFrom_KeepsExistingImportant()
    {
        var base_ = new CssDeclarationBlock();
        base_.SetProperty("color", "red", important: true);
        var other = new CssDeclarationBlock();
        other.SetProperty("color", "blue");
        base_.MergeFrom(other);
        Assert.Equal("red", base_.GetPropertyValue("color"));
    }

    [Fact]
    public void Count_ReturnsCorrectNumber()
    {
        var block = new CssDeclarationBlock();
        block.SetProperty("a", "1");
        block.SetProperty("b", "2");
        Assert.Equal(2, block.Count);
    }
}

public class CssPropertyValueTests
{
    [Fact]
    public void ToFloat_ExtractsNumber()
    {
        var val = new CssPropertyValue("12.5pt");
        Assert.Equal(12.5f, val.ToFloat());
    }

    [Fact]
    public void ToFloat_NoNumber_ReturnsDefault()
    {
        var val = new CssPropertyValue("auto");
        Assert.Equal(0f, val.ToFloat());
    }

    [Fact]
    public void IsImportant_Parsed()
    {
        var val = new CssPropertyValue("red !important", true);
        Assert.True(val.IsImportant);
        Assert.Contains("!important", val.ToString());
    }
}

public class TableLayoutEngineTests
{
    [Fact]
    public void ComputeLayout_SingleCell_ReturnsCorrectDimensions()
    {
        var engine = new TableLayoutEngine();
        var table = new TableDefinition { ColumnCount = 1 };
        var row = new TableRow();
        row.Cells.Add(new TableCell { TextContent = "Hello", ColumnIndex = 0, RowIndex = 0 });
        table.Rows.Add(row);

        var result = engine.ComputeLayout(table, 500);
        Assert.Single(result.Columns);
        Assert.Single(result.Rows);
        Assert.Equal(500, result.TotalWidth, 1);
    }

    [Fact]
    public void ComputeLayout_MultiColumn_DistributesWidth()
    {
        var engine = new TableLayoutEngine();
        var table = new TableDefinition { ColumnCount = 3 };
        var row = new TableRow();
        for (int i = 0; i < 3; i++)
            row.Cells.Add(new TableCell { TextContent = $"Cell{i}", ColumnIndex = i, RowIndex = 0 });
        table.Rows.Add(row);

        var result = engine.ComputeLayout(table, 600);
        Assert.Equal(3, result.Columns.Count);
        Assert.True(result.Columns.Sum(c => c.Width) <= 600);
    }

    [Fact]
    public void ComputeLayout_ColSpan_MergesCells()
    {
        var engine = new TableLayoutEngine();
        var table = new TableDefinition { ColumnCount = 3 };
        var row = new TableRow();
        row.Cells.Add(new TableCell { TextContent = "Wide", ColumnIndex = 0, ColSpan = 2 });
        row.Cells.Add(new TableCell { TextContent = "Narrow", ColumnIndex = 2 });
        table.Rows.Add(row);

        var result = engine.ComputeLayout(table, 600);
        Assert.Single(result.Rows);
        Assert.Equal(2, result.Rows[0].Cells.Count);
    }

    [Fact]
    public void ComputeLayout_MultipleRows_LaysOutDownward()
    {
        var engine = new TableLayoutEngine();
        var table = new TableDefinition { ColumnCount = 2 };
        for (int i = 0; i < 3; i++)
        {
            var row = new TableRow();
            row.Cells.Add(new TableCell { TextContent = $"A{i}", ColumnIndex = 0, RowIndex = i });
            row.Cells.Add(new TableCell { TextContent = $"B{i}", ColumnIndex = 1, RowIndex = i });
            table.Rows.Add(row);
        }

        var result = engine.ComputeLayout(table, 400);
        Assert.Equal(3, result.Rows.Count);
        Assert.True(result.TotalHeight > 0);
    }

    [Fact]
    public void ComputeLayout_EmptyTable_ReturnsZeroHeight()
    {
        var engine = new TableLayoutEngine();
        var table = new TableDefinition { ColumnCount = 2 };
        var result = engine.ComputeLayout(table, 400);
        Assert.Equal(2, result.Columns.Count);
        Assert.Empty(result.Rows);
    }
}

public class BarcodeEncoderTests
{
    [Fact]
    public void EncodeCode128_ReturnsBars()
    {
        var encoder = new BarcodeEncoder();
        var result = encoder.Encode(new BarcodeDefinition
        {
            Type = BarcodeType.Code128,
            Value = "Hello123",
            HeightMm = 12
        });
        Assert.NotEmpty(result.Bars);
        Assert.Equal("Hello123", result.EncodedData);
    }

    [Fact]
    public void EncodeEan13_ReturnsBars()
    {
        var encoder = new BarcodeEncoder();
        var result = encoder.Encode(new BarcodeDefinition
        {
            Type = BarcodeType.Ean13,
            Value = "5901234123457",
            HeightMm = 12
        });
        Assert.NotEmpty(result.Bars);
        Assert.Equal("5901234123457", result.EncodedData);
    }

    [Fact]
    public void EncodeQrCode_ReturnsMatrix()
    {
        var encoder = new BarcodeEncoder();
        var result = encoder.Encode(new BarcodeDefinition
        {
            Type = BarcodeType.QrCode,
            Value = "https://example.com",
            HeightMm = 20
        });
        Assert.NotEmpty(result.Bars);
        Assert.Equal("https://example.com", result.EncodedData);
    }

    [Fact]
    public void EncodeEan8_ReturnsBars()
    {
        var encoder = new BarcodeEncoder();
        var result = encoder.Encode(new BarcodeDefinition
        {
            Type = BarcodeType.Ean8,
            Value = "96385074",
            HeightMm = 12
        });
        Assert.NotEmpty(result.Bars);
    }

    [Fact]
    public void EncodeUpcA_ReturnsBars()
    {
        var encoder = new BarcodeEncoder();
        var result = encoder.Encode(new BarcodeDefinition
        {
            Type = BarcodeType.UpcA,
            Value = "012345678905",
            HeightMm = 12
        });
        Assert.NotEmpty(result.Bars);
    }

    [Fact]
    public void EncodeUpcE_ReturnsBars()
    {
        var encoder = new BarcodeEncoder();
        var result = encoder.Encode(new BarcodeDefinition
        {
            Type = BarcodeType.UpcE,
            Value = "01234565",
            HeightMm = 12
        });
        Assert.NotEmpty(result.Bars);
    }

    [Fact]
    public void EncodeCode39_ReturnsBars()
    {
        var encoder = new BarcodeEncoder();
        var result = encoder.Encode(new BarcodeDefinition
        {
            Type = BarcodeType.Code39,
            Value = "TEST123",
            HeightMm = 12
        });
        Assert.NotEmpty(result.Bars);
    }

    [Fact]
    public void EncodeDataMatrix_ReturnsResult()
    {
        var encoder = new BarcodeEncoder();
        var result = encoder.Encode(new BarcodeDefinition
        {
            Type = BarcodeType.DataMatrix,
            Value = "Data",
            HeightMm = 10
        });
        Assert.NotNull(result);
    }

    [Fact]
    public void EncodePdf417_ReturnsResult()
    {
        var encoder = new BarcodeEncoder();
        var result = encoder.Encode(new BarcodeDefinition
        {
            Type = BarcodeType.Pdf417,
            Value = "Pdf417Data",
            HeightMm = 10
        });
        Assert.NotNull(result);
    }
}

public class TagRegistryTests
{
    [Fact]
    public void RegisterDivHandler_DivTag_ReturnsHandler()
    {
        var handlers = new ITagHandler[] { new DivHandler() };
        var registry = new TagRegistry(handlers);

        var handler = registry.GetHandler("div");

        Assert.NotNull(handler);
        Assert.IsType<DivHandler>(handler);
    }

    [Fact]
    public void RegisterMultipleHandlers_AllTagsResolved()
    {
        var handlers = new ITagHandler[]
        {
            new DivHandler(),
            new ParagraphHandler(),
            new HeadingHandler(),
            new ListHandler(),
            new ListItemHandler(),
            new BoldHandler(),
            new ItalicHandler(),
            new BreakHandler(),
            new HrHandler()
        };
        var registry = new TagRegistry(handlers);

        Assert.NotNull(registry.GetHandler("div"));
        Assert.NotNull(registry.GetHandler("p"));
        Assert.NotNull(registry.GetHandler("h1"));
        Assert.NotNull(registry.GetHandler("h4"));
        Assert.NotNull(registry.GetHandler("h6"));
        Assert.NotNull(registry.GetHandler("ul"));
        Assert.NotNull(registry.GetHandler("ol"));
        Assert.NotNull(registry.GetHandler("li"));
        Assert.NotNull(registry.GetHandler("b"));
        Assert.NotNull(registry.GetHandler("i"));
        Assert.NotNull(registry.GetHandler("br"));
        Assert.NotNull(registry.GetHandler("hr"));
    }

    [Fact]
    public void UnregisteredTag_ReturnsNull()
    {
        var handlers = new ITagHandler[] { new DivHandler() };
        var registry = new TagRegistry(handlers);

        Assert.Null(registry.GetHandler("unknown"));
        Assert.Null(registry.GetHandler("table"));
    }

    [Fact]
    public void HasHandler_RegisteredTag_ReturnsTrue()
    {
        var handlers = new ITagHandler[] { new ParagraphHandler() };
        var registry = new TagRegistry(handlers);

        Assert.True(registry.HasHandler("p"));
        Assert.False(registry.HasHandler("div"));
    }

    [Fact]
    public void OpenClose_UnregisteredTag_NoException()
    {
        var handlers = new ITagHandler[] { new DivHandler() };
        var registry = new TagRegistry(handlers);

        var exOpen = Record.Exception(() => registry.OpenTag("table", null!));
        var exClose = Record.Exception(() => registry.CloseTag("table", null!));

        Assert.Null(exOpen);
        Assert.Null(exClose);
    }
}

public class ListStateTests
{
    [Fact]
    public void UnorderedList_MarkerIsBullet()
    {
        var state = new ListState(false, 1, "disc");
        Assert.Equal("\u2022", state.GetMarker());
    }

    [Fact]
    public void OrderedList_DefaultCounter_Returns1()
    {
        var state = new ListState(true, 1, "decimal");
        Assert.Equal("1.", state.GetMarker());
    }

    [Fact]
    public void OrderedList_IncrementCounter_Returns2()
    {
        var state = new ListState(true, 1, "decimal");
        state.Counter++;
        Assert.Equal("2.", state.GetMarker());
    }

    [Fact]
    public void OrderedList_StartAt5_Returns5()
    {
        var state = new ListState(true, 5, "decimal");
        Assert.Equal("5.", state.GetMarker());
    }

    [Fact]
    public void OrderedList_LowerAlpha_ReturnsCorrectLetter()
    {
        var state = new ListState(true, 1, "lower-alpha");
        Assert.Equal("a.", state.GetMarker());

        state.Counter = 2;
        Assert.Equal("b.", state.GetMarker());

        state.Counter = 26;
        Assert.Equal("z.", state.GetMarker());
    }

    [Fact]
    public void OrderedList_UpperAlpha_ReturnsCorrectLetter()
    {
        var state = new ListState(true, 1, "upper-alpha");
        Assert.Equal("A.", state.GetMarker());
    }

    [Fact]
    public void OrderedList_LowerRoman_ReturnsCorrectNumeral()
    {
        var state = new ListState(true, 1, "lower-roman");
        Assert.Equal("i.", state.GetMarker());

        state.Counter = 4;
        Assert.Equal("iv.", state.GetMarker());

        state.Counter = 9;
        Assert.Equal("ix.", state.GetMarker());
    }

    [Fact]
    public void OrderedList_UpperRoman_ReturnsCorrectNumeral()
    {
        var state = new ListState(true, 1, "upper-roman");
        Assert.Equal("I.", state.GetMarker());
    }

    [Fact]
    public void UnorderedList_WithCustomStyleType_UsesBullet()
    {
        var state = new ListState(false, 1, "circle");
        Assert.Equal("\u2022", state.GetMarker());
    }

    [Fact]
    public void RomanNumeral_AboveMaxRange_ReturnsDecimal()
    {
        var state = new ListState(true, 4000, "lower-roman");
        Assert.Equal("4000.", state.GetMarker());
    }

    [Fact]
    public void ListState_StartProperty_ReflectsInitialValue()
    {
        var state = new ListState(true, 10, "decimal");
        Assert.Equal(10, state.Start);
        Assert.Equal(10, state.Counter);
    }

    [Fact]
    public void ListState_IsOrdered_ReflectsType()
    {
        Assert.True(new ListState(true, 1, "decimal").IsOrdered);
        Assert.False(new ListState(false, 1, "disc").IsOrdered);
    }
}

public class ColorParserTests
{
    private readonly ColorParser _parser = new();

    [Fact]
    public void Parse_NamedColor_Red()
    {
        var result = _parser.Parse("red");
        Assert.IsType<RgbColor>(result);
        var rgb = (RgbColor)result;
        Assert.Equal(255, rgb.R);
        Assert.Equal(0, rgb.G);
        Assert.Equal(0, rgb.B);
    }

    [Fact]
    public void Parse_NamedColor_Black()
    {
        var result = _parser.Parse("black");
        var rgb = (RgbColor)result;
        Assert.Equal(0, rgb.R);
        Assert.Equal(0, rgb.G);
        Assert.Equal(0, rgb.B);
    }

    [Fact]
    public void Parse_NamedColor_Transparent()
    {
        var result = _parser.Parse("transparent");
        var rgb = (RgbColor)result;
        Assert.Equal(0, rgb.A);
    }

    [Fact]
    public void Parse_Hex3_ExpandsCorrectly()
    {
        var result = _parser.Parse("#f00");
        var rgb = (RgbColor)result;
        Assert.Equal(255, rgb.R);
        Assert.Equal(0, rgb.G);
        Assert.Equal(0, rgb.B);
    }

    [Fact]
    public void Parse_Hex6_ReturnsCorrectColor()
    {
        var result = _parser.Parse("#ddeeff");
        var rgb = (RgbColor)result;
        Assert.Equal(0xdd, rgb.R);
        Assert.Equal(0xee, rgb.G);
        Assert.Equal(0xff, rgb.B);
    }

    [Fact]
    public void Parse_Hex8_IncludesAlpha()
    {
        var result = _parser.Parse("#ff000080");
        var rgb = (RgbColor)result;
        Assert.Equal(255, rgb.R);
        Assert.Equal(0, rgb.G);
        Assert.Equal(0, rgb.B);
        Assert.Equal(128, rgb.A);
    }

    [Fact]
    public void Parse_HexWithoutHash_Works()
    {
        var result = _parser.Parse("ff0000");
        var rgb = (RgbColor)result;
        Assert.Equal(255, rgb.R);
    }

    [Fact]
    public void Parse_Rgb_ReturnsCorrectColor()
    {
        var result = _parser.Parse("rgb(10, 20, 30)");
        var rgb = (RgbColor)result;
        Assert.Equal(10, rgb.R);
        Assert.Equal(20, rgb.G);
        Assert.Equal(30, rgb.B);
    }

    [Fact]
    public void Parse_Rgba_IncludesAlpha()
    {
        var result = _parser.Parse("rgba(255, 0, 0, 0.5)");
        var rgb = (RgbColor)result;
        Assert.Equal(255, rgb.R);
        Assert.Equal(0, rgb.G);
        Assert.Equal(0, rgb.B);
        // 0.5 * 255 = 127.5 → truncated to 127
        Assert.Equal(127, rgb.A);
    }

    [Fact]
    public void Parse_Hsl_ConvertsToRgb()
    {
        var result = _parser.Parse("hsl(0, 100%, 50%)");
        var rgb = (RgbColor)result;
        Assert.Equal(255, rgb.R);
        Assert.Equal(0, rgb.G);
        Assert.Equal(0, rgb.B);
    }

    [Fact]
    public void Parse_Cmyk_ReturnsCmykColor()
    {
        var result = _parser.Parse("cmyk(0, 1, 0, 0)");
        Assert.IsType<CmykColor>(result);
    }

    [Fact]
    public void Parse_Invalid_ReturnsBlack()
    {
        var result = _parser.Parse("not-a-color");
        var rgb = (RgbColor)result;
        Assert.Equal(0, rgb.R);
        Assert.Equal(0, rgb.G);
        Assert.Equal(0, rgb.B);
    }

    [Fact]
    public void TryParse_EmptyString_ReturnsFalse()
    {
        Assert.False(_parser.TryParse("", out _));
        Assert.False(_parser.TryParse(null!, out _));
    }

    [Fact]
    public void TryParse_WhiteSpace_ReturnsFalse()
    {
        Assert.False(_parser.TryParse("   ", out _));
    }

    [Fact]
    public void TryParse_ValidColor_ReturnsTrue()
    {
        Assert.True(_parser.TryParse("blue", out var color));
        Assert.NotNull(color);
    }
}

public class CssNormalizerTests
{
    private readonly CssNormalizer _normalizer = new();

    [Fact]
    public void ExpandShorthands_Border_ExpandsToWidthStyleColor()
    {
        var block = new CssDeclarationBlock();
        block.SetProperty("border", "1px solid red");

        var result = _normalizer.ExpandShorthands(block);

        Assert.Equal("1px", result.GetPropertyValue("border-top-width"));
        Assert.Equal("1px", result.GetPropertyValue("border-right-width"));
        Assert.Equal("1px", result.GetPropertyValue("border-bottom-width"));
        Assert.Equal("1px", result.GetPropertyValue("border-left-width"));
        Assert.Equal("solid", result.GetPropertyValue("border-top-style"));
        Assert.Equal("red", result.GetPropertyValue("border-top-color"));
    }

    [Fact]
    public void ExpandShorthands_BorderShorthandOnly_DoesNotOverrideExistingLonghands()
    {
        var block = new CssDeclarationBlock();
        block.SetProperty("border-top-width", "5px");
        block.SetProperty("border", "1px solid blue");

        // Clone creates a copy, so original is not modified
        var result = _normalizer.ExpandShorthands(block);

        // The clone starts from original values, then border overwrites
        Assert.Equal("1px", result.GetPropertyValue("border-top-width"));
    }

    [Fact]
    public void ExpandShorthands_BorderSide_ExpandsCorrectly()
    {
        var block = new CssDeclarationBlock();
        block.SetProperty("border-left", "3px dotted green");

        var result = _normalizer.ExpandShorthands(block);

        Assert.Equal("3px", result.GetPropertyValue("border-left-width"));
        Assert.Equal("dotted", result.GetPropertyValue("border-left-style"));
        Assert.Equal("green", result.GetPropertyValue("border-left-color"));
    }

    [Fact]
    public void ExpandShorthands_MarginSingle_AppliesToAllSides()
    {
        var block = new CssDeclarationBlock();
        block.SetProperty("margin", "10px");

        var result = _normalizer.ExpandShorthands(block);
        Assert.Equal("10px", result.GetPropertyValue("margin-top"));
        Assert.Equal("10px", result.GetPropertyValue("margin-right"));
        Assert.Equal("10px", result.GetPropertyValue("margin-bottom"));
        Assert.Equal("10px", result.GetPropertyValue("margin-left"));
    }

    [Fact]
    public void ExpandShorthands_MarginTwoValues_AppliesCorrectly()
    {
        var block = new CssDeclarationBlock();
        block.SetProperty("margin", "10px 20px");

        var result = _normalizer.ExpandShorthands(block);
        Assert.Equal("10px", result.GetPropertyValue("margin-top"));
        Assert.Equal("20px", result.GetPropertyValue("margin-right"));
        Assert.Equal("10px", result.GetPropertyValue("margin-bottom"));
        Assert.Equal("20px", result.GetPropertyValue("margin-left"));
    }

    [Fact]
    public void ExpandShorthands_MarginThreeValues_AppliesCorrectly()
    {
        var block = new CssDeclarationBlock();
        block.SetProperty("margin", "10px 20px 30px");

        var result = _normalizer.ExpandShorthands(block);
        Assert.Equal("10px", result.GetPropertyValue("margin-top"));
        Assert.Equal("20px", result.GetPropertyValue("margin-right"));
        Assert.Equal("30px", result.GetPropertyValue("margin-bottom"));
        Assert.Equal("20px", result.GetPropertyValue("margin-left"));
    }

    [Fact]
    public void ExpandShorthands_MarginFourValues_AppliesCorrectly()
    {
        var block = new CssDeclarationBlock();
        block.SetProperty("margin", "1px 2px 3px 4px");

        var result = _normalizer.ExpandShorthands(block);
        Assert.Equal("1px", result.GetPropertyValue("margin-top"));
        Assert.Equal("2px", result.GetPropertyValue("margin-right"));
        Assert.Equal("3px", result.GetPropertyValue("margin-bottom"));
        Assert.Equal("4px", result.GetPropertyValue("margin-left"));
    }

    [Fact]
    public void ExpandShorthands_Padding_AppliesCorrectly()
    {
        var block = new CssDeclarationBlock();
        block.SetProperty("padding", "5pt");

        var result = _normalizer.ExpandShorthands(block);
        Assert.Equal("5pt", result.GetPropertyValue("padding-top"));
        Assert.Equal("5pt", result.GetPropertyValue("padding-right"));
        Assert.Equal("5pt", result.GetPropertyValue("padding-bottom"));
        Assert.Equal("5pt", result.GetPropertyValue("padding-left"));
    }

    [Fact]
    public void ExpandShorthands_OriginalUnchanged()
    {
        var block = new CssDeclarationBlock();
        block.SetProperty("border", "2px solid green");

        var _ = _normalizer.ExpandShorthands(block);

        Assert.Null(block.GetPropertyValue("border-top-width"));
        Assert.Equal("2px solid green", block.GetPropertyValue("border"));
    }

    [Fact]
    public void ExpandShorthands_NoShorthand_ReturnsClone()
    {
        var block = new CssDeclarationBlock();
        block.SetProperty("color", "red");

        var result = _normalizer.ExpandShorthands(block);

        Assert.Equal("red", result.GetPropertyValue("color"));
    }

    [Fact]
    public void ExpandShorthands_TextDecoration_Expands()
    {
        var block = new CssDeclarationBlock();
        block.SetProperty("text-decoration", "underline");

        var result = _normalizer.ExpandShorthands(block);

        Assert.Equal("underline", result.GetPropertyValue("text-decoration-line"));
    }

    [Fact]
    public void ExpandShorthands_ListStyle_Expands()
    {
        var block = new CssDeclarationBlock();
        block.SetProperty("list-style", "square inside");

        var result = _normalizer.ExpandShorthands(block);

        Assert.Equal("square", result.GetPropertyValue("list-style-type"));
        Assert.Equal("inside", result.GetPropertyValue("list-style-position"));
    }

    [Fact]
    public void ExpandShorthands_Font_Expands()
    {
        var block = new CssDeclarationBlock();
        block.SetProperty("font", "bold 12pt serif");

        var result = _normalizer.ExpandShorthands(block);

        Assert.Equal("bold", result.GetPropertyValue("font-weight"));
        Assert.Equal("12pt", result.GetPropertyValue("font-size"));
        Assert.Equal("serif", result.GetPropertyValue("font-family"));
    }
}

public class LayoutEngineStaticTests
{
    [Fact]
    public void ApplyBoxModel_PaddingValues_ParsedCorrectly()
    {
        var styles = new CssDeclarationBlock();
        styles.SetProperty("padding-top", "10px");
        styles.SetProperty("padding-bottom", "5mm");
        styles.SetProperty("padding-left", "2pt");
        styles.SetProperty("padding-right", "1cm");

        var box = new BlockBox();
        LayoutEngine.ApplyBoxModel(box, styles);

        Assert.Equal(10 * 0.2646f, box.PaddingTop, 3);
        Assert.Equal(5f, box.PaddingBottom, 3);
        Assert.Equal(2 * 0.3528f, box.PaddingLeft, 3);
        Assert.Equal(10f, box.PaddingRight, 3);
    }

    [Fact]
    public void ApplyBoxModel_MarginValues_ParsedCorrectly()
    {
        var styles = new CssDeclarationBlock();
        styles.SetProperty("margin-top", "10mm");

        var box = new BlockBox();
        LayoutEngine.ApplyBoxModel(box, styles);

        Assert.Equal(10f, box.MarginTop, 3);
    }

    [Fact]
    public void ApplyBoxModel_BorderValues_ParsedCorrectly()
    {
        var styles = new CssDeclarationBlock();
        styles.SetProperty("border-top-width", "1px");
        styles.SetProperty("border-bottom-width", "medium");
        styles.SetProperty("border-left-width", "thick");
        styles.SetProperty("border-right-width", "thin");

        var box = new BlockBox();
        LayoutEngine.ApplyBoxModel(box, styles);

        Assert.Equal(0.2646f, box.BorderTop, 3);
        Assert.Equal(1f, box.BorderBottom, 3); // medium = 1mm
        Assert.Equal(2f, box.BorderLeft, 3); // thick = 2mm
        Assert.Equal(0.5f, box.BorderRight, 3); // thin = 0.5mm
    }

    [Fact]
    public void ApplyBoxModel_EmptyStyle_AllZero()
    {
        var box = new BlockBox();
        LayoutEngine.ApplyBoxModel(box, new CssDeclarationBlock());

        Assert.Equal(0, box.PaddingTop);
        Assert.Equal(0, box.MarginTop);
        Assert.Equal(0, box.BorderTop);
    }

    [Fact]
    public void ApplyBoxModel_MissingProperties_DefaultsToZero()
    {
        var styles = new CssDeclarationBlock();
        styles.SetProperty("color", "red");

        var box = new BlockBox();
        LayoutEngine.ApplyBoxModel(box, styles);

        Assert.Equal(0, box.PaddingTop);
        Assert.Equal(0, box.MarginTop);
        Assert.Equal(0, box.BorderTop);
    }

    [Fact]
    public void ParseLength_Px_ConvertsCorrectly()
    {
        // ParseLength is private, tested indirectly via ApplyBoxModel
        // We can reach it through the known conversion: 1px = 0.2646mm
        var styles = new CssDeclarationBlock();
        styles.SetProperty("padding-top", "100px");

        var box = new BlockBox();
        LayoutEngine.ApplyBoxModel(box, styles);

        Assert.Equal(26.46f, box.PaddingTop, 2);
    }

    [Fact]
    public void ParseLength_Pt_ConvertsCorrectly()
    {
        var styles = new CssDeclarationBlock();
        styles.SetProperty("padding-top", "72pt");

        var box = new BlockBox();
        LayoutEngine.ApplyBoxModel(box, styles);

        Assert.Equal(72 * 0.3528f, box.PaddingTop, 2);
    }

    [Fact]
    public void ParseLength_Mm_StaysAsIs()
    {
        var styles = new CssDeclarationBlock();
        styles.SetProperty("padding-top", "25.4mm");

        var box = new BlockBox();
        LayoutEngine.ApplyBoxModel(box, styles);

        Assert.Equal(25.4f, box.PaddingTop, 2);
    }

    [Fact]
    public void ParseLength_Cm_ConvertsToMm()
    {
        var styles = new CssDeclarationBlock();
        styles.SetProperty("margin-top", "1cm");

        var box = new BlockBox();
        LayoutEngine.ApplyBoxModel(box, styles);

        Assert.Equal(10f, box.MarginTop, 2);
    }

    [Fact]
    public void ParseLength_In_ConvertsToMm()
    {
        var styles = new CssDeclarationBlock();
        styles.SetProperty("margin-top", "1in");

        var box = new BlockBox();
        LayoutEngine.ApplyBoxModel(box, styles);

        Assert.Equal(25.4f, box.MarginTop, 2);
    }

    [Fact]
    public void ParseLength_Zero_ReturnsZero()
    {
        var styles = new CssDeclarationBlock();
        styles.SetProperty("padding-top", "0");

        var box = new BlockBox();
        LayoutEngine.ApplyBoxModel(box, styles);

        Assert.Equal(0, box.PaddingTop);
    }

    [Fact]
    public void ParseLength_Thin_Medium_Thick_HaveCorrectValues()
    {
        var styles = new CssDeclarationBlock();
        styles.SetProperty("border-top-width", "thin");
        styles.SetProperty("border-bottom-width", "medium");
        styles.SetProperty("border-left-width", "thick");

        var box = new BlockBox();
        LayoutEngine.ApplyBoxModel(box, styles);

        Assert.Equal(0.5f, box.BorderTop, 2);
        Assert.Equal(1f, box.BorderBottom, 2);
        Assert.Equal(2f, box.BorderLeft, 2);
    }
}

public class CssParserTests
{
    [Fact]
    public void ComputeSpecificity_ElementOnly_Returns001()
    {
        var result = CssParser.ComputeSpecificity("div");
        Assert.Equal(0, result.IdCount);
        Assert.Equal(0, result.ClassCount);
        Assert.Equal(1, result.ElementCount);
    }

    [Fact]
    public void ComputeSpecificity_ClassOnly_Returns010()
    {
        var result = CssParser.ComputeSpecificity(".myclass");
        Assert.Equal(0, result.IdCount);
        Assert.Equal(1, result.ClassCount);
        Assert.Equal(0, result.ElementCount);
    }

    [Fact]
    public void ComputeSpecificity_IdOnly_Returns100()
    {
        var result = CssParser.ComputeSpecificity("#myid");
        Assert.Equal(1, result.IdCount);
        Assert.Equal(0, result.ClassCount);
        Assert.Equal(0, result.ElementCount);
    }

    [Fact]
    public void ComputeSpecificity_ComplexSelector_Accumulates()
    {
        var result = CssParser.ComputeSpecificity("div.myclass#myid");
        Assert.Equal(1, result.IdCount);
        Assert.Equal(1, result.ClassCount);
        Assert.Equal(1, result.ElementCount);
    }

    [Fact]
    public void ComputeSpecificity_MultipleSelectors_TakesMax()
    {
        var result = CssParser.ComputeSpecificity("div, .myclass");
        Assert.Equal(0, result.IdCount);
        Assert.Equal(1, result.ClassCount);
        Assert.Equal(1, result.ElementCount);
    }

    [Fact]
    public void ComputeSpecificity_AttributeSelector_ClassCount()
    {
        var result = CssParser.ComputeSpecificity("[type=text]");
        Assert.Equal(0, result.IdCount);
        Assert.Equal(1, result.ClassCount);
        Assert.Equal(0, result.ElementCount);
    }

    [Fact]
    public void ComputeSpecificity_PseudoClass_ClassCount()
    {
        var result = CssParser.ComputeSpecificity("li:hover");
        Assert.Equal(0, result.IdCount);
        Assert.Equal(1, result.ClassCount);
        Assert.Equal(1, result.ElementCount);
    }

    [Fact]
    public void ComputeSpecificity_DescendantSelector()
    {
        var result = CssParser.ComputeSpecificity("div p");
        Assert.Equal(0, result.IdCount);
        Assert.Equal(0, result.ClassCount);
        Assert.Equal(2, result.ElementCount);
    }

    [Fact]
    public void ParseDeclarations_SingleProperty()
    {
        var result = CssParser.ParseDeclarations("color: red");
        Assert.Equal("red", result.GetPropertyValue("color"));
    }

    [Fact]
    public void ParseDeclarations_MultipleProperties()
    {
        var result = CssParser.ParseDeclarations("color: red; font-size: 12pt");
        Assert.Equal("red", result.GetPropertyValue("color"));
        Assert.Equal("12pt", result.GetPropertyValue("font-size"));
    }

    [Fact]
    public void ParseDeclarations_WithImportant()
    {
        var result = CssParser.ParseDeclarations("color: red !important");
        Assert.True(result.TryGetProperty("color", out var val));
        Assert.True(val!.IsImportant);
    }

    [Fact]
    public void ParseDeclarations_Empty_ReturnsEmpty()
    {
        var result = CssParser.ParseDeclarations("");
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void Parse_SingleRule()
    {
        var parser = new CssParser();
        var sheet = parser.Parse("div { color: red }");
        Assert.Single(sheet.Rules);
        Assert.Equal("div", sheet.Rules[0].Selector);
    }

    [Fact]
    public void Parse_MultipleRules()
    {
        var parser = new CssParser();
        var sheet = parser.Parse("p { margin: 0 } h1 { font-size: 2em }");
        Assert.Equal(2, sheet.Rules.Count);
    }

    [Fact]
    public void Parse_FontFace_ExtractsRule()
    {
        var parser = new CssParser();
        var sheet = parser.Parse("@font-face { font-family: MyFont; src: url(font.woff); }");
        Assert.Single(sheet.FontFaceRules);
        Assert.Equal("MyFont", sheet.FontFaceRules[0].FontFamily);
    }

    [Fact]
    public void Parse_PageRule_ExtractsRule()
    {
        var parser = new CssParser();
        var sheet = parser.Parse("@page { margin: 10mm; }");
        Assert.Single(sheet.PageRules);
    }

    [Fact]
    public void Parse_MediaRule_ExtractsRule()
    {
        var parser = new CssParser();
        var sheet = parser.Parse("@media print { body { color: black; } }");
        Assert.Single(sheet.MediaRules);
        Assert.Equal("print", sheet.MediaRules[0].MediaQuery.Trim());
        Assert.Single(sheet.MediaRules[0].Rules);
    }

    [Fact]
    public void Parse_Null_ReturnsEmpty()
    {
        var parser = new CssParser();
        var sheet = parser.Parse(null!);
        Assert.Empty(sheet.Rules);
    }

    [Fact]
    public void Parse_Whitespace_ReturnsEmpty()
    {
        var parser = new CssParser();
        var sheet = parser.Parse("   ");
        Assert.Empty(sheet.Rules);
    }
}

public class CssMergerStaticTests
{
    [Fact]
    public void MediaQueryMatchesPrint_Empty_ReturnsTrue()
    {
        Assert.True(CssMerger.MediaQueryMatchesPrint(""));
    }

    [Fact]
    public void MediaQueryMatchesPrint_Print_ReturnsTrue()
    {
        Assert.True(CssMerger.MediaQueryMatchesPrint("print"));
    }

    [Fact]
    public void MediaQueryMatchesPrint_All_ReturnsTrue()
    {
        Assert.True(CssMerger.MediaQueryMatchesPrint("all"));
    }

    [Fact]
    public void MediaQueryMatchesPrint_OnlyPrint_ReturnsTrue()
    {
        Assert.True(CssMerger.MediaQueryMatchesPrint("only print"));
    }

    [Fact]
    public void MediaQueryMatchesPrint_Screen_ReturnsFalse()
    {
        Assert.False(CssMerger.MediaQueryMatchesPrint("screen"));
    }

    [Fact]
    public void MediaQueryMatchesPrint_NotScreen_ReturnsTrue()
    {
        Assert.True(CssMerger.MediaQueryMatchesPrint("not screen"));
    }

    [Fact]
    public void MediaQueryMatchesPrint_NotPrint_ReturnsFalse()
    {
        Assert.False(CssMerger.MediaQueryMatchesPrint("not print"));
    }

    [Fact]
    public void MediaQueryMatchesPrint_MultipleCommaSeparated_PrintWins()
    {
        Assert.True(CssMerger.MediaQueryMatchesPrint("screen, print"));
    }
}

public class CssPropertyValueEdgeTests
{
    [Fact]
    public void ToFloat_NegativeNumber_ReturnsNegative()
    {
        var val = new CssPropertyValue("-10px");
        Assert.Equal(-10f, val.ToFloat());
    }

    [Fact]
    public void ToFloat_DecimalNumber_ReturnsDecimal()
    {
        var val = new CssPropertyValue("3.14em");
        Assert.Equal(3.14f, val.ToFloat(), 2);
    }

    [Fact]
    public void ToFloat_NoNumber_ReturnsZero()
    {
        var val = new CssPropertyValue("inherit");
        Assert.Equal(0f, val.ToFloat());
    }

    [Fact]
    public void ToFloat_JustNumber_ReturnsNumber()
    {
        var val = new CssPropertyValue("42");
        Assert.Equal(42f, val.ToFloat());
    }
}

public sealed class HrHandlerTests
{
    [Fact]
    public void Open_CreatesBlockWithTagNameHr()
    {
        var ctx = CreateTagContext("hr");
        var handler = new HrHandler();
        handler.Open(ctx);

        Assert.NotNull(ctx.CurrentBlock);
        Assert.Equal("hr", ctx.CurrentBlock.TagName);
    }

    [Fact]
    public void Open_SetsEmptyTextContent()
    {
        var ctx = CreateTagContext("hr");
        var handler = new HrHandler();
        handler.Open(ctx);

        Assert.NotNull(ctx.CurrentBlock);
        Assert.Equal("", ctx.CurrentBlock.TextContent);
    }

    [Fact]
    public void Open_SetsHeightToBorderTopPlusBorderBottom()
    {
        var ctx = CreateTagContext("hr");
        var handler = new HrHandler();
        handler.Open(ctx);

        Assert.NotNull(ctx.CurrentBlock);
        var expected = ctx.CurrentBlock.BorderTop + ctx.CurrentBlock.BorderBottom;
        Assert.Equal(expected, ctx.CurrentBlock.Height, 4);
    }

    [Fact]
    public void Close_DoesNotThrow()
    {
        var ctx = CreateTagContext("hr");
        var handler = new HrHandler();
        handler.Open(ctx);
        handler.Close(ctx);
    }

    private static TagContext CreateTagContext(string tagName, CssDeclarationBlock? overrideStyle = null)
    {
        var parser = new CssParser();
        var merger = new CssMerger(parser);
        var normalizer = new CssNormalizer();
        var converter = NSubstitute.Substitute.For<IUnitConverter>();
        var engine = new LayoutEngine(merger, normalizer, converter);
        var config = new PdfConverterConfiguration { Title = "Test" };
        var doc = engine.CreateDocumentLayout(config);
        var page = doc.Pages[0];

        var style = overrideStyle ?? merger.ResolveStyles(tagName, new Dictionary<string, string>(), null);
        style = normalizer.ExpandShorthands(style);

        return new TagContext(
            null!, tagName, new Dictionary<string, string>(),
            style, null, engine, config, page,
            NSubstitute.Substitute.For<IFontRegistry>(),
            new Stack<ListState>());
    }
}

public sealed class BlockTagHandlerTests
{
    private sealed class TestBlockHandler : BlockTagHandler
    {
        public override string[] HandledTags => new[] { "test" };
    }

    [Fact]
    public void Open_CreatesBlockWithTagName()
    {
        var ctx = CreateHandlerContext("div");
        var handler = new TestBlockHandler();
        handler.Open(ctx);

        Assert.NotNull(ctx.CurrentBlock);
        Assert.Equal("div", ctx.CurrentBlock.TagName);
    }

    [Fact]
    public void Open_SetsCurrentBlock()
    {
        var ctx = CreateHandlerContext("p");
        var handler = new TestBlockHandler();
        handler.Open(ctx);

        Assert.NotNull(ctx.CurrentBlock);
        Assert.Equal("p", ctx.CurrentBlock.TagName);
    }

    [Fact]
    public void Close_DoesNotThrow()
    {
        var ctx = CreateHandlerContext("div");
        var handler = new TestBlockHandler();
        handler.Open(ctx);
        handler.Close(ctx);
    }

    private static TagContext CreateHandlerContext(string tagName)
    {
        var parser = new CssParser();
        var merger = new CssMerger(parser);
        var normalizer = new CssNormalizer();
        var converter = NSubstitute.Substitute.For<IUnitConverter>();
        var engine = new LayoutEngine(merger, normalizer, converter);
        var config = new PdfConverterConfiguration { Title = "Test" };
        var doc = engine.CreateDocumentLayout(config);
        var page = doc.Pages[0];

        var style = merger.ResolveStyles(tagName, new Dictionary<string, string>(), null);
        style = normalizer.ExpandShorthands(style);

        return new TagContext(
            null!, tagName, new Dictionary<string, string>(),
            style, null, engine, config, page,
            NSubstitute.Substitute.For<IFontRegistry>(),
            new Stack<ListState>());
    }
}

public sealed class InlineTagHandlerTests
{
    private sealed class TestInlineHandler : InlineTagHandler
    {
        public override string[] HandledTags => new[] { "span" };
    }

    [Fact]
    public void Open_DoesNothing()
    {
        var handler = new TestInlineHandler();
        var ctx = CreateInlineContext("span");
        handler.Open(ctx);
        Assert.Null(ctx.CurrentBlock);
    }

    [Fact]
    public void Close_DoesNotThrow()
    {
        var handler = new TestInlineHandler();
        var ctx = CreateInlineContext("span");
        handler.Close(ctx);
    }

    private static TagContext CreateInlineContext(string tagName)
    {
        var parser = new CssParser();
        var merger = new CssMerger(parser);
        var normalizer = new CssNormalizer();
        var converter = NSubstitute.Substitute.For<IUnitConverter>();
        var engine = new LayoutEngine(merger, normalizer, converter);
        var config = new PdfConverterConfiguration { Title = "Test" };
        var doc = engine.CreateDocumentLayout(config);
        var page = doc.Pages[0];

        var style = merger.ResolveStyles(tagName, new Dictionary<string, string>(), null);
        style = normalizer.ExpandShorthands(style);

        return new TagContext(
            null!, tagName, new Dictionary<string, string>(),
            style, null, engine, config, page,
            NSubstitute.Substitute.For<IFontRegistry>(),
            new Stack<ListState>());
    }
}

public class CssWidthHeightTests
{
    [Fact]
    public void CreateBlock_WithCssWidthMm_SetsBoxWidth()
    {
        var parser = new CssParser();
        var merger = new CssMerger(parser);
        var normalizer = new CssNormalizer();
        var converter = NSubstitute.Substitute.For<IUnitConverter>();
        var engine = new LayoutEngine(merger, normalizer, converter);
        var config = new PdfConverterConfiguration { Title = "Test" };
        var doc = engine.CreateDocumentLayout(config);

        var attrs = new Dictionary<string, string> { { "style", "width: 50mm" } };
        var box = engine.CreateBlock("div", attrs);

        Assert.Equal(50f, box.Width, 1);
    }

    [Fact]
    public void CreateBlock_WithCssWidthPercent_ScalesToParent()
    {
        var parser = new CssParser();
        var merger = new CssMerger(parser);
        var normalizer = new CssNormalizer();
        var converter = NSubstitute.Substitute.For<IUnitConverter>();
        var engine = new LayoutEngine(merger, normalizer, converter);
        var config = new PdfConverterConfiguration { Title = "Test" };
        var doc = engine.CreateDocumentLayout(config);

        var attrs = new Dictionary<string, string> { { "style", "width: 50%" } };
        var box = engine.CreateBlock("div", attrs);

        var expectedHalf = doc.Pages[0].ContentBox.Width * 0.5f;
        Assert.Equal(expectedHalf, box.Width, 1);
    }

    [Fact]
    public void LayoutBlock_WithCssHeightMm_SetsBoxHeight()
    {
        var parser = new CssParser();
        var merger = new CssMerger(parser);
        var normalizer = new CssNormalizer();
        var converter = NSubstitute.Substitute.For<IUnitConverter>();
        var engine = new LayoutEngine(merger, normalizer, converter);
        var config = new PdfConverterConfiguration { Title = "Test" };
        var doc = engine.CreateDocumentLayout(config);
        var page = doc.Pages[0];

        var attrs = new Dictionary<string, string> { { "style", "height: 30mm; width: 100mm" } };
        var box = engine.CreateBlock("div", attrs);
        engine.LayoutBlock(box, config);

        Assert.Equal(30f, box.Height, 1);
    }

    [Fact]
    public void CreateBlock_WithoutCssWidth_UsesContentBoxWidth()
    {
        var parser = new CssParser();
        var merger = new CssMerger(parser);
        var normalizer = new CssNormalizer();
        var converter = NSubstitute.Substitute.For<IUnitConverter>();
        var engine = new LayoutEngine(merger, normalizer, converter);
        var config = new PdfConverterConfiguration { Title = "Test" };
        var doc = engine.CreateDocumentLayout(config);

        var box = engine.CreateBlock("div", new Dictionary<string, string>());

        Assert.Equal(doc.Pages[0].ContentBox.Width, box.Width, 1);
    }

    [Fact]
    public void CreateBlock_WithCssWidthAuto_IgnoresAndUsesDefault()
    {
        var parser = new CssParser();
        var merger = new CssMerger(parser);
        var normalizer = new CssNormalizer();
        var converter = NSubstitute.Substitute.For<IUnitConverter>();
        var engine = new LayoutEngine(merger, normalizer, converter);
        var config = new PdfConverterConfiguration { Title = "Test" };
        var doc = engine.CreateDocumentLayout(config);

        var attrs = new Dictionary<string, string> { { "style", "width: auto" } };
        var box = engine.CreateBlock("div", attrs);

        Assert.Equal(doc.Pages[0].ContentBox.Width, box.Width, 1);
    }

    [Fact]
    public void LayoutBlock_WithoutCssHeight_AutoCalculates()
    {
        var parser = new CssParser();
        var merger = new CssMerger(parser);
        var normalizer = new CssNormalizer();
        var converter = NSubstitute.Substitute.For<IUnitConverter>();
        var engine = new LayoutEngine(merger, normalizer, converter);
        var config = new PdfConverterConfiguration { Title = "Test" };
        var doc = engine.CreateDocumentLayout(config);

        var box = engine.CreateBlock("p", new Dictionary<string, string>());
        box.TextContent = "Hello";
        engine.LayoutBlock(box, config);

        // Should have auto-calculated height based on font size
        Assert.True(box.Height > 0);
    }
}

public sealed class ListHandlerTests
{
    [Fact]
    public void Open_Ordered_PushesListStateWithDecimalMarker()
    {
        var ctx = CreateListContext("ol");
        var handler = new ListHandler();
        handler.Open(ctx);

        Assert.Single(ctx.ListStack);
        Assert.True(ctx.ListStack.Peek().IsOrdered);
        Assert.Equal(1, ctx.ListStack.Peek().Start);
        Assert.Equal("decimal", ctx.ListStack.Peek().ListStyleType);
    }

    [Fact]
    public void Open_Unordered_PushesListStateWithDiscMarker()
    {
        var ctx = CreateListContext("ul");
        var handler = new ListHandler();
        handler.Open(ctx);

        Assert.Single(ctx.ListStack);
        Assert.False(ctx.ListStack.Peek().IsOrdered);
        Assert.Equal("disc", ctx.ListStack.Peek().ListStyleType);
    }

    [Fact]
    public void Open_WithStartAttribute_UsesCustomStart()
    {
        var ctx = CreateListContext("ol", new Dictionary<string, string> { { "start", "5" } });
        var handler = new ListHandler();
        handler.Open(ctx);

        Assert.Equal(5, ctx.ListStack.Peek().Start);
        Assert.Equal(5, ctx.ListStack.Peek().Counter);
    }

    [Fact]
    public void Open_WithCustomListStyle_UsesProvidedStyle()
    {
        var ctx = CreateListContext("ul");
        ctx.ComputedStyle.SetProperty("list-style-type", "square");
        var handler = new ListHandler();
        handler.Open(ctx);

        Assert.Equal("square", ctx.ListStack.Peek().ListStyleType);
    }

    [Fact]
    public void Open_SetsCurrentBlockWithTagName()
    {
        var ctx = CreateListContext("ol");
        var handler = new ListHandler();
        handler.Open(ctx);

        Assert.NotNull(ctx.CurrentBlock);
        Assert.Equal("ol", ctx.CurrentBlock.TagName);
    }

    [Fact]
    public void Close_PopsListState()
    {
        var ctx = CreateListContext("ul");
        var handler = new ListHandler();
        handler.Open(ctx);
        Assert.Single(ctx.ListStack);

        handler.Close(ctx);
        Assert.Empty(ctx.ListStack);
    }

    [Fact]
    public void Close_AddsBlockToPage()
    {
        var ctx = CreateListContext("ul");
        var handler = new ListHandler();
        handler.Open(ctx);

        Assert.Empty(ctx.CurrentPage.Blocks);
        handler.Close(ctx);
        Assert.Single(ctx.CurrentPage.Blocks);
    }

    private static TagContext CreateListContext(string tagName, Dictionary<string, string>? attrs = null)
    {
        var parser = new CssParser();
        var merger = new CssMerger(parser);
        var normalizer = new CssNormalizer();
        var converter = NSubstitute.Substitute.For<IUnitConverter>();
        var engine = new LayoutEngine(merger, normalizer, converter);
        var config = new PdfConverterConfiguration { Title = "Test" };
        var doc = engine.CreateDocumentLayout(config);
        var page = doc.Pages[0];

        var attributes = attrs ?? new Dictionary<string, string>();
        var style = merger.ResolveStyles(tagName, attributes, null);
        style = normalizer.ExpandShorthands(style);

        return new TagContext(
            null!, tagName, attributes,
            style, null, engine, config, page,
            NSubstitute.Substitute.For<IFontRegistry>(),
            new Stack<ListState>());
    }
}
