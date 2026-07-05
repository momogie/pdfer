using AngleSharp.Dom;
using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Application.HtmlProcessing;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;
using PdfEr.Core.Domain.Typography;
using PdfEr.Core.Domain.ValueObjects;
using PdfEr.Infrastructure.PdfWriters;

namespace PdfEr.Infrastructure;

public sealed class PdfConverterService : IPdfConverter
{
    private static readonly HashSet<string> SkipContentTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "style", "script", "noscript"
    };

    private readonly HtmlParser _htmlParser;
    private readonly CssParser _cssParser;
    private readonly CssMerger _cssMerger;
    private readonly CssNormalizer _cssNormalizer;
    private readonly LayoutEngine _layoutEngine;
    private readonly TagRegistry _tagRegistry;
    private readonly PdfWriter _pdfWriter;
    private readonly IFontRegistry _fontRegistry;
    private readonly PdfConverterConfiguration _defaultConfig;

    private DocumentLayout _documentLayout = null!;
    private BlockBox? _currentBlock;
    private PdfConverterConfiguration _currentConfig = null!;
    private readonly Stack<ListState> _listStack = new();

    public PdfConverterService(
        HtmlParser htmlParser,
        CssParser cssParser,
        CssMerger cssMerger,
        CssNormalizer cssNormalizer,
        LayoutEngine layoutEngine,
        TagRegistry tagRegistry,
        PdfWriter pdfWriter,
        IFontRegistry fontRegistry,
        PdfConverterConfiguration defaultConfig)
    {
        _htmlParser = htmlParser;
        _cssParser = cssParser;
        _cssMerger = cssMerger;
        _cssNormalizer = cssNormalizer;
        _layoutEngine = layoutEngine;
        _tagRegistry = tagRegistry;
        _pdfWriter = pdfWriter;
        _fontRegistry = fontRegistry;
        _defaultConfig = defaultConfig;
    }

    public byte[] ConvertHtmlToPdf(string html, PdfConverterConfiguration? config = null)
    {
        config ??= _defaultConfig;
        return ConvertAsync(html, config).GetAwaiter().GetResult();
    }

    public async Task<byte[]> ConvertHtmlToPdfAsync(string html, CancellationToken cancellationToken = default)
    {
        return await ConvertAsync(html, _defaultConfig);
    }

    public void ConvertHtmlToPdfToFile(string html, string filePath, PdfConverterConfiguration? config = null)
    {
        var pdf = ConvertHtmlToPdf(html, config);
        File.WriteAllBytes(filePath, pdf);
    }

    public async Task ConvertHtmlToPdfToFileAsync(string html, string filePath, PdfConverterConfiguration? config = null, CancellationToken cancellationToken = default)
    {
        var pdf = await ConvertHtmlToPdfAsync(html, cancellationToken);
        await File.WriteAllBytesAsync(filePath, pdf, cancellationToken);
    }

    private async Task<byte[]> ConvertAsync(string html, PdfConverterConfiguration config)
    {
        var parseResult = await _htmlParser.ParseAsync(html);
        var userCss = _cssParser.Parse(parseResult.ExtractedCss ?? "");
        _cssMerger.AddUserStylesheet(userCss);

        RegisterFontFaces();

        var body = parseResult.Document.Body;
        _documentLayout = _layoutEngine.CreateDocumentLayout(config);
        _currentConfig = config;
        _currentBlock = null;
        _listStack.Clear();

        if (body != null)
            WalkDom(body, null);

        return _pdfWriter.WriteDocumentLayout(_documentLayout, config);
    }

    private void RegisterFontFaces()
    {
        foreach (var face in _cssMerger.FontFaceRules)
        {
            if (string.IsNullOrWhiteSpace(face.FontFamily) || string.IsNullOrWhiteSpace(face.Src))
                continue;

            var fontData = LoadFontData(face.Src);
            if (fontData == null) continue;

            var style = ParseFontFaceStyle(face.FontStyle, face.FontWeight);
            _fontRegistry.RegisterFont(face.FontFamily, style, fontData);
        }
    }

    private static byte[]? LoadFontData(string src)
    {
        var urlMatch = System.Text.RegularExpressions.Regex.Match(src,
            @"url\([""']?([^""'\)]+)[""']?\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!urlMatch.Success) return null;

        var path = urlMatch.Groups[1].Value.Trim();
        if (string.IsNullOrEmpty(path)) return null;

        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var fullPath = Path.IsPathRooted(path) ? path :
                Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, path));
            return File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
        }
        catch
        {
            return null;
        }
    }

    private static FontStyle ParseFontFaceStyle(string? fontStyle, string? fontWeight)
    {
        bool italic = fontStyle != null &&
            (fontStyle.Contains("italic", StringComparison.OrdinalIgnoreCase) ||
             fontStyle.Contains("oblique", StringComparison.OrdinalIgnoreCase));

        bool bold = fontWeight != null &&
            (fontWeight == "bold" || fontWeight == "700" || fontWeight == "800" || fontWeight == "900" ||
             (int.TryParse(fontWeight, out var w) && w >= 700));

        if (bold && italic) return FontStyle.BoldItalic;
        if (bold) return FontStyle.Bold;
        if (italic) return FontStyle.Italic;
        return FontStyle.Regular;
    }

    private PageLayout CurrentPage => _documentLayout.Pages.Last();

    private void WalkDom(INode node, CssDeclarationBlock? parentStyle)
    {
        if (node == null) return;

        foreach (var child in node.ChildNodes)
        {
            if (child is IElement el)
            {
                var tagName = el.TagName.ToLowerInvariant();

                if (SkipContentTags.Contains(tagName))
                    continue;

                var attrs = GetAttributes(el);
                var resolved = _cssMerger.ResolveStyles(tagName, attrs, parentStyle);
                _cssNormalizer.ExpandShorthands(resolved);

                if (IsDisplayNone(resolved))
                    continue;

                if (_tagRegistry.HasHandler(tagName))
                {
                    var savedBlock = _currentBlock;

                    var context = new TagContext(
                        el, tagName, attrs, resolved, parentStyle,
                        _layoutEngine, _currentConfig, CurrentPage, _fontRegistry, _listStack);

                    if (tagName is "ul" or "ol")
                    {
                        var isOrdered = tagName == "ol";
                        var start = 1;
                        if (attrs.TryGetValue("start", out var s) && int.TryParse(s, out var parsed))
                            start = parsed;
                        var lst = resolved?.GetPropertyValue("list-style-type");
                        if (string.IsNullOrWhiteSpace(lst))
                            lst = isOrdered ? "decimal" : "disc";
                        _listStack.Push(new ListState(isOrdered, start, lst));
                    }

                    _tagRegistry.OpenTag(tagName, context);

                    if (context.CurrentBlock != null)
                    {
                        _currentBlock = context.CurrentBlock;
                    }

                    WalkDom(el, resolved);

                    if (context.CurrentBlock != null)
                    {
                        _layoutEngine.LayoutBlock(context.CurrentBlock, _currentConfig);
                    }

                    if (tagName is "ul" or "ol" && _listStack.Count > 0)
                        _listStack.Pop();

                    _currentBlock = savedBlock;
                    _tagRegistry.CloseTag(tagName, context);
                }
                else
                {
                    if (el.ChildNodes.Length > 0)
                        WalkDom(el, resolved);
                }
            }
            else if (child is IText textNode)
            {
                var text = textNode.TextContent?.Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (_currentBlock != null)
                {
                    if (_currentBlock.TextContent == null)
                        _currentBlock.TextContent = "";
                    if (_currentBlock.TextContent.Length > 0 && !_currentBlock.TextContent.EndsWith(' '))
                        _currentBlock.TextContent += " ";
                    _currentBlock.TextContent += text;
                }
                else
                {
                    var box = _layoutEngine.CreateBlock("p", new Dictionary<string, string>());
                    box.TextContent = text;
                    _layoutEngine.LayoutBlock(box, _currentConfig);
                    CurrentPage.Blocks.Add(box);
                }
            }
        }
    }

    private static bool IsDisplayNone(CssDeclarationBlock style)
    {
        var display = style.GetPropertyValue("display");
        return display != null && display.Trim().ToLowerInvariant() == "none";
    }

    private static Dictionary<string, string> GetAttributes(IElement el)
    {
        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attr in el.Attributes)
            attrs[attr.Name] = attr.Value;
        return attrs;
    }
}
