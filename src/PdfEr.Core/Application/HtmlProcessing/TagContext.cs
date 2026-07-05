using AngleSharp.Dom;
using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;
using PdfEr.Core.Domain.Tables;
using PdfEr.Core.Domain.Typography;

namespace PdfEr.Core.Application.HtmlProcessing;

public sealed class TagContext
{
    public IElement Element { get; }
    public string TagName { get; }
    public Dictionary<string, string> Attributes { get; }
    public CssDeclarationBlock ComputedStyle { get; }
    public CssDeclarationBlock? ParentStyle { get; }
    public LayoutEngine LayoutEngine { get; }
    public PdfConverterConfiguration Config { get; }
    public PageLayout CurrentPage { get; set; }
    public BlockBox? CurrentBlock { get; set; }
    public IFontRegistry FontRegistry { get; }
    public Stack<ListState> ListStack { get; set; }
    public TableDefHolder? TableDef { get; set; }

    public TagContext(
        IElement element,
        string tagName,
        Dictionary<string, string> attributes,
        CssDeclarationBlock computedStyle,
        CssDeclarationBlock? parentStyle,
        LayoutEngine layoutEngine,
        PdfConverterConfiguration config,
        PageLayout currentPage,
        IFontRegistry fontRegistry,
        Stack<ListState> listStack,
        TableDefHolder? tableDef = null)
    {
        Element = element;
        TagName = tagName;
        Attributes = attributes;
        ComputedStyle = computedStyle;
        ParentStyle = parentStyle;
        LayoutEngine = layoutEngine;
        Config = config;
        CurrentPage = currentPage;
        FontRegistry = fontRegistry;
        ListStack = listStack;
        TableDef = tableDef;
    }
}

public sealed class TableDefHolder
{
    public TableDefinition? Current { get; set; }
}

public sealed class ListState
{
    public bool IsOrdered { get; }
    public int Counter { get; set; } = 1;
    public int Start { get; }
    public string? ListStyleType { get; }

    public ListState(bool isOrdered, int start, string? listStyleType)
    {
        IsOrdered = isOrdered;
        Start = start;
        Counter = start;
        ListStyleType = listStyleType;
    }

    public string GetMarker()
    {
        if (!IsOrdered)
        {
            return "\u2022";
        }

        return ListStyleType switch
        {
            "lower-alpha" or "lower-latin" => ((char)('a' + (Counter - 1) % 26)).ToString() + ".",
            "upper-alpha" or "upper-latin" => ((char)('A' + (Counter - 1) % 26)).ToString() + ".",
            "lower-roman" => ToRoman(Counter).ToLowerInvariant() + ".",
            "upper-roman" => ToRoman(Counter) + ".",
            _ => Counter.ToString() + "."
        };
    }

    private static string ToRoman(int num)
    {
        if (num < 1 || num > 3999) return num.ToString();

        var values = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        var numerals = new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };

        var result = new System.Text.StringBuilder();
        for (int i = 0; i < values.Length; i++)
        {
            while (num >= values[i])
            {
                num -= values[i];
                result.Append(numerals[i]);
            }
        }
        return result.ToString();
    }
}
