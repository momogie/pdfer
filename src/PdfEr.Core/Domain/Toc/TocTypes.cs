namespace PdfEr.Core.Domain.Toc;

public sealed class BookmarkNode
{
    public string Title { get; set; } = string.Empty;
    public int PageNumber { get; set; } = 1;
    public float PageX { get; set; }
    public float PageY { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public List<BookmarkNode> Children { get; } = new();
}

public sealed class TableOfContentsDefinition
{
    public string Title { get; set; } = "Table of Contents";
    public bool GenerateFromBookmarks { get; set; } = true;
    public int MaxLevel { get; set; } = 3;
    public bool ShowPageNumbers { get; set; } = true;
    public string? TitleStyle { get; set; }
}

public sealed class TocRenderResult
{
    public List<BookmarkNode> Bookmarks { get; } = new();
    public string? Content { get; set; }
    public int StartPage { get; set; }
}
