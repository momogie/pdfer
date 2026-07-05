using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Application.HtmlProcessing;
using PdfEr.Core.Application.HtmlProcessing.TagHandlers;
using PdfEr.Core.Application.Services;
using PdfEr.Core.Domain.Barcodes;
using PdfEr.Core.Domain.Enums;
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
