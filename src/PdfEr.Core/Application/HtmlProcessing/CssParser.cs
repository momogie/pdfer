using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using PdfEr.Core.Domain.Styles;

namespace PdfEr.Core.Application.HtmlProcessing;

public sealed class CssParser
{
    private static readonly Regex CommentRegex = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex RuleRegex = new(@"([^{}]+)\{([^}]+)\}", RegexOptions.Compiled);
    private static readonly Regex AtPageRegex = new(@"@page(?:\s+:?([a-zA-Z0-9_-]+))?(?:\s*:([a-zA-Z]+))?\s*\{([^}]+)\}", RegexOptions.Compiled);
    private static readonly Regex AtFontFaceRegex = new(@"@font-face\s*\{([^}]+)\}", RegexOptions.Compiled);
    private static readonly Regex PropertyRegex = new(@"([\w-]+)\s*:\s*(.*?)(?:\s*!important)?\s*(?:;|$)", RegexOptions.Compiled);

    private static readonly ConcurrentDictionary<string, CssStylesheet> Cache = new();

    public CssStylesheet Parse(string cssText)
    {
        if (string.IsNullOrWhiteSpace(cssText)) return new CssStylesheet();

        if (Cache.TryGetValue(cssText, out var cached))
            return cached;

        cssText = CommentRegex.Replace(cssText, "");

        var sheet = new CssStylesheet();

        foreach (Match match in AtFontFaceRegex.Matches(cssText))
        {
            var ff = new CssFontFaceRule();
            var props = ParseDeclarations(match.Groups[1].Value);
            ff.FontFamily = props.GetPropertyValue("font-family")?.Trim().Trim('\'', '"');
            ff.Src = props.GetPropertyValue("src");
            ff.FontStyle = props.GetPropertyValue("font-style");
            ff.FontWeight = props.GetPropertyValue("font-weight");
            sheet.FontFaceRules.Add(ff);
        }

        foreach (Match match in AtPageRegex.Matches(cssText))
        {
            var name = match.Groups[1].Success ? match.Groups[1].Value : null;
            var pseudo = match.Groups[2].Success ? match.Groups[2].Value : null;
            var decl = ParseDeclarations(match.Groups[3].Value);
            sheet.PageRules.Add(new CssPageRule(name, pseudo, decl));
        }

        ExtractMediaRules(cssText, sheet);

        ParseRulesInto(cssText, sheet.Rules);

        Cache.TryAdd(cssText, sheet);
        return sheet;
    }

    private static void ExtractMediaRules(string cssText, CssStylesheet sheet)
    {
        int i = 0;
        while (i < cssText.Length)
        {
            int atIdx = cssText.IndexOf("@media", i, StringComparison.OrdinalIgnoreCase);
            if (atIdx < 0) break;

            int braceStart = cssText.IndexOf('{', atIdx);
            if (braceStart < 0) break;

            var mediaQuery = cssText[atIdx..braceStart]
                .AsSpan("@media".Length).Trim().ToString();

            int braceEnd = FindMatchingBrace(cssText, braceStart);
            if (braceEnd < 0) break;

            var innerContent = cssText[(braceStart + 1)..braceEnd];

            var mediaRule = new CssMediaRule(mediaQuery);
            ParseRulesInto(innerContent, mediaRule.Rules);
            sheet.MediaRules.Add(mediaRule);

            i = braceEnd + 1;
        }
    }

    private static int FindMatchingBrace(string text, int openPos)
    {
        if (text[openPos] != '{') return -1;
        int depth = 1;
        int i = openPos + 1;
        while (i < text.Length && depth > 0)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}') depth--;
            if (depth > 0) i++;
        }
        return depth == 0 ? i : -1;
    }

    private static void ParseRulesInto(string cssText, List<CssRule> rules)
    {
        foreach (Match match in RuleRegex.Matches(cssText))
        {
            var selector = match.Groups[1].Value.Trim();
            if (selector.StartsWith("@")) continue;

            var decl = ParseDeclarations(match.Groups[2].Value);
            var specificity = ComputeSpecificity(selector);

            rules.Add(new CssRule(selector, specificity, decl));
        }
    }

    public static CssDeclarationBlock ParseDeclarations(string cssText)
    {
        var block = new CssDeclarationBlock();
        foreach (Match match in PropertyRegex.Matches(cssText))
        {
            var propName = match.Groups[1].Value.Trim().ToLowerInvariant();
            var propValue = match.Groups[2].Value.Trim();
            var important = match.Value.Contains("!important");
            block.SetProperty(propName, propValue, important);
        }
        return block;
    }

    public static CssSpecificity ComputeSpecificity(string selector)
    {
        int idCount = 0, classCount = 0, elementCount = 0;

        var parts = selector.Split(',', StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var simpleParts = SplitByCombinator(part);
            foreach (var sp in simpleParts)
            {
                CountSpecificity(sp, ref idCount, ref classCount, ref elementCount);
            }
        }

        return new CssSpecificity(idCount, classCount, elementCount);
    }

    private static void CountSpecificity(string selectorPart, ref int idCount, ref int classCount, ref int elementCount)
    {
        if (string.IsNullOrWhiteSpace(selectorPart)) return;
        if (selectorPart == ">" || selectorPart == "+" || selectorPart == "~") return;

        bool hasTag = false;
        int i = 0;

        while (i < selectorPart.Length && selectorPart[i] != '#' && selectorPart[i] != '.' &&
               selectorPart[i] != '[' && selectorPart[i] != ':')
        {
            if (!char.IsWhiteSpace(selectorPart[i]))
                hasTag = true;
            i++;
        }

        while (i < selectorPart.Length)
        {
            if (selectorPart[i] == '#') { idCount++; i++; while (i < selectorPart.Length && selectorPart[i] != '.' && selectorPart[i] != '[' && selectorPart[i] != ':' && selectorPart[i] != '#') i++; }
            else if (selectorPart[i] == '.') { classCount++; i++; while (i < selectorPart.Length && selectorPart[i] != '.' && selectorPart[i] != '[' && selectorPart[i] != ':' && selectorPart[i] != '#') i++; }
            else if (selectorPart[i] == ':') { classCount++; i++; while (i < selectorPart.Length && selectorPart[i] != '.' && selectorPart[i] != '[' && selectorPart[i] != ':' && selectorPart[i] != '#' && selectorPart[i] != ' ') i++; }
            else if (selectorPart[i] == '[') { classCount++; i++; while (i < selectorPart.Length && selectorPart[i] != ']') i++; if (i < selectorPart.Length) i++; }
            else i++;
        }

        if (hasTag) elementCount++;
    }

    private static string[] SplitByCombinator(string selector)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < selector.Length; i++)
        {
            if (selector[i] == '[') depth++;
            else if (selector[i] == ']') depth--;
            else if (depth == 0 && (selector[i] == ' ' || selector[i] == '>' || selector[i] == '+' || selector[i] == '~'))
            {
                if (i > start)
                    parts.Add(selector[start..i].Trim());
                parts.Add(selector[i].ToString());
                start = i + 1;
            }
        }
        if (start < selector.Length)
            parts.Add(selector[start..].Trim());
        return parts.ToArray();
    }

    public static void ClearCache() => Cache.Clear();
}
