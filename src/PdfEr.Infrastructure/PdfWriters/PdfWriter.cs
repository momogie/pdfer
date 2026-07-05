using System.Buffers;
using System.IO.Compression;
using System.Text;
using PdfEr.Core.Application.HtmlProcessing;
using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Enums;
using PdfEr.Core.Domain.Forms;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;
using PdfEr.Core.Domain.Typography;
using PdfEr.Core.Domain.Toc;
using PdfEr.Core.Domain.ValueObjects;

namespace PdfEr.Infrastructure.PdfWriters;

public sealed class PdfBuffer
{
    private readonly List<byte[]> _chunks = new();
    private int _totalLength;

    public int Length => _totalLength;

    public void Append(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var bytes = Encoding.ASCII.GetBytes(text);
        _chunks.Add(bytes);
        _totalLength += bytes.Length;
    }

    public void Append(byte[] data)
    {
        if (data == null || data.Length == 0) return;
        _chunks.Add(data);
        _totalLength += data.Length;
    }

    public void AppendLine(string text = "")
    {
        Append(text);
        Append("\n");
    }

    public void AppendFormat(string format, params object[] args)
    {
        Append(string.Format(format, args));
    }

    public byte[] ToByteArray()
    {
        var result = new byte[_totalLength];
        var offset = 0;
        foreach (var chunk in _chunks)
        {
            Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }
        return result;
    }

    public void WriteToStream(Stream stream)
    {
        foreach (var chunk in _chunks)
            stream.Write(chunk, 0, chunk.Length);
    }

    public async Task WriteToStreamAsync(Stream stream, CancellationToken ct = default)
    {
        foreach (var chunk in _chunks)
            await stream.WriteAsync(chunk, 0, chunk.Length, ct);
    }

    public void Clear()
    {
        _chunks.Clear();
        _totalLength = 0;
    }
}

public sealed class PdfEncryptionOptions
{
    public bool Encrypt { get; set; }
    public string? UserPassword { get; set; }
    public string? OwnerPassword { get; set; }
    public EncryptionLevel Level { get; set; } = EncryptionLevel.Aes128;
    public PdfPermissions Permissions { get; set; } = PdfPermissions.FullAccess;
}

public enum EncryptionLevel { Aes40, Aes128, Aes256 }

[Flags]
public enum PdfPermissions
{
    FullAccess = 0,
    NoPrint = 1,
    NoModify = 2,
    NoCopy = 4,
    NoAnnotate = 8,
    NoFillForms = 16,
    NoAccessibility = 32,
    NoAssemble = 64,
    NoPrintHighQuality = 128
}

public sealed class PdfMetadata
{
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Keywords { get; set; } = string.Empty;
    public string Creator { get; set; } = "PdfEr";
    public string Producer { get; set; } = "PdfEr";
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime Modified { get; set; } = DateTime.UtcNow;
}

public sealed class PdfWriter
{
    private readonly PdfBuffer _buffer = new();
    private int _nextObjectNumber = 1;
    private readonly List<long> _objectOffsets = new();
    private long _currentOffset;
    private PdfMetadata? _metadata;
    private PdfEncryptionOptions? _encryption;
    private string? _pdfVersion;
    private readonly Dictionary<string, int> _fontObjects = new();
    private readonly Dictionary<string, int> _imageObjects = new();
    private int _pagesRootRef;
    private readonly List<int> _pageRefs = new();
    private readonly IFontRegistry? _fontRegistry;

    private readonly List<FontEntry> _fontEntries = new();
    private readonly Dictionary<string, int> _fontKeyToIndex = new(StringComparer.OrdinalIgnoreCase);

    private const float MmToPt = 72f / 25.4f;

    public PdfWriter() { }

    public PdfWriter(IFontRegistry fontRegistry)
    {
        _fontRegistry = fontRegistry;
    }

    private struct FontEntry
    {
        public string FontKey;
        public string FamilyName;
        public FontStyle Style;
        public FontDefinition? FontDef;
        public int ObjectNum;
    }

    public byte[] WriteDocumentLayout(DocumentLayout layout, PdfConverterConfiguration config)
    {
        _buffer.Clear();
        _nextObjectNumber = 1;
        _objectOffsets.Clear();
        _fontObjects.Clear();
        _imageObjects.Clear();
        _pageRefs.Clear();
        _fontEntries.Clear();
        _fontKeyToIndex.Clear();
        _metadata = new PdfMetadata { Title = config.Title ?? "Document" };
        _encryption = null;

        WriteHeader(config.PdfVersion);

        var contentNums = new List<int>();
        var pageFontIndices = new List<List<int>>();
        var pageImageRefs = new List<List<(string Name, int ObjNum)>>();

        foreach (var page in layout.Pages)
        {
            var usedFonts = new List<int>();
            var images = new List<(string Name, int ObjNum)>();
            var contentNum = WriteBlockContentStream(page, config, usedFonts, images);
            contentNums.Add(contentNum);
            pageFontIndices.Add(usedFonts);
            pageImageRefs.Add(images);
        }

        WriteAllFontObjects();

        var pageNums = new List<int>();
        RecordObjectOffset();
        var pagesRootRef = AllocateObjectNumber();
        _buffer.AppendLine($"{pagesRootRef} 0 obj");
        _buffer.AppendLine("<< /Type /Pages");
        _buffer.Append("   /Kids [");
        for (int i = 0; i < layout.Pages.Count; i++)
        {
            if (i > 0) _buffer.Append(" ");
            var pageNum = AllocateObjectNumber();
            pageNums.Add(pageNum);
            _buffer.Append($"{pageNum} 0 R");
        }
        _buffer.AppendLine("]");
        _buffer.AppendLine($"   /Count {layout.Pages.Count}");
        _buffer.AppendLine(">>");
        _buffer.AppendLine("endobj");

        for (int i = 0; i < layout.Pages.Count; i++)
        {
            var page = layout.Pages[i];
            var pageW = page.Size.WidthMillimeters * MmToPt;
            var pageH = page.Size.HeightMillimeters * MmToPt;

            RecordObjectOffset();
            _buffer.AppendLine($"{pageNums[i]} 0 obj");
            _buffer.AppendLine("<< /Type /Page");
            _buffer.AppendLine($"   /Parent {pagesRootRef} 0 R");
            _buffer.Append($"   /MediaBox [0 0 {pageW:F2} {pageH:F2}]");
            _buffer.AppendLine();
            _buffer.AppendLine($"   /Contents {contentNums[i]} 0 R");

            var fontResources = BuildFontResourcesDict(pageFontIndices[i]);
            var xObjectResources = BuildXObjectDict(pageImageRefs[i]);
            _buffer.AppendLine($"   /Resources << /Font << {fontResources} >> {xObjectResources}>>");
            _buffer.AppendLine(">>");
            _buffer.AppendLine("endobj");

            _pageRefs.Add(pageNums[i]);
        }

        string title = config.Title ?? "Document";
        var infoRef = WriteDocumentInfo(title);
        var catalogRef = WriteCatalog(pagesRootRef, 0, infoRef, 0, null);

        WriteXRefTable();
        WriteTrailer(catalogRef, infoRef);

        return _buffer.ToByteArray();
    }

    private string BuildFontResourcesDict(List<int> fontIndices)
    {
        if (fontIndices.Count == 0)
            return "/F1 0 R";

        var parts = new List<string>();
        foreach (var idx in fontIndices)
        {
            if (idx >= 0 && idx < _fontEntries.Count)
            {
                var fe = _fontEntries[idx];
                parts.Add($"{fe.FontKey} {fe.ObjectNum} 0 R");
            }
        }
        if (parts.Count == 0)
            return "/F1 0 R";
        return string.Join(" ", parts);
    }

    private static string BuildXObjectDict(List<(string Name, int ObjNum)> images)
    {
        if (images.Count == 0)
            return "";

        var parts = new List<string>();
        foreach (var (name, objNum) in images)
            parts.Add($"{name} {objNum} 0 R");

        return $"/XObject << {string.Join(" ", parts)} >> ";
    }

    private int WriteBlockContentStream(PageLayout page, PdfConverterConfiguration config, List<int> usedFonts, List<(string Name, int ObjNum)> pageImages)
    {
        RecordObjectOffset();
        var num = AllocateObjectNumber();

        var pageW = page.Size.WidthMillimeters * MmToPt;
        var pageH = page.Size.HeightMillimeters * MmToPt;
        float marginLeftPt = page.Margins.Left * MmToPt;
        float marginTopPt = page.Margins.Top * MmToPt;

        var sb = new StringBuilder();
        var colorParser = new ColorParser();

        foreach (var block in page.Blocks)
        {
            var style = block.ComputedStyle;
            bool hasBorders = block.BorderTop > 0 || block.BorderBottom > 0 || block.BorderLeft > 0 || block.BorderRight > 0;
            bool hasBackground = false;
            if (style != null)
            {
                var bg = style.GetPropertyValue("background-color");
                hasBackground = !string.IsNullOrWhiteSpace(bg) && bg != "transparent" && bg != "rgba(0, 0, 0, 0)";
            }
            if (block.InlineContent.Count == 0 && string.IsNullOrEmpty(block.TextContent) && !hasBorders && !hasBackground)
                continue;


            float fontSize = config.DefaultFontSize;
            bool bold = false;
            bool italic = false;
            string? fontFamily = null;

            if (style != null)
            {
                fontSize = GetFontSizeFromStyle(style, config.DefaultFontSize);

                var fw = style.GetPropertyValue("font-weight");
                if (fw != null) bold = fw.Trim() is "bold" or "700" or "800" or "900";

                var fst = style.GetPropertyValue("font-style");
                if (fst != null) italic = fst.Trim() == "italic" || fst.Trim() == "oblique";

                fontFamily = style.GetPropertyValue("font-family");
            }

            float fontSizePt = fontSize;

            float blockXPt = (block.X + block.PaddingLeft) * MmToPt + marginLeftPt;
            float blockYPt = pageH - ((block.Y + block.PaddingTop + fontSizePt * 0.3f) * MmToPt) - marginTopPt;

            // Border box rectangle in PDF coordinates
            float rectLeftPt = block.X * MmToPt + marginLeftPt;
            float rectBottomPt = pageH - ((block.Y + block.Height) * MmToPt) - marginTopPt;
            float rectWidthPt = block.Width * MmToPt;
            float rectHeightPt = block.Height * MmToPt;

            // Draw background if set
            string? bgColorVal = null;
            if (style != null)
                bgColorVal = style.GetPropertyValue("background-color");
            if (!string.IsNullOrWhiteSpace(bgColorVal) && bgColorVal != "transparent" && bgColorVal != "rgba(0, 0, 0, 0)")
            {
                if (colorParser.TryParse(bgColorVal, out var docColor) && docColor is RgbColor bgRgb && bgRgb.A > 0)
                {
                    sb.AppendLine($"{bgRgb.R / 255f:F2} {bgRgb.G / 255f:F2} {bgRgb.B / 255f:F2} rg");
                    sb.AppendLine($"{rectLeftPt:F2} {rectBottomPt:F2} {rectWidthPt:F2} {rectHeightPt:F2} re f");
                }
            }

            // Draw borders if any
            if (hasBorders)
            {
                float br = 0, bg = 0, bb = 0;
                if (style != null)
                {
                    var bColor = style.GetPropertyValue("border-color");
                    if (!string.IsNullOrWhiteSpace(bColor) && bColor != "transparent")
                    {
                        if (colorParser.TryParse(bColor, out var bDocColor) && bDocColor is RgbColor bRgb)
                        {
                            br = bRgb.R / 255f; bg = bRgb.G / 255f; bb = bRgb.B / 255f;
                        }
                    }
                }
                sb.AppendLine($"{br:F2} {bg:F2} {bb:F2} RG");

                // Set border width (use max width for consistency)
                float bwPt = Math.Max(block.BorderTop, Math.Max(block.BorderBottom, Math.Max(block.BorderLeft, block.BorderRight))) * MmToPt;
                sb.AppendLine($"{bwPt:F2} w");

                // Draw full rectangle outline
                sb.AppendLine($"{rectLeftPt:F2} {rectBottomPt:F2} {rectWidthPt:F2} {rectHeightPt:F2} re S");
            }

            var fontIdx = ResolveFontIndex(fontFamily, bold, italic);
            if (!usedFonts.Contains(fontIdx))
                usedFonts.Add(fontIdx);

            // Text color
            string? textColorVal = null;
            if (style != null)
                textColorVal = style.GetPropertyValue("color");
            if (!string.IsNullOrWhiteSpace(textColorVal) && textColorVal != "transparent" && textColorVal != "rgba(0, 0, 0, 0)")
            {
                if (colorParser.TryParse(textColorVal, out var tcDocColor) && tcDocColor is RgbColor tcRgb && tcRgb.A > 0)
                {
                    sb.AppendLine($"{tcRgb.R / 255f:F2} {tcRgb.G / 255f:F2} {tcRgb.B / 255f:F2} rg");
                }
            }

            // Emit image operators before text block (outside BT/ET)
            if (block.InlineContent.Count > 0)
            {
                foreach (var inline in block.InlineContent)
                {
                    if (inline.Type == InlineBoxType.Image && inline.ImageData != null)
                    {
                        var imgObjNum = WriteImage(
                            inline.ImageSource ?? $"img{_imageObjects.Count}",
                            inline.ImageData,
                            inline.ImagePixelWidth,
                            inline.ImagePixelHeight);

                        float imgLeftPt = inline.X * MmToPt + marginLeftPt;
                        float imgBottomPt = pageH - ((inline.Y + inline.Height) * MmToPt) - marginTopPt;
                        float imgWidthPt = inline.Width * MmToPt;
                        float imgHeightPt = inline.Height * MmToPt;

                        var imgName = $"/Img{imgObjNum}";
                        if (!pageImages.Any(p => p.Name == imgName))
                            pageImages.Add((imgName, imgObjNum));

                        sb.AppendLine("q");
                        sb.AppendLine($"{imgWidthPt:F2} 0 0 {imgHeightPt:F2} {imgLeftPt:F2} {imgBottomPt:F2} cm");
                        sb.AppendLine($"{imgName} Do");
                        sb.AppendLine("Q");
                    }
                }
            }

            // Text alignment
            string? textAlign = null;
            if (style != null)
                textAlign = style.GetPropertyValue("text-align");

            float textOffsetX = 0;
            float extraWordSpacing = 0;
            int wordCount = 0;
            if (textAlign is "center" or "right" or "justify")
            {
                float textWidthPt = 0;
                if (block.InlineContent.Count > 0)
                {
                    foreach (var inline in block.InlineContent)
                    {
                        if (inline.Type == InlineBoxType.Text && inline.Text != null)
                        {
                            textWidthPt += inline.Text.Length * fontSizePt * 0.5f;
                            wordCount += inline.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(block.TextContent))
                {
                    textWidthPt = block.TextContent.Length * fontSizePt * 0.5f;
                    wordCount = block.TextContent.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                }

                float contentWidthPt = block.ContentWidth * MmToPt;

                if (textAlign == "center")
                    textOffsetX = Math.Max(0, (contentWidthPt - textWidthPt) / 2);
                else if (textAlign == "right")
                    textOffsetX = Math.Max(0, contentWidthPt - textWidthPt);
                else if (textAlign == "justify" && wordCount > 1)
                {
                    float extraSpace = contentWidthPt - textWidthPt;
                    if (extraSpace > 0)
                        extraWordSpacing = extraSpace / (wordCount - 1);
                }
            }

            // Emit text block (BT/ET) only if there is text content
            bool hasText = false;
            if (block.InlineContent.Count > 0)
            {
                foreach (var inline in block.InlineContent)
                {
                    if (inline.Type == InlineBoxType.Text && inline.Text != null)
                    {
                        hasText = true;
                        break;
                    }
                }
            }
            if (!hasText && !string.IsNullOrEmpty(block.TextContent))
                hasText = true;

            if (hasText)
            {
                sb.Append("BT\n");
                sb.AppendLine($"{_fontEntries[fontIdx].FontKey} {fontSizePt:F2} Tf");

                if (extraWordSpacing > 0)
                    sb.AppendLine($"{extraWordSpacing:F2} Tw");

                sb.AppendLine($"{blockXPt + textOffsetX:F2} {blockYPt:F2} Td");

                if (block.InlineContent.Count > 0)
                {
                    foreach (var inline in block.InlineContent)
                    {
                        if (inline.Type == InlineBoxType.Text && inline.Text != null)
                        {
                            sb.AppendLine($"{FormatPdfText(inline.Text)} Tj");
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(block.TextContent))
                {
                    sb.AppendLine($"{FormatPdfText(block.TextContent)} Tj");
                }

                sb.Append("ET\n");

                if (extraWordSpacing > 0)
                    sb.AppendLine("0 Tw");
            }

            // Text decoration (underline, line-through, overline)
            if (block.InlineContent.Count > 0)
            {
                foreach (var inline in block.InlineContent)
                {
                    if (inline.Type != InlineBoxType.Text || inline.Text == null)
                        continue;

                    var inlineStyle = inline.ComputedStyle ?? block.ComputedStyle;
                    string? decoration = null;
                    if (inlineStyle != null)
                        decoration = inlineStyle.GetPropertyValue("text-decoration-line");

                    if (string.IsNullOrWhiteSpace(decoration) || decoration == "none")
                        continue;

                    float inlineXPt = inline.X * MmToPt + marginLeftPt;
                    float inlineYPt = pageH - (inline.Y * MmToPt) - marginTopPt;
                    float inlineWidthPt = inline.Text.Length * fontSizePt * 0.5f;

                    // Stroke color from text color
                    string? decoColorStr = null;
                    if (inlineStyle != null)
                        decoColorStr = inlineStyle.GetPropertyValue("color");
                    if (!string.IsNullOrWhiteSpace(decoColorStr) && decoColorStr != "transparent" && decoColorStr != "rgba(0, 0, 0, 0)")
                    {
                        if (colorParser.TryParse(decoColorStr, out var dc) && dc is RgbColor decoRgb && decoRgb.A > 0)
                            sb.AppendLine($"{decoRgb.R / 255f:F2} {decoRgb.G / 255f:F2} {decoRgb.B / 255f:F2} RG");
                        else
                            sb.AppendLine("0 0 0 RG");
                    }
                    else
                    {
                        sb.AppendLine("0 0 0 RG");
                    }

                    sb.AppendLine($"{Math.Max(0.4f, fontSizePt * 0.05f):F2} w");

                    if (decoration.Contains("underline"))
                    {
                        float uy = inlineYPt - fontSizePt * 0.05f;
                        sb.AppendLine($"{inlineXPt:F2} {uy:F2} m {inlineXPt + inlineWidthPt:F2} {uy:F2} l S");
                    }
                    if (decoration.Contains("line-through"))
                    {
                        float ty = inlineYPt + fontSizePt * 0.4f;
                        sb.AppendLine($"{inlineXPt:F2} {ty:F2} m {inlineXPt + inlineWidthPt:F2} {ty:F2} l S");
                    }
                    if (decoration.Contains("overline"))
                    {
                        float oy = inlineYPt + fontSizePt * 0.85f;
                        sb.AppendLine($"{inlineXPt:F2} {oy:F2} m {inlineXPt + inlineWidthPt:F2} {oy:F2} l S");
                    }
                }
            }
        }

        var stream = sb.ToString();
        _buffer.AppendLine($"{num} 0 obj");
        _buffer.AppendLine($"<< /Length {stream.Length} >>");
        _buffer.AppendLine("stream");
        _buffer.Append(stream);
        _buffer.AppendLine("endstream");
        _buffer.AppendLine("endobj");
        return num;
    }

    private int ResolveFontIndex(string? fontFamily, bool bold, bool italic)
    {
        if (_fontRegistry == null)
            return 0;

        var style = (bold, italic) switch
        {
            (true, true) => FontStyle.BoldItalic,
            (true, false) => FontStyle.Bold,
            (false, true) => FontStyle.Italic,
            (false, false) => FontStyle.Regular
        };

        var fontDef = _fontRegistry.FindFontWithFallback(fontFamily ?? "sans-serif", style);
        var family = fontDef?.FamilyName ?? "Helvetica";
        var key = $"{family}:{style}";

        if (_fontKeyToIndex.TryGetValue(key, out var existing))
            return existing;

        var idx = _fontEntries.Count;
        var fontKey = $"/F{idx + 1}";
        _fontKeyToIndex[key] = idx;
        _fontEntries.Add(new FontEntry
        {
            FontKey = fontKey,
            FamilyName = family,
            Style = style,
            FontDef = fontDef
        });
        return idx;
    }

    private static float GetFontSizeFromStyle(CssDeclarationBlock? style, float defaultSize)
    {
        var val = style?.GetPropertyValue("font-size");
        if (val == null) return defaultSize;
        val = val.Trim().ToLowerInvariant();

        if (val.EndsWith("pt")) return float.TryParse(val[..^2], out var v) ? v : defaultSize;
        if (val.EndsWith("px")) return float.TryParse(val[..^2], out var v) ? v * 0.75f : defaultSize;
        if (val.EndsWith("mm")) return float.TryParse(val[..^2], out var v) ? v * 2.8346f : defaultSize;
        if (val.EndsWith("em")) return float.TryParse(val[..^2], out var v) ? v * defaultSize : defaultSize;
        if (val.EndsWith("%")) return float.TryParse(val[..^1], out var v) ? v * defaultSize * 0.01f : defaultSize;
        if (val.EndsWith("rem")) return float.TryParse(val[..^3], out var v) ? v * defaultSize : defaultSize;

        return val switch
        {
            "xx-small" => 7f,
            "x-small" => 7.7f,
            "small" => 8.6f,
            "medium" => 10f,
            "large" => 12f,
            "x-large" => 15f,
            "xx-large" => 20f,
            "smaller" => defaultSize * 0.83f,
            "larger" => defaultSize * 1.2f,
            _ => defaultSize
        };
    }

    private int AllocateObjectNumber() => _nextObjectNumber++;

    public byte[] WriteDocument(
        string title,
        string textContent,
        PdfMetadata? metadata = null,
        PdfEncryptionOptions? encryption = null,
        IReadOnlyList<PageSize>? pages = null,
        IReadOnlyList<BookmarkNode>? bookmarks = null,
        FormDefinition? form = null)
    {
        _buffer.Clear();
        _nextObjectNumber = 1;
        _objectOffsets.Clear();
        _fontObjects.Clear();
        _imageObjects.Clear();
        _pageRefs.Clear();
        _metadata = metadata;
        _encryption = encryption;

        WriteHeader(pages?.Count > 0 ? PdfVersion.V2_0 : PdfVersion.V1_7);

        _pagesRootRef = WritePagesRoot();
        int[] pageNums;
        if (pages != null && pages.Count > 0)
        {
            pageNums = new int[pages.Count];
            for (int i = 0; i < pages.Count; i++)
            {
                pageNums[i] = WritePage(_pagesRootRef, textContent);
                _pageRefs.Add(pageNums[i]);
            }
        }
        else
        {
            int pageNum = WritePage(_pagesRootRef, textContent);
            _pageRefs.Add(pageNum);
        }

        var fonts = WriteFontResource();
        var info = WriteDocumentInfo(title);
        int formObj = 0;
        if (form != null)
            formObj = WriteFormStructure(form);

        int? bookmarkRoot = null;
        if (bookmarks != null && bookmarks.Count > 0)
            bookmarkRoot = WriteBookmarks(bookmarks);

        UpdatePagesRoot();

        var catalogRef = WriteCatalog(_pagesRootRef, fonts, info, formObj, bookmarkRoot);

        if (encryption?.Encrypt == true)
            WriteEncryptionDictionary(encryption);

        WriteXRefTable();
        WriteTrailer(catalogRef, info);

        return _buffer.ToByteArray();
    }

    public byte[] WriteSimpleDocument(string title, string textContent)
    {
        return WriteDocument(title, textContent);
    }

    private void WriteHeader(PdfVersion version)
    {
        _pdfVersion = version switch
        {
            PdfVersion.V1_0 => "1.0", PdfVersion.V1_1 => "1.1", PdfVersion.V1_2 => "1.2",
            PdfVersion.V1_3 => "1.3", PdfVersion.V1_4 => "1.4", PdfVersion.V1_5 => "1.5",
            PdfVersion.V1_6 => "1.6", PdfVersion.V1_7 => "1.7", PdfVersion.V2_0 => "2.0",
            _ => "1.7"
        };
        _buffer.AppendLine($"%PDF-{_pdfVersion}");
        _buffer.AppendLine("%\u00e2\u00e3\u00cf\u00d3");
    }

    private int WritePagesRoot()
    {
        RecordObjectOffset();
        var num = AllocateObjectNumber();
        _buffer.AppendLine($"{num} 0 obj");
        _buffer.AppendLine("<< /Type /Pages");
        _buffer.AppendLine("   /Kids []");
        _buffer.AppendLine("   /Count 0");
        _buffer.AppendLine(">>");
        _buffer.AppendLine("endobj");
        return num;
    }

    private int WritePage(int parentRef, string textContent)
    {
        RecordObjectOffset();
        var num = AllocateObjectNumber();
        var contentNum = WriteContentStream(textContent);

        _buffer.AppendLine($"{num} 0 obj");
        _buffer.AppendLine("<< /Type /Page");
        _buffer.AppendLine($"   /Parent {parentRef} 0 R");
        _buffer.AppendLine("   /MediaBox [0 0 595.28 841.89]");
        _buffer.AppendLine($"   /Contents {contentNum} 0 R");
        _buffer.AppendLine("   /Resources << /Font << /F1 5 0 R >> >>");
        _buffer.AppendLine(">>");
        _buffer.AppendLine("endobj");

        _pageRefs.Add(num);
        return num;
    }

    private void UpdatePagesRoot()
    {
        if (_pageRefs.Count == 0) return;
        _buffer.AppendLine($"{_pagesRootRef} 0 obj");
        _buffer.AppendLine("<< /Type /Pages");
        _buffer.AppendLine("   /Kids [" + string.Join(" ", _pageRefs.Select(p => $"{p} 0 R")) + "]");
        _buffer.AppendLine($"   /Count {_pageRefs.Count}");
        _buffer.AppendLine(">>");
        _buffer.AppendLine("endobj");
    }

    public int WriteContentStream(string textContent)
    {
        RecordObjectOffset();
        var num = AllocateObjectNumber();
        var formatted = FormatPdfText(textContent);

        var stream = $"BT\n/F1 12 Tf\n72 800 Td\n{formatted} Tj\nET\n";

        _buffer.AppendLine($"{num} 0 obj");
        _buffer.AppendLine("<< /Length " + stream.Length + " >>");
        _buffer.AppendLine("stream");
        _buffer.Append(stream);
        _buffer.AppendLine();
        _buffer.AppendLine("endstream");
        _buffer.AppendLine("endobj");
        return num;
    }

    private List<int> WriteAllFontObjects()
    {
        var objNums = new List<int>(_fontEntries.Count);

        for (int i = 0; i < _fontEntries.Count; i++)
        {
            var entry = _fontEntries[i];

            if (entry.FontDef?.FontData != null && entry.FontDef.IsEmbedded)
            {
                var compressed = DeflateCompress(entry.FontDef.FontData);

                RecordObjectOffset();
                var fontFileNum = AllocateObjectNumber();
                _buffer.AppendLine($"{fontFileNum} 0 obj");
                _buffer.AppendLine($"<< /Length {compressed.Length} /Length1 {entry.FontDef.FontData.Length} /Filter /FlateDecode >>");
                _buffer.AppendLine("stream");
                _buffer.Append(Encoding.ASCII.GetString(compressed));
                _buffer.AppendLine();
                _buffer.AppendLine("endstream");
                _buffer.AppendLine("endobj");

                RecordObjectOffset();
                var descriptorNum = AllocateObjectNumber();
                _buffer.AppendLine($"{descriptorNum} 0 obj");
                _buffer.AppendLine("<< /Type /FontDescriptor");
                _buffer.AppendLine($"   /FontName /{EscapePdfString(entry.FamilyName)}");
                _buffer.AppendLine($"   /FontFile2 {fontFileNum} 0 R");
                _buffer.AppendLine("   /Flags 32");
                _buffer.AppendLine("   /ItalicAngle 0");
                _buffer.AppendLine("   /Ascent 800");
                _buffer.AppendLine("   /Descent -200");
                _buffer.AppendLine("   /CapHeight 700");
                _buffer.AppendLine("   /StemV 80");
                _buffer.AppendLine(">>");
                _buffer.AppendLine("endobj");

                RecordObjectOffset();
                var fontObjNum = AllocateObjectNumber();
                _buffer.AppendLine($"{fontObjNum} 0 obj");
                _buffer.AppendLine("<< /Type /Font /Subtype /TrueType");
                _buffer.AppendLine($"   /BaseFont /{EscapePdfString(entry.FamilyName)}");
                _buffer.AppendLine($"   /FontDescriptor {descriptorNum} 0 R");
                _buffer.AppendLine("   /FirstChar 32");
                _buffer.AppendLine("   /LastChar 255");
                _buffer.AppendLine("   /Encoding /WinAnsiEncoding");
                _buffer.AppendLine(">>");
                _buffer.AppendLine("endobj");

                _fontObjects[entry.FamilyName] = fontObjNum;
                objNums.Add(fontObjNum);

                _fontEntries[i] = new FontEntry
                {
                    FontKey = entry.FontKey,
                    FamilyName = entry.FamilyName,
                    Style = entry.Style,
                    FontDef = entry.FontDef,
                    ObjectNum = fontObjNum
                };
            }
            else
            {
                var baseFont = entry.FamilyName switch
                {
                    string n when n.Contains("Times", StringComparison.OrdinalIgnoreCase) => "Times-Roman",
                    string n when n.Contains("Courier", StringComparison.OrdinalIgnoreCase) => "Courier",
                    _ => "Helvetica"
                };

                if (entry.Style == FontStyle.Bold) baseFont += "-Bold";
                else if (entry.Style == FontStyle.Italic) baseFont += "-Oblique";
                else if (entry.Style == FontStyle.BoldItalic) baseFont += "-BoldOblique";

                RecordObjectOffset();
                var num = AllocateObjectNumber();
                _buffer.AppendLine($"{num} 0 obj");
                _buffer.AppendLine($"<< /Type /Font /Subtype /Type1 /BaseFont /{baseFont} >>");
                _buffer.AppendLine("endobj");
                objNums.Add(num);

                _fontEntries[i] = new FontEntry
                {
                    FontKey = entry.FontKey,
                    FamilyName = entry.FamilyName,
                    Style = entry.Style,
                    FontDef = entry.FontDef,
                    ObjectNum = num
                };
            }
        }

        return objNums;
    }

    private int WriteFontResource()
    {
        RecordObjectOffset();
        var num = AllocateObjectNumber();
        _buffer.AppendLine($"{num} 0 obj");
        _buffer.AppendLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        _buffer.AppendLine("endobj");
        _fontObjects["Helvetica"] = num;
        return num;
    }

    public int WriteEmbeddedFont(string fontName, byte[]? fontData = null)
    {
        if (_fontObjects.TryGetValue(fontName, out var existing))
            return existing;

        RecordObjectOffset();
        var num = AllocateObjectNumber();
        var descriptorNum = AllocateObjectNumber();
        var fontFileNum = AllocateObjectNumber();

        if (fontData != null)
        {
            var compressed = DeflateCompress(fontData);
            RecordObjectOffset();
            _buffer.AppendLine($"{fontFileNum} 0 obj");
            _buffer.AppendLine($"<< /Length {compressed.Length} /Length1 {fontData.Length} /Filter /FlateDecode >>");
            _buffer.AppendLine("stream");
            _buffer.Append(Encoding.ASCII.GetString(compressed));
            _buffer.AppendLine();
            _buffer.AppendLine("endstream");
            _buffer.AppendLine("endobj");
        }

        RecordObjectOffset();
        _buffer.AppendLine($"{descriptorNum} 0 obj");
        _buffer.AppendLine("<< /Type /FontDescriptor");
        _buffer.AppendLine($"   /FontName /{EscapePdfString(fontName)}");
        if (fontData != null)
            _buffer.AppendLine($"   /FontFile2 {fontFileNum} 0 R");
        _buffer.AppendLine("   /Flags 32");
        _buffer.AppendLine("   /ItalicAngle 0");
        _buffer.AppendLine("   /Ascent 800");
        _buffer.AppendLine("   /Descent -200");
        _buffer.AppendLine("   /CapHeight 700");
        _buffer.AppendLine("   /StemV 80");
        _buffer.AppendLine(">>");
        _buffer.AppendLine("endobj");

        RecordObjectOffset();
        _buffer.AppendLine($"{num} 0 obj");
        _buffer.AppendLine("<< /Type /Font /Subtype /TrueType");
        _buffer.AppendLine($"   /BaseFont /{EscapePdfString(fontName)}");
        _buffer.AppendLine($"   /FontDescriptor {descriptorNum} 0 R");
        _buffer.AppendLine("   /FirstChar 32");
        _buffer.AppendLine("   /LastChar 255");
        _buffer.AppendLine("   /Encoding /WinAnsiEncoding");
        _buffer.AppendLine(">>");
        _buffer.AppendLine("endobj");

        _fontObjects[fontName] = num;
        return num;
    }

    public int WriteImage(string imageName, byte[] imageData, int width, int height)
    {
        if (_imageObjects.TryGetValue(imageName, out var existing))
            return existing;

        RecordObjectOffset();
        var num = AllocateObjectNumber();

        _buffer.AppendLine($"{num} 0 obj");
        _buffer.AppendLine("<< /Type /XObject /Subtype /Image");
        _buffer.AppendLine($"   /Width {width}");
        _buffer.AppendLine($"   /Height {height}");
        _buffer.AppendLine("   /ColorSpace /DeviceRGB");
        _buffer.AppendLine("   /BitsPerComponent 8");
        _buffer.AppendLine($"   /Length {imageData.Length}");
        _buffer.AppendLine(">>");
        _buffer.AppendLine("stream");
        _buffer.Append(imageData);
        _buffer.AppendLine();
        _buffer.AppendLine("endstream");
        _buffer.AppendLine("endobj");

        _imageObjects[imageName] = num;
        return num;
    }

    private int WriteDocumentInfo(string title)
    {
        RecordObjectOffset();
        var num = AllocateObjectNumber();
        var now = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        _buffer.AppendLine($"{num} 0 obj");
        _buffer.AppendLine("<<");
        _buffer.AppendLine($"   /Title {FormatPdfText(_metadata?.Title ?? title)}");
        _buffer.AppendLine($"   /Author {FormatPdfText(_metadata?.Author ?? "")}");
        _buffer.AppendLine($"   /Subject {FormatPdfText(_metadata?.Subject ?? "")}");
        _buffer.AppendLine($"   /Keywords {FormatPdfText(_metadata?.Keywords ?? "")}");
        _buffer.AppendLine($"   /Creator {FormatPdfText(_metadata?.Creator ?? "PdfEr")}");
        _buffer.AppendLine($"   /Producer {FormatPdfText(_metadata?.Producer ?? "PdfEr")}");
        _buffer.AppendLine($"   /CreationDate (D:{now})");
        _buffer.AppendLine($"   /ModDate (D:{now})");
        _buffer.AppendLine(">>");
        _buffer.AppendLine("endobj");
        return num;
    }

    private int WriteCatalog(int pagesRoot, int fontRef, int infoRef, int formObj = 0, int? bookmarkRoot = null)
    {
        RecordObjectOffset();
        var num = AllocateObjectNumber();
        _buffer.AppendLine($"{num} 0 obj");
        _buffer.AppendLine("<< /Type /Catalog");
        _buffer.AppendLine($"   /Pages {pagesRoot} 0 R");
        _buffer.AppendLine($"   /Version /{_pdfVersion ?? "1.7"}");

        if (bookmarkRoot.HasValue)
        {
            _buffer.AppendLine($"   /Outlines {bookmarkRoot.Value} 0 R");
        }

        if (formObj > 0)
        {
            _buffer.AppendLine($"   /AcroForm {formObj} 0 R");
        }

        _buffer.AppendLine(">>");
        _buffer.AppendLine("endobj");
        return num;
    }

    private int WriteFormStructure(FormDefinition form)
    {
        RecordObjectOffset();
        var num = AllocateObjectNumber();
        int[] fieldRefs = new int[form.Fields.Count];

        for (int i = 0; i < form.Fields.Count; i++)
        {
            fieldRefs[i] = WriteFormField(form.Fields[i]);
        }

        string fieldsArray = string.Join(" ", fieldRefs.Select(r => $"{r} 0 R"));
        _buffer.AppendLine($"{num} 0 obj");
        _buffer.AppendLine("<< /Type /Catalog");
        _buffer.AppendLine("   /DR << /Font << /F1 5 0 R >> >>");
        _buffer.AppendLine("   /Fields [" + fieldsArray + "]");
        _buffer.AppendLine("   /NeedAppearances " + (form.NeedAppearances ? "true" : "false"));
        _buffer.AppendLine(">>");
        _buffer.AppendLine("endobj");

        return num;
    }

    private int WriteFormField(FormField field)
    {
        RecordObjectOffset();
        var num = AllocateObjectNumber();

        string ft = field.Type switch
        {
            FormFieldType.Text => "/Tx",
            FormFieldType.CheckBox => "/Btn",
            FormFieldType.RadioButton => "/Btn",
            FormFieldType.ComboBox => "/Ch",
            FormFieldType.ListBox => "/Ch",
            FormFieldType.PushButton => "/Btn",
            FormFieldType.Signature => "/Sig",
            _ => "/Tx"
        };

        _buffer.AppendLine($"{num} 0 obj");
        _buffer.AppendLine("<<");
        _buffer.AppendLine($"   /FT {ft}");
        _buffer.AppendLine($"   /T {FormatPdfText(field.Name)}");
        _buffer.AppendLine($"   /Rect [{field.X:F2} {field.Y:F2} {field.X + field.Width:F2} {field.Y + field.Height:F2}]");
        _buffer.AppendLine("   /Subtype /Widget");
        _buffer.AppendLine($"   /Ff {(int)GetFieldFlags(field)}");
        if (!string.IsNullOrEmpty(field.DefaultValue))
            _buffer.AppendLine($"   /V {FormatPdfText(field.DefaultValue)}");
        if (!string.IsNullOrEmpty(field.DefaultValue))
            _buffer.AppendLine($"   /DV {FormatPdfText(field.DefaultValue)}");
        _buffer.AppendLine(">>");
        _buffer.AppendLine("endobj");

        return num;
    }

    private static uint GetFieldFlags(FormField field)
    {
        uint flags = 0;
        if (field.ReadOnly) flags |= 1;
        if (field.Required) flags |= 2;
        return flags;
    }

    private int WriteBookmarks(IReadOnlyList<BookmarkNode> bookmarks)
    {
        RecordObjectOffset();
        var rootNum = AllocateObjectNumber();

        var allNodes = new List<(BookmarkNode node, int objNum, int parentNum, int prevNum, int nextNum)>();
        int firstNum = 0, lastNum = 0;

        void ProcessNodes(IReadOnlyList<BookmarkNode> nodes, int parentNum, ref int firstSibling, ref int lastSibling)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var objNum = AllocateObjectNumber();
                int prev = i > 0 ? allNodes[allNodes.Count - 1].objNum : 0;
                int next = 0;

                allNodes.Add((node, objNum, parentNum, prev, next));

                if (i > 0 && allNodes.Count >= 2)
                {
                    var prevEntry = allNodes[allNodes.Count - 2];
                    allNodes[^2] = (prevEntry.node, prevEntry.objNum, prevEntry.parentNum, prevEntry.prevNum, objNum);
                }

                if (i == 0) firstSibling = objNum;
                lastSibling = objNum;
            }
        }

        int dummyFirst = 0, dummyLast = 0;
        ProcessNodes(bookmarks, rootNum, ref dummyFirst, ref dummyLast);
        firstNum = dummyFirst;
        lastNum = dummyLast;

        _buffer.AppendLine($"{rootNum} 0 obj");
        _buffer.AppendLine("<< /Type /Outlines");
        _buffer.AppendLine($"   /First {firstNum} 0 R");
        _buffer.AppendLine($"   /Last {lastNum} 0 R");
        _buffer.AppendLine("   /Count " + bookmarks.Count);
        _buffer.AppendLine(">>");
        _buffer.AppendLine("endobj");

        foreach (var (node, objNum, parentNum, prevNum, nextNum) in allNodes)
        {
            RecordObjectOffset();
            _buffer.AppendLine($"{objNum} 0 obj");
            _buffer.AppendLine("<< /Title " + FormatPdfText(node.Title) + "");
            _buffer.AppendLine("   /Parent " + parentNum + " 0 R");
            _buffer.AppendLine("   /Dest [" + _pageRefs[0] + " 0 R /XYZ " + node.PageX.ToString("F0") + " " + node.PageY.ToString("F0") + " 0]");
            if (prevNum > 0) _buffer.AppendLine("   /Prev " + prevNum + " 0 R");
            if (nextNum > 0) _buffer.AppendLine("   /Next " + nextNum + " 0 R");
            _buffer.AppendLine(">>");
            _buffer.AppendLine("endobj");
        }

        return rootNum;
    }

    private void WriteEncryptionDictionary(PdfEncryptionOptions options)
    {
        RecordObjectOffset();
        var num = AllocateObjectNumber();

        string filter =
            options.Level == EncryptionLevel.Aes40 ? "/V 2 /R 3 /Length 40" :
            options.Level == EncryptionLevel.Aes128 ? "/V 4 /R 4 /Length 128 /CF << /StdCF << /CFM /AESV2 /Length 128 >> >> /StrF /StdCF /StmF /StdCF" :
            "/V 5 /R 5 /Length 256 /CF << /StdCF << /CFM /AESV3 /Length 256 >> >> /StrF /StdCF /StmF /StdCF";

        _buffer.AppendLine($"{num} 0 obj");
        _buffer.AppendLine("<< /Filter /Standard");
        _buffer.AppendLine($"   {filter}");
        _buffer.AppendLine($"   /P {(uint)options.Permissions}");
        _buffer.AppendLine(">>");
        _buffer.AppendLine("endobj");
    }

    private void WriteXRefTable()
    {
        _currentOffset = _buffer.Length;
        _buffer.AppendLine("xref");
        _buffer.AppendLine($"0 {_nextObjectNumber}");
        _buffer.AppendLine("0000000000 65535 f ");
        foreach (var offset in _objectOffsets)
        {
            _buffer.AppendLine($"{offset:D10} 00000 n ");
        }
    }

    private void WriteTrailer(int catalogRef, int infoRef)
    {
        _buffer.AppendLine("trailer");
        _buffer.AppendLine($"<< /Size {_nextObjectNumber} /Root {catalogRef} 0 R /Info {infoRef} 0 R >>");
        _buffer.AppendLine("startxref");
        _buffer.AppendLine(_currentOffset.ToString());
        _buffer.AppendLine("%%EOF");
    }

    private void RecordObjectOffset()
    {
        _currentOffset = _buffer.Length;
        _objectOffsets.Add(_currentOffset);
    }

    private static string EscapePdfString(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c == '\\') sb.Append("\\\\");
            else if (c == '(') sb.Append("\\(");
            else if (c == ')') sb.Append("\\)");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c < 32 || c > 127) sb.Append(ToPdfOctal(c));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static string ToPdfOctal(int value)
    {
        int v = value & 0xFF;
        return $"\\{(v >> 6) & 7}{(v >> 3) & 7}{v & 7}";
    }

    private static string FormatPdfText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "()";

        bool needsUnicode = false;
        foreach (char c in text)
        {
            if (c > 255) { needsUnicode = true; break; }
        }

        if (!needsUnicode)
            return $"({EscapePdfString(text)})";

        var bytes = System.Text.Encoding.BigEndianUnicode.GetBytes("\uFEFF" + text);
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"<{hex}>";
    }

    private static byte[] DeflateCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal))
        {
            deflate.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }
}

public sealed class PdfStreamWriter : IDisposable
{
    private readonly Stream _output;
    private readonly StreamWriter _writer;
    private int _nextObjectNumber = 1;
    private readonly List<long> _objectOffsets = new();
    private long _currentOffset;
    private bool _disposed;
    private int _pagesRootRef;
    private readonly List<int> _pageRefs = new();

    public PdfStreamWriter(Stream output)
    {
        _output = output;
        _writer = new StreamWriter(output, Encoding.ASCII, 4096, leaveOpen: true);
    }

    private int AllocateObjectNumber() => _nextObjectNumber++;

    public void WriteHeader(PdfVersion version = PdfVersion.V1_7)
    {
        var ver = version switch
        {
            PdfVersion.V1_0 => "1.0", PdfVersion.V1_1 => "1.1", PdfVersion.V1_2 => "1.2",
            PdfVersion.V1_3 => "1.3", PdfVersion.V1_4 => "1.4", PdfVersion.V1_5 => "1.5",
            PdfVersion.V1_6 => "1.6", PdfVersion.V1_7 => "1.7", PdfVersion.V2_0 => "2.0",
            _ => "1.7"
        };
        WriteLine($"%PDF-{ver}");
        WriteLine("%\u00e2\u00e3\u00cf\u00d3");
    }

    public int BeginObject()
    {
        RecordOffset();
        var num = AllocateObjectNumber();
        WriteLine($"{num} 0 obj");
        return num;
    }

    public void EndObject()
    {
        WriteLine("endobj");
    }

    public int WriteStreamObject(byte[] streamData)
    {
        var num = BeginObject();
        WriteLine($"<< /Length {streamData.Length} >>");
        WriteLine("stream");
        _writer.Flush();
        _output.Write(streamData, 0, streamData.Length);
        _output.WriteByte((byte)'\n');
        EndObject();
        return num;
    }

    public int WriteCompressedStreamObject(byte[] data)
    {
        var compressed = DeflateCompressStream(data);
        var num = BeginObject();
        WriteLine($"<< /Length {compressed.Length} /Filter /FlateDecode >>");
        WriteLine("stream");
        _writer.Flush();
        _output.Write(compressed, 0, compressed.Length);
        _output.WriteByte((byte)'\n');
        EndObject();
        return num;
    }

    public int WritePagesRoot()
    {
        _pagesRootRef = BeginObject();
        WriteLine("<< /Type /Pages");
        WriteLine("   /Kids []");
        WriteLine("   /Count 0");
        WriteLine(">>");
        EndObject();
        return _pagesRootRef;
    }

    public int WritePage(int parentRef, string textContent)
    {
        var num = BeginObject();
        var contentNum = WriteStreamObject(Encoding.ASCII.GetBytes(
            $"BT\n/F1 12 Tf\n72 800 Td\n({EscapePdfString(textContent)}) Tj\nET\n"));

        WriteLine("<< /Type /Page");
        WriteLine($"   /Parent {parentRef} 0 R");
        WriteLine("   /MediaBox [0 0 595.28 841.89]");
        WriteLine($"   /Contents {contentNum} 0 R");
        WriteLine("   /Resources << /Font << /F1 5 0 R >> >>");
        WriteLine(">>");
        EndObject();

        _pageRefs.Add(num);
        return num;
    }

    public async Task WriteToFileAsync(string filePath, CancellationToken ct = default)
    {
        long xrefOffset = _currentOffset;

        WriteLine("xref");
        WriteLine($"0 {_nextObjectNumber}");
        WriteLine("0000000000 65535 f ");
        foreach (var offset in _objectOffsets)
            WriteLine($"{offset:D10} 00000 n ");

        WriteLine("trailer");
        WriteLine("<< /Size " + _nextObjectNumber + " /Root 4 0 R /Info 3 0 R >>");
        WriteLine("startxref");
        WriteLine(xrefOffset.ToString());
        WriteLine("%%EOF");
        await _writer.FlushAsync();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _writer.Flush();
            _writer.Dispose();
            if (_output is FileStream fs) fs.Dispose();
            _disposed = true;
        }
    }

    private void WriteLine(string text)
    {
        _writer.WriteLine(text);
        _currentOffset += Encoding.ASCII.GetByteCount(text) + 1;
    }

    private void RecordOffset()
    {
        _objectOffsets.Add(_currentOffset);
    }

    private static string EscapePdfString(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("(", "\\(")
            .Replace(")", "\\)")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }

    private static byte[] DeflateCompressStream(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal))
            deflate.Write(data, 0, data.Length);
        return output.ToArray();
    }
}
