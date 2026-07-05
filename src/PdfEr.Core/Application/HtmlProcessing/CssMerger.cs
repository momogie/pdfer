using PdfEr.Core.Domain.Styles;

namespace PdfEr.Core.Application.HtmlProcessing;

public sealed class CssMerger
{
    private readonly CssStylesheet _defaultStylesheet;
    private readonly CssStylesheet _userStylesheet;
    private readonly CssParser _parser;

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

    public CssMerger(CssParser parser)
    {
        _parser = parser;
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
        _defaultStylesheet.Merge(_parser.Parse(defaults));
    }

    public void AddUserStylesheet(CssStylesheet stylesheet)
    {
        foreach (var rule in stylesheet.Rules)
            _userStylesheet.Rules.Add(rule);

        foreach (var rule in stylesheet.FontFaceRules)
            _userStylesheet.FontFaceRules.Add(rule);

        foreach (var rule in stylesheet.PageRules)
            _userStylesheet.PageRules.Add(rule);

        foreach (var mediaRule in stylesheet.MediaRules)
        {
            if (MediaQueryMatchesPrint(mediaRule.MediaQuery))
            {
                foreach (var rule in mediaRule.Rules)
                    _userStylesheet.Rules.Add(rule);
            }
        }
    }

    public CssDeclarationBlock ResolveStyles(string tagName, Dictionary<string, string> attributes, CssDeclarationBlock? parentInherited)
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

        CollectMatchingRules(_defaultStylesheet.Rules, tagName, attributes, result);
        CollectMatchingRules(_userStylesheet.Rules, tagName, attributes, result);

        if (attributes.TryGetValue("style", out var inlineStyle) && !string.IsNullOrWhiteSpace(inlineStyle))
        {
            var inlineDecl = CssParser.ParseDeclarations(inlineStyle);
            result.MergeFrom(inlineDecl, overrideImportant: true);
        }

        return result;
    }

    private static void CollectMatchingRules(List<CssRule> rules, string tagName, Dictionary<string, string> attributes, CssDeclarationBlock result)
    {
        var matched = new List<(CssRule rule, int specificitySort)>();

        foreach (var rule in rules)
        {
            if (SelectorMatches(rule.Selector, tagName, attributes))
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

    private static bool SelectorMatches(string selector, string tagName, Dictionary<string, string> attributes)
    {
        var parts = selector.Split(',');
        return parts.Any(p => MatchesSingleSelector(p.Trim(), tagName, attributes));
    }

    private static bool MatchesSingleSelector(string selector, string tagName, Dictionary<string, string> attributes)
    {
        selector = selector.Trim();

        if (selector == "*") return true;

        if (!selector.Contains(' ') && !selector.Contains('>') &&
            !selector.Contains('+') && !selector.Contains('~'))
        {
            return MatchesSimpleSelector(selector, tagName, attributes);
        }

        var combinators = SplitByCombinator(selector);

        for (int i = combinators.Length - 1; i >= 0; i--)
        {
            var part = combinators[i].Trim();
            char combinator = i > 0 ? GetCombinator(combinators[i - 1]) : ' ';

            if (i == combinators.Length - 1)
            {
                if (!MatchesSimpleSelector(part, tagName, attributes))
                    return false;
            }
            else
            {
                return MatchesSimpleSelector(part, tagName, attributes);
            }
        }

        return true;
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
