using AngleSharp;
using AngleSharp.Dom;
using PdfEr.Core.Domain.Exceptions;

namespace PdfEr.Core.Application.HtmlProcessing;

public enum HtmlParseMode
{
    FullDocument,
    HtmlFragment,
    StylesheetOnly,
    BodyContent
}

public sealed class HtmlParseResult
{
    public IDocument Document { get; }
    public string? ExtractedCss { get; set; }
    public List<ExternalCssReference> ExternalStylesheets { get; } = new();

    public HtmlParseResult(IDocument document) => Document = document;
}

public sealed class ExternalCssReference
{
    public string Url { get; init; } = "";
    public string? Media { get; init; }
}

public sealed class HtmlParser
{
    private readonly IBrowsingContext _context;

    public HtmlParser()
    {
        var config = Configuration.Default;
        _context = BrowsingContext.New(config);
    }

    public async Task<HtmlParseResult> ParseAsync(string html, HtmlParseMode mode = HtmlParseMode.FullDocument)
    {
        IDocument document;
        try
        {
            document = await _context.OpenAsync(req => req.Content(html));
        }
        catch (Exception ex)
        {
            throw new HtmlParseException("Failed to parse HTML", ex);
        }

        var result = new HtmlParseResult(document);

        var styleNodes = document.QuerySelectorAll("style");
        var cssParts = new List<string>();
        foreach (var style in styleNodes)
        {
            if (!string.IsNullOrWhiteSpace(style.TextContent))
                cssParts.Add(style.TextContent);
        }
        result.ExtractedCss = string.Join("\n", cssParts);

        var links = document.QuerySelectorAll("link[rel=stylesheet]");
        foreach (var link in links)
        {
            var href = link.GetAttribute("href");
            if (!string.IsNullOrWhiteSpace(href))
            {
                result.ExternalStylesheets.Add(new ExternalCssReference
                {
                    Url = href,
                    Media = link.GetAttribute("media")
                });
            }
        }

        return result;
    }

    public string ExtractBodyContent(IDocument document)
    {
        var body = document.Body;
        return body?.InnerHtml ?? "";
    }
}
