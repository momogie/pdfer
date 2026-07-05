using System.Globalization;

namespace PdfEr.Core.Domain.Styles;

public readonly struct CssSpecificity : IComparable<CssSpecificity>
{
    public int IdCount { get; }
    public int ClassCount { get; }
    public int ElementCount { get; }

    public CssSpecificity(int idCount, int classCount, int elementCount)
    {
        IdCount = idCount;
        ClassCount = classCount;
        ElementCount = elementCount;
    }

    public int CompareTo(CssSpecificity other)
    {
        if (IdCount != other.IdCount) return IdCount.CompareTo(other.IdCount);
        if (ClassCount != other.ClassCount) return ClassCount.CompareTo(other.ClassCount);
        return ElementCount.CompareTo(other.ElementCount);
    }

    public static bool operator >(CssSpecificity a, CssSpecificity b) => a.CompareTo(b) > 0;
    public static bool operator <(CssSpecificity a, CssSpecificity b) => a.CompareTo(b) < 0;

    public override string ToString() => $"({IdCount},{ClassCount},{ElementCount})";
}

public sealed class CssDeclarationBlock
{
    private readonly Dictionary<string, CssPropertyValue> _properties = new(StringComparer.OrdinalIgnoreCase);

    public string? GetPropertyValue(string propertyName)
    {
        return _properties.TryGetValue(propertyName, out var val) ? val.RawValue : null;
    }

    public bool TryGetProperty(string propertyName, out CssPropertyValue? value)
    {
        return _properties.TryGetValue(propertyName, out value);
    }

    public void SetProperty(string propertyName, string value, bool important = false)
    {
        _properties[propertyName] = new CssPropertyValue(value, important);
    }

    public CssDeclarationBlock Clone()
    {
        var clone = new CssDeclarationBlock();
        foreach (var kvp in _properties)
            clone._properties[kvp.Key] = kvp.Value;
        return clone;
    }

    public void MergeFrom(CssDeclarationBlock other, bool overrideImportant = false)
    {
        foreach (var kvp in other._properties)
        {
            if (_properties.TryGetValue(kvp.Key, out var existing))
            {
                if (kvp.Value.IsImportant || (!existing.IsImportant && overrideImportant))
                    _properties[kvp.Key] = kvp.Value;
            }
            else
            {
                _properties[kvp.Key] = kvp.Value;
            }
        }
    }

    public IReadOnlyDictionary<string, CssPropertyValue> AllProperties => _properties;

    public int Count => _properties.Count;
}

public sealed class CssPropertyValue
{
    public string RawValue { get; }
    public bool IsImportant { get; }

    public CssPropertyValue(string rawValue, bool isImportant = false)
    {
        RawValue = rawValue;
        IsImportant = isImportant;
    }

    public float ToFloat(float defaultValue = 0)
    {
        var cleaned = RawValue.Trim();
        var numStr = new string(cleaned.TakeWhile(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
        return float.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : defaultValue;
    }

    public override string ToString() => IsImportant ? $"{RawValue} !important" : RawValue;
}

public sealed class CssRule
{
    public string Selector { get; }
    public CssSpecificity Specificity { get; }
    public CssDeclarationBlock Declarations { get; }
    public bool IsImportant { get; }

    public CssRule(string selector, CssSpecificity specificity, CssDeclarationBlock declarations, bool isImportant = false)
    {
        Selector = selector;
        Specificity = specificity;
        Declarations = declarations;
        IsImportant = isImportant;
    }
}

public sealed class CssStylesheet
{
    public List<CssRule> Rules { get; } = new();
    public List<CssMediaRule> MediaRules { get; } = new();
    public List<CssFontFaceRule> FontFaceRules { get; } = new();
    public List<CssPageRule> PageRules { get; } = new();

    public void Merge(CssStylesheet other)
    {
        Rules.AddRange(other.Rules);
        MediaRules.AddRange(other.MediaRules);
        FontFaceRules.AddRange(other.FontFaceRules);
        PageRules.AddRange(other.PageRules);
    }
}

public sealed class CssMediaRule
{
    public string MediaQuery { get; }
    public List<CssRule> Rules { get; } = new();
    public CssMediaRule(string mediaQuery) => MediaQuery = mediaQuery;
}

public sealed class CssFontFaceRule
{
    public string? FontFamily { get; set; }
    public string? Src { get; set; }
    public string? FontStyle { get; set; }
    public string? FontWeight { get; set; }
}

public sealed class CssPageRule
{
    public string? PageName { get; }
    public string? PseudoClass { get; }
    public CssDeclarationBlock Declarations { get; }
    public Dictionary<string, CssDeclarationBlock> MarginBoxes { get; } = new();

    public CssPageRule(string? pageName, string? pseudoClass, CssDeclarationBlock declarations)
    {
        PageName = pageName;
        PseudoClass = pseudoClass;
        Declarations = declarations;
    }
}
