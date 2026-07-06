using System.Text.RegularExpressions;
using AngleSharp.Dom;
using PdfEr.Core.Domain.Styles;

namespace PdfEr.Core.Application.HtmlProcessing;

public sealed class CssMerger
{
    private static readonly Regex VarRegex = new(@"var\(\s*(--[\w-]+)\s*(?:,\s*([^()]*(?:\([^()]*\)[^()]*)*))?\)", RegexOptions.Compiled);

    private readonly CssStylesheet _defaultStylesheet;
    private readonly CssStylesheet _userStylesheet;
    private readonly CssParser _parser;
    private readonly CssNormalizer _normalizer;
    private readonly Dictionary<string, string> _customProperties = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> InheritedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "azimuth", "border-collapse", "border-spacing", "caption-side",
        "color", "cursor", "direction", "elevation", "empty-cells",
        "font-family", "font-size", "font-style", "font-variant",
        "font-weight", "font", "letter-spacing", "line-height",
        "list-style-image", "list-style-position", "list-style-type",
        "list-style", "orphans", "pitch-range", "pitch", "quotes",
        "richness", "speak-header", "speak-numeral", "speak-punctuation",
        "speak", "speech-rate", "stress", "text-align", "text-indent",
        "text-transform", "visibility", "voice-family", "volume",
        "white-space", "widows", "word-spacing"
    };

    public CssMerger(CssParser parser, CssNormalizer normalizer)
    {
        _parser = parser;
        _normalizer = normalizer;
        _defaultStylesheet = new CssStylesheet();
        _userStylesheet = new CssStylesheet();
        LoadDefaultStyles();
    }

    public CssStylesheet UserStylesheet => _userStylesheet;
    public IReadOnlyList<CssFontFaceRule> FontFaceRules => _userStylesheet.FontFaceRules;
    public IReadOnlyList<CssPageRule> PageRules => _userStylesheet.PageRules;

    private void LoadDefaultStyles()
    {
        var defaults = @"
            body { margin:0; padding:0; font-family:DejaVu Sans; font-size:10pt; line-height:1.3; color:#000; }
            p { margin:1.12em 0; }
            h1 { font-size:2em; font-weight:bold; margin:0.67em 0; }
            h2 { font-size:1.5em; font-weight:bold; margin:0.75em 0; }
            h3 { font-size:1.17em; font-weight:bold; margin:0.83em 0; }
            h4 { font-size:1em; font-weight:bold; margin:1.12em 0; }
            h5 { font-size:0.83em; font-weight:bold; margin:1.5em 0; }
            h6 { font-size:0.75em; font-weight:bold; margin:1.67em 0; }
            blockquote { margin:1em 40px; }
            pre { font-family:monospace; white-space:pre; margin:1em 0; }
            hr { border:1px inset; margin:0.5em 0; }
            table { display:table; border-collapse:separate; border-spacing:2px; }
            thead { display:table-header-group; }
            tbody { display:table-row-group; }
            tfoot { display:table-footer-group; }
            tr { display:table-row; }
            td, th { display:table-cell; padding:1px; }
            th { font-weight:bold; text-align:center; }
            caption { display:table-caption; text-align:center; }
            ul, ol { display:block; margin:1em 0; padding-left:40px; }
            li { display:list-item; }
            a { text-decoration:underline; color:#00f; }
            strong { font-weight:bold; }
            em { font-style:italic; }
            img { display:inline-block; }
            sub { vertical-align:sub; font-size:smaller; }
            sup { vertical-align:super; font-size:smaller; }
            code { font-family:monospace; }
            form { display:block; margin:0; }
            input, textarea, select { display:inline-block; }
            div { display:block; }
            span { display:inline; }
        ";
        var parsed = _parser.Parse(defaults);
        foreach (var rule in parsed.Rules)
            ExpandRuleInPlace(rule);
        _defaultStylesheet.Merge(parsed);
    }

    private void ExpandRuleInPlace(CssRule rule)
    {
        var expanded = _normalizer.ExpandShorthands(rule.Declarations);
        rule.Declarations.Clear();
        foreach (var kvp in expanded.AllProperties)
            rule.Declarations.SetProperty(kvp.Key, kvp.Value.RawValue, kvp.Value.IsImportant);
    }

    public void AddUserStylesheet(CssStylesheet stylesheet)
    {
        var allRules = new List<CssRule>(stylesheet.Rules);
        foreach (var mediaRule in stylesheet.MediaRules)
        {
            if (MediaQueryMatchesPrint(mediaRule.MediaQuery))
                allRules.AddRange(mediaRule.Rules);
        }

        CollectCustomProperties(allRules);

        foreach (var rule in allRules)
        {
            ResolveVariablesInPlace(rule.Declarations);
            ExpandRuleInPlace(rule);
            _userStylesheet.Rules.Add(rule);
        }

        foreach (var rule in stylesheet.FontFaceRules)
            _userStylesheet.FontFaceRules.Add(rule);

        foreach (var rule in stylesheet.PageRules)
            _userStylesheet.PageRules.Add(rule);
    }

    private void CollectCustomProperties(List<CssRule> rules)
    {
        foreach (var rule in rules)
        {
            if (!SelectorDefinesRoot(rule.Selector)) continue;

            foreach (var kvp in rule.Declarations.AllProperties)
            {
                if (kvp.Key.StartsWith("--", StringComparison.Ordinal))
                    _customProperties[kvp.Key] = kvp.Value.RawValue;
            }
        }
    }

    private static bool IsRootOnlySelector(string selector)
    {
        return selector.Split(',', StringSplitOptions.TrimEntries)
            .All(p => p.Equals(":root", StringComparison.OrdinalIgnoreCase));
    }

    private static bool SelectorDefinesRoot(string selector)
    {
        return selector.Split(',', StringSplitOptions.TrimEntries)
            .Any(p => p.Equals(":root", StringComparison.OrdinalIgnoreCase) ||
                      p.Equals("html", StringComparison.OrdinalIgnoreCase) ||
                      p.Equals("body", StringComparison.OrdinalIgnoreCase));
    }

    private void ResolveVariablesInPlace(CssDeclarationBlock block)
    {
        foreach (var kvp in block.AllProperties.ToList())
        {
            if (kvp.Key.StartsWith("--", StringComparison.Ordinal)) continue;
            if (!kvp.Value.RawValue.Contains("var(", StringComparison.OrdinalIgnoreCase)) continue;

            var resolved = ResolveVarValue(kvp.Value.RawValue);
            block.SetProperty(kvp.Key, resolved, kvp.Value.IsImportant);
        }
    }

    private string ResolveVarValue(string value)
    {
        for (int i = 0; i < 5 && value.Contains("var(", StringComparison.OrdinalIgnoreCase); i++)
        {
            value = VarRegex.Replace(value, m =>
            {
                var varName = m.Groups[1].Value;
                if (_customProperties.TryGetValue(varName, out var resolved))
                    return resolved;
                return m.Groups[2].Success ? m.Groups[2].Value.Trim() : string.Empty;
            });
        }
        return value;
    }

    public CssDeclarationBlock ResolveStyles(string tagName, Dictionary<string, string> attributes, CssDeclarationBlock? parentInherited, IElement? element = null)
    {
        var result = new CssDeclarationBlock();

        if (parentInherited != null)
        {
            foreach (var kvp in parentInherited.AllProperties)
            {
                if (InheritedProperties.Contains(kvp.Key))
                    result.SetProperty(kvp.Key, kvp.Value.RawValue, kvp.Value.IsImportant);
            }
        }

        CollectMatchingRules(_defaultStylesheet.Rules, tagName, attributes, element, result);
        CollectMatchingRules(_userStylesheet.Rules, tagName, attributes, element, result);

        if (attributes.TryGetValue("style", out var inlineStyle) && !string.IsNullOrWhiteSpace(inlineStyle))
        {
            var inlineDecl = CssParser.ParseDeclarations(inlineStyle);
            ResolveVariablesInPlace(inlineDecl);
            inlineDecl = _normalizer.ExpandShorthands(inlineDecl);
            result.MergeFrom(inlineDecl, overrideImportant: true);
        }

        return result;
    }

    private static void CollectMatchingRules(List<CssRule> rules, string tagName, Dictionary<string, string> attributes, IElement? element, CssDeclarationBlock result)
    {
        var matched = new List<(CssRule rule, int specificitySort)>();

        foreach (var rule in rules)
        {
            if (IsRootOnlySelector(rule.Selector)) continue;

            if (SelectorMatches(rule.Selector, tagName, attributes, element))
            {
                int sort = rule.Specificity.IdCount * 1_000_000 +
                           rule.Specificity.ClassCount * 10_000 +
                           rule.Specificity.ElementCount;
                matched.Add((rule, sort));
            }
        }

        matched.Sort((a, b) => a.specificitySort.CompareTo(b.specificitySort));

        foreach (var (rule, _) in matched)
            result.MergeFrom(rule.Declarations, overrideImportant: true);
    }

    public static bool MediaQueryMatchesPrint(string mediaQuery)
    {
        if (string.IsNullOrWhiteSpace(mediaQuery)) return true;

        var parts = mediaQuery.Split(',', StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var tokens = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return true;

            var mediaType = tokens[0].ToLowerInvariant();
            if (mediaType == "all") return true;
            if (mediaType == "print") return true;
            if (mediaType == "only" && tokens.Length > 1 && tokens[1].ToLowerInvariant() == "print") return true;
            if (mediaType == "not")
            {
                if (tokens.Length > 1 && tokens[1].ToLowerInvariant() == "screen") return true;
                if (tokens.Length > 1 && tokens[1].ToLowerInvariant() == "print") return false;
            }
        }

        return false;
    }

    private static bool SelectorMatches(string selector, string tagName, Dictionary<string, string> attributes, IElement? element)
    {
        var parts = selector.Split(',');
        return parts.Any(p => MatchesSingleSelector(p.Trim(), tagName, attributes, element));
    }

    private static bool MatchesSingleSelector(string selector, string tagName, Dictionary<string, string> attributes, IElement? element)
    {
        selector = selector.Trim();

        if (selector == "*") return true;

        if (!selector.Contains(' ') && !selector.Contains('>') &&
            !selector.Contains('+') && !selector.Contains('~'))
        {
            return MatchesSimpleSelector(selector, tagName, attributes);
        }

        var combinators = SplitByCombinator(selector);
        int lastIdx = combinators.Length - 1;

        if (!MatchesSimpleSelector(combinators[lastIdx].Trim(), tagName, attributes))
            return false;

        if (element == null) return false;

        IElement? cursor = element;
        int i = lastIdx - 1;
        while (i >= 0)
        {
            char combinator = GetCombinator(combinators[i]);
            i--;
            if (i < 0) break;
            var part = combinators[i].Trim();
            i--;

            if (combinator == '>')
            {
                cursor = cursor?.ParentElement;
                if (cursor == null || !MatchesSimpleSelector(part, cursor.TagName.ToLowerInvariant(), GetElementAttributes(cursor)))
                    return false;
            }
            else if (combinator == '+')
            {
                cursor = cursor?.PreviousElementSibling;
                if (cursor == null || !MatchesSimpleSelector(part, cursor.TagName.ToLowerInvariant(), GetElementAttributes(cursor)))
                    return false;
            }
            else if (combinator == '~')
            {
                var sib = cursor?.PreviousElementSibling;
                bool found = false;
                while (sib != null)
                {
                    if (MatchesSimpleSelector(part, sib.TagName.ToLowerInvariant(), GetElementAttributes(sib)))
                    {
                        found = true;
                        cursor = sib;
                        break;
                    }
                    sib = sib.PreviousElementSibling;
                }
                if (!found) return false;
            }
            else // descendant combinator (space): any ancestor
            {
                var ancestor = cursor?.ParentElement;
                bool found = false;
                while (ancestor != null)
                {
                    if (MatchesSimpleSelector(part, ancestor.TagName.ToLowerInvariant(), GetElementAttributes(ancestor)))
                    {
                        found = true;
                        cursor = ancestor;
                        break;
                    }
                    ancestor = ancestor.ParentElement;
                }
                if (!found) return false;
            }
        }

        return true;
    }

    private static Dictionary<string, string> GetElementAttributes(IElement element)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attr in element.Attributes)
            dict[attr.Name] = attr.Value;
        return dict;
    }

    private static bool MatchesSimpleSelector(string selector, string tagName, Dictionary<string, string> attributes)
    {
        if (string.IsNullOrEmpty(selector)) return false;

        var simple = new SimpleSelector(selector);

        if (simple.Tag != null && simple.Tag != "*" && !simple.Tag.Equals(tagName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (simple.Id != null)
        {
            if (!attributes.TryGetValue("id", out var id) ||
                !id.Equals(simple.Id, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        foreach (var cls in simple.Classes)
        {
            if (!attributes.TryGetValue("class", out var classStr) ||
                !classStr.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Any(c => c.Equals(cls, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        foreach (var attr in simple.Attributes)
        {
            if (!EvaluateAttributeSelector(attr, attributes))
                return false;
        }

        return true;
    }

    private static bool EvaluateAttributeSelector(string attrSelector, Dictionary<string, string> attributes)
    {
        var match = System.Text.RegularExpressions.Regex.Match(attrSelector,
            @"^\[([\w-]+)(?:([~|^$*]?=)?[""']?([^""'\] ]*)[""']?)?\]$");
        if (!match.Success) return true;

        var name = match.Groups[1].Value;
        var op = match.Groups[2].Value;
        var value = match.Groups[3].Value;

        if (!attributes.TryGetValue(name, out var attrValue))
            return false;

        if (string.IsNullOrEmpty(op))
            return true;

        return op switch
        {
            "=" => attrValue.Equals(value, StringComparison.OrdinalIgnoreCase),
            "~=" => attrValue.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Any(v => v.Equals(value, StringComparison.OrdinalIgnoreCase)),
            "|=" => attrValue.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                    attrValue.StartsWith(value + "-", StringComparison.OrdinalIgnoreCase),
            "^=" => attrValue.StartsWith(value, StringComparison.OrdinalIgnoreCase),
            "$=" => attrValue.EndsWith(value, StringComparison.OrdinalIgnoreCase),
            "*=" => attrValue.Contains(value, StringComparison.OrdinalIgnoreCase),
            _ => true
        };
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

    private static char GetCombinator(string part)
    {
        part = part.Trim();
        if (part == ">") return '>';
        if (part == "+") return '+';
        if (part == "~") return '~';
        return ' ';
    }

    private readonly struct SimpleSelector
    {
        public string? Tag { get; }
        public string? Id { get; }
        public List<string> Classes { get; } = new();
        public List<string> Attributes { get; } = new();
        public List<string> PseudoClasses { get; } = new();

        public SimpleSelector(string selector)
        {
            int i = 0;
            var tagBuf = new System.Text.StringBuilder();

            while (i < selector.Length && selector[i] != '#' && selector[i] != '.' &&
                   selector[i] != '[' && selector[i] != ':')
            {
                tagBuf.Append(selector[i]);
                i++;
            }

            var tag = tagBuf.ToString().Trim();
            Tag = tag.Length > 0 ? tag : null;

            while (i < selector.Length)
            {
                if (selector[i] == '#')
                {
                    i++;
                    var idBuf = new System.Text.StringBuilder();
                    while (i < selector.Length && selector[i] != '.' && selector[i] != '[' &&
                           selector[i] != ':' && selector[i] != '#')
                    {
                        idBuf.Append(selector[i]);
                        i++;
                    }
                    Id = idBuf.ToString();
                }
                else if (selector[i] == '.')
                {
                    i++;
                    var clsBuf = new System.Text.StringBuilder();
                    while (i < selector.Length && selector[i] != '.' && selector[i] != '[' &&
                           selector[i] != ':' && selector[i] != '#')
                    {
                        clsBuf.Append(selector[i]);
                        i++;
                    }
                    if (clsBuf.Length > 0)
                        Classes.Add(clsBuf.ToString());
                }
                else if (selector[i] == '[')
                {
                    var attrBuf = new System.Text.StringBuilder();
                    attrBuf.Append(selector[i]);
                    i++;
                    while (i < selector.Length && selector[i] != ']')
                    {
                        attrBuf.Append(selector[i]);
                        i++;
                    }
                    if (i < selector.Length)
                    {
                        attrBuf.Append(selector[i]);
                        i++;
                    }
                    Attributes.Add(attrBuf.ToString());
                }
                else if (selector[i] == ':')
                {
                    var pseudoBuf = new System.Text.StringBuilder();
                    pseudoBuf.Append(selector[i]);
                    i++;
                    while (i < selector.Length && selector[i] != '.' && selector[i] != '[' &&
                           selector[i] != ':' && selector[i] != '#' && selector[i] != ' ')
                    {
                        pseudoBuf.Append(selector[i]);
                        i++;
                    }
                    PseudoClasses.Add(pseudoBuf.ToString());
                }
                else
                {
                    i++;
                }
            }
        }
    }
}
