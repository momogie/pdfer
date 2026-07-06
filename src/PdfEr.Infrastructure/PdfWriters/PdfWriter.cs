using System.IO.Compression;
using System.Text;
using PdfEr.Core.Application.HtmlProcessing;
using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Enums;
using PdfEr.Core.Domain.Forms;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;
using PdfEr.Core.Domain.Toc;
using PdfEr.Core.Domain.Typography;
using PdfEr.Core.Domain.ValueObjects;

namespace PdfEr.Infrastructure.PdfWriters;

public partial class PdfWriter
{
    private readonly PdfBuffer _buffer = new();
    private int _nextObjectNumber = 1;
    private readonly Dictionary<int, long> _objectOffsets = new();
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
    private readonly Dictionary<string, HashSet<int>> _fontUsedChars = new();

    private struct LinkAnnotation
    {
        public float Left;
        public float Bottom;
        public float Right;
        public float Top;
        public string Url;
    }

    internal const float MmToPt = 72f / 25.4f;

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
        _fontUsedChars.Clear();
        _metadata = new PdfMetadata { Title = config.Title ?? "Document" };
        _encryption = null;

        WriteHeader(config.PdfVersion);

        var contentNums = new List<int>();
        var pageFontIndices = new List<List<int>>();
        var pageImageRefs = new List<List<(string Name, int ObjNum)>>();
        var pageOpacities = new List<List<float>>();
        var pageLinkAnnots = new List<List<LinkAnnotation>>();
        var pageNamedAnchors = new List<Dictionary<string, (float y, float pageH, float marginTop)>>();

        foreach (var page in layout.Pages)
        {
            var usedFonts = new List<int>();
            var images = new List<(string Name, int ObjNum)>();
            var usedOpacs = new List<float>();
            var linkAnnots = new List<LinkAnnotation>();
            var namedAnchors = new Dictionary<string, (float y, float pageH, float marginTop)>();
            var contentNum = WriteBlockContentStream(page, config, usedFonts, images, usedOpacs, layout.Pages.Count, linkAnnots, namedAnchors);
            contentNums.Add(contentNum);
            pageFontIndices.Add(usedFonts);
            pageImageRefs.Add(images);
            pageOpacities.Add(usedOpacs);
            pageLinkAnnots.Add(linkAnnots);
            pageNamedAnchors.Add(namedAnchors);
        }

        WriteAllFontObjects();

        // Write link annotation objects per page
        var linkAnnotObjNums = new List<List<int>>();
        foreach (var annots in pageLinkAnnots)
        {
            var objNums = new List<int>();
            foreach (var annot in annots)
            {
                var objNum = AllocateObjectNumber();
                RecordObjectOffset(objNum);
                _buffer.AppendLine($"{objNum} 0 obj");
                _buffer.AppendLine("<< /Type /Annot /Subtype /Link");
                _buffer.AppendLine($"   /Rect [{annot.Left:F2} {annot.Bottom:F2} {annot.Right:F2} {annot.Top:F2}]");
                _buffer.AppendLine($"   /Border [0 0 0]");
                string escapedUrl = EscapePdfString(annot.Url);
                _buffer.AppendLine($"   /A << /Type /Action /S /URI /URI ({escapedUrl}) >>");
                _buffer.AppendLine(">>");
                _buffer.AppendLine("endobj");
                objNums.Add(objNum);
            }
            linkAnnotObjNums.Add(objNums);
        }

        // Pre-allocate all page object numbers
        var pageNums = new List<int>();
        for (int i = 0; i < layout.Pages.Count; i++)
        {
            var pageNum = AllocateObjectNumber();
            pageNums.Add(pageNum);
            _pageRefs.Add(pageNum);
        }

        // Build bookmarks from layout headings
        var bookmarks = BuildBookmarks(layout);
        int? bookmarkRootNum = null;
        if (bookmarks.Count > 0)
            bookmarkRootNum = WriteBookmarks(bookmarks);

        var pagesRootRef = AllocateObjectNumber();
        RecordObjectOffset(pagesRootRef);
        _buffer.AppendLine($"{pagesRootRef} 0 obj");
        _buffer.AppendLine("<< /Type /Pages");
        _buffer.Append("   /Kids [");
        for (int i = 0; i < layout.Pages.Count; i++)
        {
            if (i > 0) _buffer.Append(" ");
            _buffer.Append($"{pageNums[i]} 0 R");
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

            RecordObjectOffset(pageNums[i]);
            _buffer.AppendLine($"{pageNums[i]} 0 obj");
            _buffer.AppendLine("<< /Type /Page");
            _buffer.AppendLine($"   /Parent {pagesRootRef} 0 R");
            _buffer.Append($"   /MediaBox [0 0 {pageW:F2} {pageH:F2}]");
            _buffer.AppendLine();
            _buffer.AppendLine($"   /Contents {contentNums[i]} 0 R");

            var fontResources = BuildFontResourcesDict(pageFontIndices[i]);
            var xObjectResources = BuildXObjectDict(pageImageRefs[i]);
            var extGStateResources = BuildExtGStateDict(pageOpacities[i]);
            _buffer.AppendLine($"   /Resources << /Font << {fontResources} >> {xObjectResources} {extGStateResources}>>");

            // Add link annotations if any
            var annots = linkAnnotObjNums[i];
            if (annots.Count > 0)
            {
                var annotRefs = string.Join(" ", annots.Select(a => $"{a} 0 R"));
                _buffer.AppendLine($"   /Annots [{annotRefs}]");
            }

            _buffer.AppendLine(">>");
            _buffer.AppendLine("endobj");
        }

        // Write page labels
        int pageLabelsNum = 0;
        if (layout.Pages.Any(p => !string.IsNullOrWhiteSpace(p.PageLabelPrefix) || !string.IsNullOrWhiteSpace(p.PageLabelStyle)))
            pageLabelsNum = WritePageLabels(layout.Pages);

        string title = config.Title ?? "Document";
        var infoRef = WriteDocumentInfo(title);
        var catalogRef = WriteCatalog(pagesRootRef, 0, infoRef, 0, bookmarkRootNum, pageLabelsNum > 0 ? pageLabelsNum : null);

        WriteXRefTable();
        WriteTrailer(catalogRef, infoRef);

        return _buffer.ToByteArray();
    }

    private static List<BookmarkNode> BuildBookmarks(DocumentLayout layout)
    {
        var bookmarks = new List<BookmarkNode>();
        foreach (var page in layout.Pages)
        {
            foreach (var block in page.Blocks)
            {
                if (block.TagName is "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
                    && !string.IsNullOrWhiteSpace(block.TextContent))
                {
                    var level = block.TagName[1] - '0';
                    var node = new BookmarkNode
                    {
                        Title = block.TextContent.Trim(),
                        PageNumber = page.PageNumber,
                        PageX = 0,
                        PageY = block.Y
                    };

                    // Insert into hierarchy by level
                    InsertBookmark(bookmarks, node, level, 1);
                }
            }
        }
        return bookmarks;
    }

    private static void InsertBookmark(List<BookmarkNode> siblings, BookmarkNode node, int level, int currentLevel)
    {
        if (level <= currentLevel || siblings.Count == 0)
        {
            siblings.Add(node);
            return;
        }

        var last = siblings[^1];
        InsertBookmark(last.Children, node, level, currentLevel + 1);
    }

    internal string BuildFontResourcesDict(List<int> fontIndices)
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

    internal static string BuildXObjectDict(List<(string Name, int ObjNum)> images)
    {
        if (images.Count == 0)
            return "";

        var parts = new List<string>();
        foreach (var (name, objNum) in images)
            parts.Add($"{name} {objNum} 0 R");

        return $"/XObject << {string.Join(" ", parts)} >> ";
    }

    internal static string BuildExtGStateDict(List<float> opacities)
    {
        if (opacities.Count == 0)
            return "";

        var parts = new List<string>();
        foreach (var op in opacities)
        {
            string gsName = $"/GS_{op:F1}".Replace(".", "_");
            string opStr = op.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            parts.Add($"{gsName} << /ca {opStr} /CA {opStr} >>");
        }
        return $"/ExtGState << {string.Join(" ", parts)} >> ";
    }

    private int AllocateObjectNumber() => _nextObjectNumber++;

    // Legacy document API

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

    // PDF structure writers

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
        _buffer.Append(new byte[] { (byte)'%', 0xe2, 0xe3, 0xcf, 0xd3, (byte)'\n' });
    }

    private int WritePagesRoot()
    {
        var num = AllocateObjectNumber();
        RecordObjectOffset(num);
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
        var num = AllocateObjectNumber();
        RecordObjectOffset(num);
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
        var num = AllocateObjectNumber();
        RecordObjectOffset(num);
        var formatted = FormatPdfText(textContent);

        var stream = $"BT\n/F1 12 Tf\n72 800 Td\n{formatted} Tj\nET\n";
        var streamBytes = Encoding.ASCII.GetBytes(stream);

        _buffer.AppendLine($"{num} 0 obj");
        _buffer.AppendLine("<< /Length " + streamBytes.Length + " >>");
        _buffer.AppendLine("stream");
        _buffer.Append(streamBytes);
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

                var fontFileNum = AllocateObjectNumber();
                RecordObjectOffset(fontFileNum);
                _buffer.AppendLine($"{fontFileNum} 0 obj");
                _buffer.AppendLine($"<< /Length {compressed.Length} /Length1 {entry.FontDef.FontData.Length} /Filter /FlateDecode >>");
                _buffer.AppendLine("stream");
                _buffer.Append(Encoding.ASCII.GetString(compressed));
                _buffer.AppendLine();
                _buffer.AppendLine("endstream");
                _buffer.AppendLine("endobj");

                var descriptorNum = AllocateObjectNumber();
                RecordObjectOffset(descriptorNum);
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

                var fontObjNum = AllocateObjectNumber();
                RecordObjectOffset(fontObjNum);
                _buffer.AppendLine($"{fontObjNum} 0 obj");
                _buffer.AppendLine("<< /Type /Font /Subtype /TrueType");
                _buffer.AppendLine($"   /BaseFont /{EscapePdfString(entry.FamilyName)}");
                _buffer.AppendLine($"   /FontDescriptor {descriptorNum} 0 R");
                _buffer.AppendLine("   /FirstChar 32");
                _buffer.AppendLine("   /LastChar 255");
                _buffer.AppendLine("   /Encoding /WinAnsiEncoding");

                // ToUnicode CMap for text extraction/search
                var toUnicodeNum = WriteToUnicodeCmap(entry.FamilyName);
                if (toUnicodeNum.HasValue)
                    _buffer.AppendLine($"   /ToUnicode {toUnicodeNum.Value} 0 R");

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

                var num = AllocateObjectNumber();
                RecordObjectOffset(num);
                _buffer.AppendLine($"{num} 0 obj");
                _buffer.AppendLine($"<< /Type /Font /Subtype /Type1 /BaseFont /{baseFont}");

                var toUnicodeNum = WriteToUnicodeCmap(entry.FamilyName);
                if (toUnicodeNum.HasValue)
                    _buffer.AppendLine($"   /ToUnicode {toUnicodeNum.Value} 0 R");

                _buffer.AppendLine(">>");
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
        var num = AllocateObjectNumber();
        RecordObjectOffset(num);
        _buffer.AppendLine($"{num} 0 obj");
        _buffer.AppendLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        _buffer.AppendLine("endobj");
        _fontObjects["Helvetica"] = num;
        return num;
    }

    private int? WriteToUnicodeCmap(string fontName)
    {
        if (!_fontUsedChars.TryGetValue(fontName, out var usedChars) || usedChars.Count == 0)
            return null;

        var cmapBuilder = new System.Text.StringBuilder();
        cmapBuilder.AppendLine("/CIDInit /ProcSet findresource begin");
        cmapBuilder.AppendLine("12 dict begin");
        cmapBuilder.AppendLine("begincmap");
        cmapBuilder.AppendLine("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def");
        cmapBuilder.AppendLine("/CMapName /Adobe-Identity-UCS def");
        cmapBuilder.AppendLine("/CMapType 2 def");
        cmapBuilder.AppendLine("1 begincodespacerange");
        cmapBuilder.AppendLine("<00> <FF>");
        cmapBuilder.AppendLine("endcodespacerange");
        cmapBuilder.AppendLine($"{usedChars.Count} beginbfchar");

        int code = 32;
        foreach (var ch in usedChars.OrderBy(c => c))
        {
            cmapBuilder.AppendLine($"<{code:X2}> <{ch:X4}>");
            code++;
            if (code > 255) break;
        }

        cmapBuilder.AppendLine("endbfchar");
        cmapBuilder.AppendLine("endcmap");
        cmapBuilder.AppendLine("CMapName currentdict /CMap defineresource pop");
        cmapBuilder.AppendLine("end");
        cmapBuilder.AppendLine("end");

        var cmapBytes = Encoding.ASCII.GetBytes(cmapBuilder.ToString());
        var toUnicodeNum = AllocateObjectNumber();
        RecordObjectOffset(toUnicodeNum);
        _buffer.AppendLine($"{toUnicodeNum} 0 obj");
        _buffer.AppendLine($"<< /Length {cmapBytes.Length} >>");
        _buffer.AppendLine("stream");
        _buffer.Append(cmapBytes);
        _buffer.AppendLine("endstream");
        _buffer.AppendLine("endobj");

        return toUnicodeNum;
    }

    public int WriteEmbeddedFont(string fontName, byte[]? fontData = null)
    {
        if (_fontObjects.TryGetValue(fontName, out var existing))
            return existing;

        var num = AllocateObjectNumber();
        var descriptorNum = AllocateObjectNumber();
        var fontFileNum = AllocateObjectNumber();

        if (fontData != null)
        {
            var compressed = DeflateCompress(fontData);

            RecordObjectOffset(fontFileNum);
            _buffer.AppendLine($"{fontFileNum} 0 obj");
            _buffer.AppendLine($"<< /Length {compressed.Length} /Length1 {fontData.Length} /Filter /FlateDecode >>");
            _buffer.AppendLine("stream");
            _buffer.Append(Encoding.ASCII.GetString(compressed));
            _buffer.AppendLine();
            _buffer.AppendLine("endstream");
            _buffer.AppendLine("endobj");
        }

        RecordObjectOffset(descriptorNum);
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

        RecordObjectOffset(num);
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

            // Generate ToUnicode CMap for subsetted font
            if (_fontUsedChars.TryGetValue(fontName, out var usedChars) && usedChars.Count > 0)
            {
                var cmapBuilder = new System.Text.StringBuilder();
                cmapBuilder.AppendLine("/CIDInit /ProcSet findresource begin");
                cmapBuilder.AppendLine("12 dict begin");
                cmapBuilder.AppendLine("begincmap");
                cmapBuilder.AppendLine("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def");
                cmapBuilder.AppendLine("/CMapName /Adobe-Identity-UCS def");
                cmapBuilder.AppendLine("/CMapType 2 def");
                cmapBuilder.AppendLine("1 begincodespacerange");
                cmapBuilder.AppendLine("<00> <FF>");
                cmapBuilder.AppendLine("endcodespacerange");
                cmapBuilder.AppendLine($"{usedChars.Count} beginbfchar");

                int code = 32;
                foreach (var ch in usedChars.OrderBy(c => c))
                {
                    cmapBuilder.AppendLine($"<{code:X2}> <{ch:X4}>");
                    code++;
                    if (code > 255) break;
                }

                cmapBuilder.AppendLine("endbfchar");
                cmapBuilder.AppendLine("endcmap");
                cmapBuilder.AppendLine("CMapName currentdict /CMap defineresource pop");
                cmapBuilder.AppendLine("end");
                cmapBuilder.AppendLine("end");

                var cmapBytes = Encoding.ASCII.GetBytes(cmapBuilder.ToString());

                var toUnicodeNum = AllocateObjectNumber();
                RecordObjectOffset(toUnicodeNum);
                _buffer.AppendLine($"{toUnicodeNum} 0 obj");
                _buffer.AppendLine($"<< /Length {cmapBytes.Length} >>");
                _buffer.AppendLine("stream");
                _buffer.Append(cmapBytes);
                _buffer.AppendLine("endstream");
                _buffer.AppendLine("endobj");

                _buffer.AppendLine($"   /ToUnicode {toUnicodeNum} 0 R");
            }

        return num;
    }

    public void RecordUsedChars(string fontName, string text)
    {
        if (string.IsNullOrEmpty(fontName) || string.IsNullOrEmpty(text))
            return;
        if (!_fontUsedChars.ContainsKey(fontName))
            _fontUsedChars[fontName] = new HashSet<int>();
        foreach (char c in text)
            _fontUsedChars[fontName].Add(c);
    }

    public int WriteImage(string imageName, byte[] imageData, int width, int height)
    {
        if (_imageObjects.TryGetValue(imageName, out var existing))
            return existing;

        var num = AllocateObjectNumber();
        RecordObjectOffset(num);

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
        var num = AllocateObjectNumber();
        RecordObjectOffset(num);
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

    internal int WriteCatalog(int pagesRoot, int fontRef, int infoRef, int formObj = 0, int? bookmarkRoot = null, int? pageLabelsObj = null)
    {
        var num = AllocateObjectNumber();
        RecordObjectOffset(num);
        _buffer.AppendLine($"{num} 0 obj");
        _buffer.AppendLine("<< /Type /Catalog");
        _buffer.AppendLine($"   /Pages {pagesRoot} 0 R");
        _buffer.AppendLine($"   /Version /{_pdfVersion ?? "1.7"}");

        if (bookmarkRoot.HasValue)
            _buffer.AppendLine($"   /Outlines {bookmarkRoot.Value} 0 R");

        if (pageLabelsObj.HasValue)
            _buffer.AppendLine($"   /PageLabels {pageLabelsObj.Value} 0 R");

        if (formObj > 0)
            _buffer.AppendLine($"   /AcroForm {formObj} 0 R");

        _buffer.AppendLine(">>");
        _buffer.AppendLine("endobj");
        return num;
    }

    private int WriteFormStructure(FormDefinition form)
    {
        var num = AllocateObjectNumber();
        RecordObjectOffset(num);
        int[] fieldRefs = new int[form.Fields.Count];

        for (int i = 0; i < form.Fields.Count; i++)
            fieldRefs[i] = WriteFormField(form.Fields[i]);

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
        var num = AllocateObjectNumber();
        RecordObjectOffset(num);

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
        var rootNum = AllocateObjectNumber();
        RecordObjectOffset(rootNum);

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
            RecordObjectOffset(objNum);
            int pageRef = node.PageNumber > 0 && node.PageNumber <= _pageRefs.Count
                ? _pageRefs[node.PageNumber - 1] : _pageRefs[0];
            _buffer.AppendLine($"{objNum} 0 obj");
            _buffer.AppendLine("<< /Title " + FormatPdfText(node.Title) + "");
            _buffer.AppendLine("   /Parent " + parentNum + " 0 R");
            _buffer.AppendLine("   /Dest [" + pageRef + " 0 R /XYZ " + node.PageX.ToString("F0") + " " + node.PageY.ToString("F0") + " 0]");
            if (prevNum > 0) _buffer.AppendLine("   /Prev " + prevNum + " 0 R");
            if (nextNum > 0) _buffer.AppendLine("   /Next " + nextNum + " 0 R");
            _buffer.AppendLine(">>");
            _buffer.AppendLine("endobj");
        }

        return rootNum;
    }

    private int WritePageLabels(IReadOnlyList<PageLayout> pages)
    {
        var num = AllocateObjectNumber();
        RecordObjectOffset(num);

        _buffer.AppendLine($"{num} 0 obj");
        _buffer.AppendLine("<< /Nums [");
        int startIdx = 0;
        for (int i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            bool hasPrefix = !string.IsNullOrWhiteSpace(page.PageLabelPrefix);
            bool hasStyle = !string.IsNullOrWhiteSpace(page.PageLabelStyle);
            if (!hasPrefix && !hasStyle)
                continue;

            _buffer.AppendLine($"   {startIdx}");
            _buffer.Append("   << ");
            if (hasStyle)
                _buffer.Append($"/S /{page.PageLabelStyle} ");
            if (hasPrefix)
                _buffer.Append($"/P ({EscapePdfString(page.PageLabelPrefix!)}) ");
            _buffer.AppendLine(">>");
            startIdx = i + 1;
        }
        _buffer.AppendLine("]");
        _buffer.AppendLine(">>");
        _buffer.AppendLine("endobj");
        return num;
    }

    private void WriteEncryptionDictionary(PdfEncryptionOptions options)
    {
        var num = AllocateObjectNumber();
        RecordObjectOffset(num);

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
        for (int i = 1; i < _nextObjectNumber; i++)
        {
            if (_objectOffsets.TryGetValue(i, out var offset))
                _buffer.AppendLine($"{offset:D10} 00000 n ");
            else
                _buffer.AppendLine("0000000000 00000 n ");
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

    private void RecordObjectOffset(int objectNumber)
    {
        _currentOffset = _buffer.Length;
        _objectOffsets[objectNumber] = _currentOffset;
    }

    internal static string EscapePdfString(string text)
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

    internal static string FormatPdfText(string text)
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

    internal static byte[] DeflateCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal))
        {
            deflate.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }
}
