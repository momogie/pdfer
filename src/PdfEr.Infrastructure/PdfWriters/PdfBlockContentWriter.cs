using System.Text;
using PdfEr.Core.Application.HtmlProcessing;
using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Styles;
using PdfEr.Core.Domain.ValueObjects;

namespace PdfEr.Infrastructure.PdfWriters;

public partial class PdfWriter
{
    private int WriteBlockContentStream(PageLayout page, PdfConverterConfiguration config, List<int> usedFonts, List<(string Name, int ObjNum)> pageImages, List<float>? usedOpacities = null, int totalPages = 1, List<LinkAnnotation>? linkAnnotations = null, Dictionary<string, (float y, float pageH, float marginTop)>? namedAnchors = null)
    {
        var num = AllocateObjectNumber();
        RecordObjectOffset(num);

        var pageW = page.Size.WidthMillimeters * MmToPt;
        var pageH = page.Size.HeightMillimeters * MmToPt;
        float marginLeftPt = page.Margins.Left * MmToPt;
        float marginTopPt = page.Margins.Top * MmToPt;

        var sb = new StringBuilder();
        var colorParser = new ColorParser();

        var allBlocks = new List<BlockBox>();
        if (page.Header?.RenderedContent != null && ShouldShowHeaderFooter(page.PageNumber, page.Header))
            allBlocks.Add(page.Header.RenderedContent);
        allBlocks.AddRange(page.Blocks);
        if (page.Footer?.RenderedContent != null && ShouldShowHeaderFooter(page.PageNumber, page.Footer))
            allBlocks.Add(page.Footer.RenderedContent);

        foreach (var block in allBlocks)
        {
            var style = block.ComputedStyle;

            if (usedOpacities != null && style != null)
            {
                var opVal = style.GetPropertyValue("opacity");
                if (!string.IsNullOrWhiteSpace(opVal) && float.TryParse(opVal, out var opacity) && opacity >= 0 && opacity < 1f)
                {
                    float rounded = MathF.Round(opacity * 10f) / 10f;
                    if (!usedOpacities.Contains(rounded))
                        usedOpacities.Add(rounded);
                }
            }
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

            float rectLeftPt = block.X * MmToPt + marginLeftPt;
            float rectBottomPt = pageH - ((block.Y + block.Height) * MmToPt) - marginTopPt;
            float rectWidthPt = block.Width * MmToPt;
            float rectHeightPt = block.Height * MmToPt;

            float borderRadiusPt = ParseBorderRadius(style);

            // Overflow hidden — apply clipping path
            string? overflow = style?.GetPropertyValue("overflow");
            bool clipContent = overflow is "hidden" or "clip" or "auto" or "scroll";

            float blockOpacity = 1f;
            if (style != null)
            {
                var opVal = style.GetPropertyValue("opacity");
                if (!string.IsNullOrWhiteSpace(opVal) && float.TryParse(opVal, out var op) && op >= 0 && op < 1f)
                {
                    blockOpacity = op;
                    float rounded = MathF.Round(op * 10f) / 10f;
                    string gsName = $"/GS_{rounded:F1}".Replace(".", "_");
                    sb.AppendLine($"{gsName} gs");
                }
            }

            // Clipping path for overflow hidden
            if (clipContent)
            {
                sb.AppendLine("q");
                sb.AppendLine($"{rectLeftPt:F2} {rectBottomPt:F2} {rectWidthPt:F2} {rectHeightPt:F2} re W n");
            }

            // Box-shadow
            string? boxShadow = style?.GetPropertyValue("box-shadow");
            if (!string.IsNullOrWhiteSpace(boxShadow) && boxShadow != "none")
            {
                var parts = boxShadow.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 && parts[0] != "inset")
                {
                    if (colorParser.TryParse(parts[^1], out var shColor) && shColor is RgbColor shRgb && shRgb.A > 0)
                    {
                        float offX = ParseShadowLength(parts[0]);
                        float offY = ParseShadowLength(parts[1]);
                        float blur = parts.Length >= 3 ? ParseShadowLength(parts[2]) : 0;
                        float shOpacity = shRgb.A / 255f;
                        if (shOpacity < 1f)
                        {
                            float sRounded = MathF.Round(shOpacity * 10f) / 10f;
                            string sGsName = $"/GS_{sRounded:F1}".Replace(".", "_");
                            if (!usedOpacities!.Contains(sRounded))
                                usedOpacities.Add(sRounded);
                            sb.AppendLine($"{sGsName} gs");
                        }
                        float shLeft = rectLeftPt + offX * MmToPt;
                        float shBottom = rectBottomPt - offY * MmToPt;
                        sb.AppendLine($"{shRgb.R / 255f:F2} {shRgb.G / 255f:F2} {shRgb.B / 255f:F2} rg");
                        if (blur > 0)
                        {
                            float blurPt = blur * MmToPt;
                            sb.AppendLine($"{shLeft:F2} {shBottom:F2} {rectWidthPt + blurPt * 2:F2} {rectHeightPt + blurPt * 2:F2} re f");
                        }
                        else
                            sb.AppendLine($"{shLeft:F2} {shBottom:F2} {rectWidthPt:F2} {rectHeightPt:F2} re f");
                        if (shOpacity < 1f)
                            sb.AppendLine("/GS_1_0 gs");
                    }
                }
            }

            // Background-image support
            string? bgImage = style?.GetPropertyValue("background-image");
            if (!string.IsNullOrWhiteSpace(bgImage) && bgImage != "none" && bgImage.StartsWith("url("))
            {
                var urlMatch = System.Text.RegularExpressions.Regex.Match(bgImage, @"url\([""']?([^""'\)]+)[""']?\)");
                if (urlMatch.Success)
                {
                    var imgSrc = urlMatch.Groups[1].Value;
                    var imgResult = LoadImageData(imgSrc);
                    if (imgResult != null)
                    {
                        var bgImgObjNum = WriteImage($"bg_{imgSrc}", imgResult.Data, imgResult.Width, imgResult.Height);
                        var bgImgName = $"/Img{bgImgObjNum}";
                        if (!pageImages.Any(p => p.Name == bgImgName))
                            pageImages.Add((bgImgName, bgImgObjNum));

                        string? bgRepeat = style?.GetPropertyValue("background-repeat");
                        string? bgSize = style?.GetPropertyValue("background-size");

                        float bgW = rectWidthPt;
                        float bgH = rectHeightPt;
                        if (bgSize != null && bgSize != "auto" && bgSize != "cover" && bgSize != "contain")
                        {
                            var sizeParts = bgSize.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (sizeParts.Length >= 1 && float.TryParse(sizeParts[0].Replace("px", "").Replace("pt", ""), out var sw))
                                bgW = sw * MmToPt;
                            if (sizeParts.Length >= 2 && float.TryParse(sizeParts[1].Replace("px", "").Replace("pt", ""), out var sh))
                                bgH = sh * MmToPt;
                        }

                        sb.AppendLine("q");
                        sb.AppendLine($"{bgW:F2} 0 0 {bgH:F2} {rectLeftPt:F2} {rectBottomPt:F2} cm");
                        sb.AppendLine($"{bgImgName} Do");
                        sb.AppendLine("Q");
                    }
                }
            }

            string? bgColorVal = null;
            if (style != null)
                bgColorVal = style.GetPropertyValue("background-color");
            if (!string.IsNullOrWhiteSpace(bgColorVal) && bgColorVal != "transparent" && bgColorVal != "rgba(0, 0, 0, 0)")
            {
                if (colorParser.TryParse(bgColorVal, out var docColor) && docColor is RgbColor bgRgb && bgRgb.A > 0)
                {
                    sb.AppendLine($"{bgRgb.R / 255f:F2} {bgRgb.G / 255f:F2} {bgRgb.B / 255f:F2} rg");
                    if (borderRadiusPt > 0)
                        AppendRoundedRectPath(sb, rectLeftPt, rectBottomPt, rectWidthPt, rectHeightPt, borderRadiusPt, "f");
                    else
                        sb.AppendLine($"{rectLeftPt:F2} {rectBottomPt:F2} {rectWidthPt:F2} {rectHeightPt:F2} re f");
                }
            }

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

                float bwPt = Math.Max(block.BorderTop, Math.Max(block.BorderBottom, Math.Max(block.BorderLeft, block.BorderRight))) * MmToPt;
                sb.AppendLine($"{bwPt:F2} w");

                // Border style (dashed/dotted)
                string? borderStyle = style?.GetPropertyValue("border-style");
                if (borderStyle is "dashed" or "dotted")
                {
                    float dashLen = borderStyle == "dashed" ? bwPt * 3f : bwPt;
                    float gapLen = borderStyle == "dashed" ? bwPt * 2f : bwPt * 2f;
                    sb.AppendLine($"[{dashLen:F2} {gapLen:F2}] 0 d");
                }

                if (borderRadiusPt > 0)
                    AppendRoundedRectPath(sb, rectLeftPt, rectBottomPt, rectWidthPt, rectHeightPt, borderRadiusPt, "S");
                else
                    sb.AppendLine($"{rectLeftPt:F2} {rectBottomPt:F2} {rectWidthPt:F2} {rectHeightPt:F2} re S");

                // Reset dash pattern if changed
                if (borderStyle is "dashed" or "dotted")
                    sb.AppendLine("[] 0 d");
            }

            var fontIdx = ResolveFontIndex(fontFamily, bold, italic);
            if (!usedFonts.Contains(fontIdx))
                usedFonts.Add(fontIdx);

            string? textColorVal = null;
            if (style != null)
                textColorVal = style.GetPropertyValue("color");
            if (!string.IsNullOrWhiteSpace(textColorVal) && textColorVal != "transparent" && textColorVal != "rgba(0, 0, 0, 0)")
            {
                if (colorParser.TryParse(textColorVal, out var tcDocColor) && tcDocColor is RgbColor tcRgb && tcRgb.A > 0)
                    sb.AppendLine($"{tcRgb.R / 255f:F2} {tcRgb.G / 255f:F2} {tcRgb.B / 255f:F2} rg");
            }

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

            // Generate link annotation if block has a URL
            if (block.LinkUrl != null && linkAnnotations != null)
            {
                float linkLeft = block.X * MmToPt + marginLeftPt;
                float linkBottom = pageH - ((block.Y + block.Height) * MmToPt) - marginTopPt;
                float linkRight = (block.X + block.Width) * MmToPt + marginLeftPt;
                float linkTop = pageH - (block.Y * MmToPt) - marginTopPt;
                linkAnnotations.Add(new LinkAnnotation
                {
                    Left = linkLeft,
                    Bottom = linkBottom,
                    Right = linkRight,
                    Top = linkTop,
                    Url = block.LinkUrl
                });
            }

            // Collect named anchors
            if (block.AnchorName != null && namedAnchors != null)
            {
                namedAnchors[block.AnchorName] = (block.Y, pageH, marginTopPt);
            }

            string? textAlign = null;
            if (style != null)
                textAlign = style.GetPropertyValue("text-align");

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
                string? simpleText = null;
                InlineBox? singleInlineForDeco = null;
                bool isSimpleText = false;

                if (block.InlineContent.Count == 0 && !string.IsNullOrEmpty(block.TextContent))
                {
                    simpleText = block.TextContent;
                    isSimpleText = true;
                }
                else if (block.InlineContent.Count == 1 && block.InlineContent[0].Type == InlineBoxType.Text && block.InlineContent[0].Text != null)
                {
                    simpleText = block.InlineContent[0].Text;
                    singleInlineForDeco = block.InlineContent[0];
                    isSimpleText = true;
                }

                float lineHeightPt = fontSizePt * GetLineHeight(style, 1.2f);
                float contentWidthPt = block.ContentWidth * MmToPt;

                if (isSimpleText && !string.IsNullOrEmpty(simpleText))
                {
                    WriteSimpleTextBlock(sb, simpleText, singleInlineForDeco,
                        contentWidthPt, lineHeightPt, blockXPt, blockYPt,
                        fontSizePt, textAlign, fontFamily, bold, italic, fontIdx,
                        style, colorParser, page.PageNumber, totalPages);
                }
                else if (block.InlineContent.Count > 0)
                {
                    WriteInlineContentBlock(sb, block,
                        contentWidthPt, blockXPt, blockYPt,
                        fontSizePt, textAlign, fontFamily, bold, italic, fontIdx,
                        marginLeftPt, marginTopPt, pageH, colorParser,
                        page.PageNumber, totalPages);
                }
            }

            // Close overflow clip if applied
            if (clipContent)
                sb.AppendLine("Q");
        }

        var stream = sb.ToString();
        var streamBytes = Encoding.ASCII.GetBytes(stream);
        _buffer.AppendLine($"{num} 0 obj");
        _buffer.AppendLine($"<< /Length {streamBytes.Length} >>");
        _buffer.AppendLine("stream");
        _buffer.Append(streamBytes);
        _buffer.AppendLine("endstream");
        _buffer.AppendLine("endobj");
        return num;
    }

    private static float ParseShadowLength(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        value = value.Trim().ToLowerInvariant();
        if (value.EndsWith("px") && float.TryParse(value[..^2], out var px)) return px * 0.2646f;
        if (value.EndsWith("pt") && float.TryParse(value[..^2], out var pt)) return pt * 0.3528f;
        if (value.EndsWith("mm") && float.TryParse(value[..^2], out var mm)) return mm;
        if (value.EndsWith("em") && float.TryParse(value[..^2], out var em)) return em * 3f;
        if (float.TryParse(value, out var n)) return n;
        return 0;
    }

    private sealed class ImageLoadResult
    {
        public byte[] Data { get; init; } = [];
        public int Width { get; init; }
        public int Height { get; init; }
    }

    private static ImageLoadResult? LoadImageData(string src)
    {
        try
        {
            var path = src;
            if (!Path.IsPathRooted(path))
                path = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, path));
            if (!File.Exists(path)) return null;

            using var img = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(path);
            int w = img.Width;
            int h = img.Height;
            var data = new byte[w * h * 3];
            int idx = 0;
            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < h; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < w; x++)
                    {
                        var pixel = row[x];
                        data[idx++] = pixel.R;
                        data[idx++] = pixel.G;
                        data[idx++] = pixel.B;
                    }
                }
            });
            return new ImageLoadResult { Data = data, Width = w, Height = h };
        }
        catch { return null; }
    }
}
