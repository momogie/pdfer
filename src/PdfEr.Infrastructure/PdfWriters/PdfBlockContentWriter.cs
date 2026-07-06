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

            // Box-shadow (drawn before clip so it's not clipped)
            string? boxShadow = style?.GetPropertyValue("box-shadow");
            if (!string.IsNullOrWhiteSpace(boxShadow) && boxShadow != "none")
            {
                var shadows = boxShadow.Split(',');
                foreach (var shadowStr in shadows)
                {
                    var parts = shadowStr.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 3) continue;

                    bool isInset = parts[0] == "inset";
                    int idx = isInset ? 1 : 0;

                    if (idx + 3 > parts.Length) continue;

                    // Color is always the last component
                    if (!colorParser.TryParse(parts[^1], out var shColor) || shColor is not RgbColor shRgb || shRgb.A == 0)
                        continue;

                    float offX = ParseShadowLength(parts[idx]);
                    float offY = ParseShadowLength(parts[idx + 1]);
                    float blur = (idx + 2 < parts.Length - 1) ? ParseShadowLength(parts[idx + 2]) : 0;
                    float spread = (idx + 3 < parts.Length - 1) ? ParseShadowLength(parts[idx + 3]) : 0;

                    float shOpacity = shRgb.A / 255f;
                    if (shOpacity < 1f)
                    {
                        float sRounded = MathF.Round(shOpacity * 10f) / 10f;
                        string sGsName = $"/GS_{sRounded:F1}".Replace(".", "_");
                        if (!usedOpacities!.Contains(sRounded))
                            usedOpacities.Add(sRounded);
                        sb.AppendLine($"{sGsName} gs");
                    }

                    float spreadPt = spread * MmToPt;
                    float offXPt = offX * MmToPt;
                    float offYPt = offY * MmToPt;

                    if (isInset)
                    {
                        sb.AppendLine("q");
                        sb.AppendLine($"{rectLeftPt:F2} {rectBottomPt:F2} {rectWidthPt:F2} {rectHeightPt:F2} re W n");

                        float innerLeft = rectLeftPt + offXPt - spreadPt;
                        float innerBottom = rectBottomPt - offYPt - spreadPt;
                        float innerW = rectWidthPt + Math.Abs(spreadPt) * 2;
                        float innerH = rectHeightPt + Math.Abs(spreadPt) * 2;

                        sb.AppendLine($"{shRgb.R / 255f:F2} {shRgb.G / 255f:F2} {shRgb.B / 255f:F2} rg");
                        if (blur > 0)
                        {
                            float bPt = blur * MmToPt;
                            innerLeft -= bPt; innerBottom -= bPt;
                            innerW += bPt * 2; innerH += bPt * 2;
                        }
                        sb.AppendLine($"{innerLeft:F2} {innerBottom:F2} {innerW:F2} {innerH:F2} re f");
                        sb.AppendLine("Q");
                    }
                    else
                    {
                        float shLeft = rectLeftPt + offXPt - spreadPt;
                        float shBottom = rectBottomPt - offYPt - spreadPt;
                        float shW = rectWidthPt + spreadPt * 2;
                        float shH = rectHeightPt + spreadPt * 2;

                        sb.AppendLine($"{shRgb.R / 255f:F2} {shRgb.G / 255f:F2} {shRgb.B / 255f:F2} rg");

                        if (blur > 0)
                        {
                            float bPt = blur * MmToPt;
                            sb.AppendLine($"{shLeft - bPt:F2} {shBottom - bPt:F2} {shW + bPt * 2:F2} {shH + bPt * 2:F2} re f");
                        }
                        else
                        {
                            sb.AppendLine($"{shLeft:F2} {shBottom:F2} {shW:F2} {shH:F2} re f");
                        }
                    }

                    if (shOpacity < 1f)
                        sb.AppendLine("/GS_1_0 gs");
                }
            }

            // Clipping path for overflow hidden and/or border-radius (applies to background and content)
            bool needClip = clipContent || borderRadiusPt > 0;
            if (needClip)
            {
                sb.AppendLine("q");
                if (borderRadiusPt > 0)
                    AppendRoundedRectPath(sb, rectLeftPt, rectBottomPt, rectWidthPt, rectHeightPt, borderRadiusPt, "W n");
                else
                    sb.AppendLine($"{rectLeftPt:F2} {rectBottomPt:F2} {rectWidthPt:F2} {rectHeightPt:F2} re W n");
            }

            // Background-image support (multiple backgrounds via comma)
            string? bgImageRaw = style?.GetPropertyValue("background-image");
            if (!string.IsNullOrWhiteSpace(bgImageRaw) && bgImageRaw != "none")
            {
                var bgImages = bgImageRaw.Split(',');
                string? bgPosRaw = style?.GetPropertyValue("background-position");
                string? bgSizeRaw = style?.GetPropertyValue("background-size");
                string? bgRepeatRaw = style?.GetPropertyValue("background-repeat");

                var bgPositions = bgPosRaw?.Split(',') ?? ["0% 0%"];
                var bgSizes = bgSizeRaw?.Split(',') ?? ["auto"];
                var bgRepeats = bgRepeatRaw?.Split(',') ?? ["repeat"];

                for (int bi = 0; bi < bgImages.Length; bi++)
                {
                    var bgImage = bgImages[bi].Trim();
                    if (!bgImage.StartsWith("url(")) continue;

                    var urlMatch = System.Text.RegularExpressions.Regex.Match(bgImage, @"url\([""']?([^""'\)]+)[""']?\)");
                    if (!urlMatch.Success) continue;

                    var imgSrc = urlMatch.Groups[1].Value;
                    var imgResult = LoadImageData(imgSrc);
                    if (imgResult == null) continue;

                    var bgImgObjNum = WriteImage($"bg_{imgSrc}", imgResult.Data, imgResult.Width, imgResult.Height);
                    var bgImgName = $"/Img{bgImgObjNum}";
                    if (!pageImages.Any(p => p.Name == bgImgName))
                        pageImages.Add((bgImgName, bgImgObjNum));

                    float imgW = imgResult.Width;
                    float imgH = imgResult.Height;

                    // Parse background-size
                    var bgSize = bi < bgSizes.Length ? bgSizes[bi].Trim() : bgSizes[^1].Trim();
                    float renderW = rectWidthPt;
                    float renderH = rectHeightPt;
                    float imgAspect = imgW / imgH;
                    float containerAspect = rectWidthPt / rectHeightPt;

                    if (bgSize == "cover")
                    {
                        if (imgAspect > containerAspect)
                        { renderW = rectWidthPt; renderH = rectWidthPt / imgAspect; }
                        else
                        { renderH = rectHeightPt; renderW = rectHeightPt * imgAspect; }
                    }
                    else if (bgSize == "contain")
                    {
                        if (imgAspect > containerAspect)
                        { renderH = rectHeightPt; renderW = rectHeightPt * imgAspect; }
                        else
                        { renderW = rectWidthPt; renderH = rectWidthPt / imgAspect; }
                    }
                    else if (bgSize != "auto")
                    {
                        var sizeParts = bgSize.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (sizeParts.Length >= 1)
                        {
                            if (sizeParts[0].EndsWith('%') && float.TryParse(sizeParts[0][..^1], out var wpct))
                                renderW = rectWidthPt * wpct / 100f;
                else
                            renderW = CssLengthParser.ParseCssLengthMm(sizeParts[0], rectWidthPt) * MmToPt;
                        }
                        if (sizeParts.Length >= 2)
                        {
                            if (sizeParts[1].EndsWith('%') && float.TryParse(sizeParts[1][..^1], out var hpct))
                                renderH = rectHeightPt * hpct / 100f;
                            else
                                renderH = CssLengthParser.ParseCssLengthMm(sizeParts[1], rectHeightPt) * MmToPt;
                        }
                        else if (sizeParts.Length == 1 && sizeParts[0] != "auto")
                        {
                            // Single value sets width, height auto
                            renderH = renderW / imgAspect;
                        }
                    }

                    // Parse background-position
                    var bgPos = bi < bgPositions.Length ? bgPositions[bi].Trim() : bgPositions[^1].Trim();
                    float posXPt = 0, posYPt = 0;
                    var posParts = bgPos.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (posParts.Length >= 1)
                    {
                        if (posParts[0] == "center") posXPt = (rectWidthPt - renderW) / 2f;
                        else if (posParts[0] == "right") posXPt = rectWidthPt - renderW;
                        else if (posParts[0] == "left") posXPt = 0;
                        else if (posParts[0].EndsWith('%') && float.TryParse(posParts[0][..^1], out var xpct))
                            posXPt = (rectWidthPt - renderW) * xpct / 100f;
                        else
                            posXPt = CssLengthParser.ParseCssLengthMm(posParts[0], rectWidthPt) * MmToPt;
                    }
                    if (posParts.Length >= 2)
                    {
                        if (posParts[1] == "center") posYPt = (rectHeightPt - renderH) / 2f;
                        else if (posParts[1] == "bottom") posYPt = rectHeightPt - renderH;
                        else if (posParts[1] == "top") posYPt = 0;
                        else if (posParts[1].EndsWith('%') && float.TryParse(posParts[1][..^1], out var ypct))
                            posYPt = (rectHeightPt - renderH) * ypct / 100f;
                        else
                            posYPt = CssLengthParser.ParseCssLengthMm(posParts[1], rectHeightPt) * MmToPt;
                    }

                    // Parse background-repeat
                    var bgRepeat = bi < bgRepeats.Length ? bgRepeats[bi].Trim() : bgRepeats[^1].Trim();

                    if (bgRepeat == "no-repeat")
                    {
                        sb.AppendLine("q");
                        sb.AppendLine($"{renderW:F2} 0 0 {renderH:F2} {rectLeftPt + posXPt:F2} {rectBottomPt + posYPt:F2} cm");
                        sb.AppendLine($"{bgImgName} Do");
                        sb.AppendLine("Q");
                    }
                    else
                    {
                        // repeat / repeat-x / repeat-y: tile the image
                        float stepX = bgRepeat == "repeat-y" ? rectWidthPt + 1 : renderW;
                        float stepY = bgRepeat == "repeat-x" ? rectHeightPt + 1 : renderH;
                        int tilesX = bgRepeat == "no-repeat" ? 1 : (int)Math.Ceiling((rectWidthPt - posXPt) / stepX) + 1;
                        int tilesY = bgRepeat == "no-repeat" ? 1 : (int)Math.Ceiling((rectHeightPt - posYPt) / stepY) + 1;

                        for (int tx = 0; tx < tilesX; tx++)
                        {
                            for (int ty = 0; ty < tilesY; ty++)
                            {
                                float tileX = rectLeftPt + posXPt + tx * stepX;
                                float tileY = rectBottomPt + posYPt + ty * stepY;
                                if (tileX > rectLeftPt + rectWidthPt || tileY > rectBottomPt + rectHeightPt)
                                    continue;
                                sb.AppendLine("q");
                                sb.AppendLine($"{renderW:F2} 0 0 {renderH:F2} {tileX:F2} {tileY:F2} cm");
                                sb.AppendLine($"{bgImgName} Do");
                                sb.AppendLine("Q");
                            }
                        }
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
                    float bgAlpha = bgRgb.A / 255f;
                    if (bgAlpha < 1f)
                    {
                        float aRounded = MathF.Round(bgAlpha * 10f) / 10f;
                        string aGsName = $"/GS_{aRounded:F1}".Replace(".", "_");
                        if (!usedOpacities!.Contains(aRounded))
                            usedOpacities.Add(aRounded);
                        sb.AppendLine($"{aGsName} gs");
                    }
                    sb.AppendLine($"{bgRgb.R / 255f:F2} {bgRgb.G / 255f:F2} {bgRgb.B / 255f:F2} rg");
                    if (borderRadiusPt > 0)
                        AppendRoundedRectPath(sb, rectLeftPt, rectBottomPt, rectWidthPt, rectHeightPt, borderRadiusPt, "f");
                    else
                        sb.AppendLine($"{rectLeftPt:F2} {rectBottomPt:F2} {rectWidthPt:F2} {rectHeightPt:F2} re f");
                    if (bgAlpha < 1f)
                        sb.AppendLine("/GS_1_0 gs");
                }
            }

            if (hasBorders)
                DrawBorders(sb, block, style, rectLeftPt, rectBottomPt, rectWidthPt, rectHeightPt, borderRadiusPt, colorParser);

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

            // Close clip (overflow and/or border-radius)
            if (needClip)
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

    private readonly struct BorderRadii
    {
        public float TopLeft { get; }
        public float TopRight { get; }
        public float BottomRight { get; }
        public float BottomLeft { get; }

        public BorderRadii(float uniform)
        {
            TopLeft = TopRight = BottomRight = BottomLeft = uniform;
        }

        public BorderRadii(float tl, float tr, float br, float bl)
        {
            TopLeft = tl; TopRight = tr; BottomRight = br; BottomLeft = bl;
        }

        public static BorderRadii Parse(CssDeclarationBlock? style, float mmToPt)
        {
            if (style == null) return new BorderRadii(0);

            var val = style.GetPropertyValue("border-radius");
            if (string.IsNullOrWhiteSpace(val)) return new BorderRadii(0);

            val = val.Trim().ToLowerInvariant();
            var parts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            float Parse(string v) => v switch
            {
                string s when s.EndsWith("mm") && float.TryParse(s[..^2], out var mm) => mm * mmToPt,
                string s when s.EndsWith("pt") && float.TryParse(s[..^2], out var pt) => pt,
                string s when s.EndsWith("px") && float.TryParse(s[..^2], out var px) => px * 0.75f,
                string s when s.EndsWith("em") && float.TryParse(s[..^2], out var em) => em * 12f,
                string s when float.TryParse(s, out var raw) => raw * mmToPt,
                _ => 0
            };

            if (parts.Length == 1) return new BorderRadii(Parse(parts[0]));
            if (parts.Length == 2)
            {
                float tlbr = Parse(parts[0]), trbl = Parse(parts[1]);
                return new BorderRadii(tlbr, trbl, tlbr, trbl);
            }
            if (parts.Length == 3)
                return new BorderRadii(Parse(parts[0]), Parse(parts[1]), Parse(parts[2]), Parse(parts[1]));
            if (parts.Length >= 4)
                return new BorderRadii(Parse(parts[0]), Parse(parts[1]), Parse(parts[3]), Parse(parts[2]));
            return new BorderRadii(0);
        }
    }

    private void DrawBorders(StringBuilder sb, BlockBox block, CssDeclarationBlock? style,
        float rectLeftPt, float rectBottomPt, float rectWidthPt, float rectHeightPt,
        float borderRadiusPt, ColorParser colorParser)
    {
        float ParseColor(string? val, out float r, out float g, out float b)
        {
            r = g = b = 0;
            if (string.IsNullOrWhiteSpace(val) || val == "transparent") return 0;
            if (!colorParser.TryParse(val, out var docColor) || docColor is not RgbColor rgb) return 0;
            if (rgb.A == 0) return 0;
            r = rgb.R / 255f; g = rgb.G / 255f; b = rgb.B / 255f;
            return rgb.A / 255f;
        }

        // Parse per-side colors and styles (default to currentColor = the element's color or black)
        string? currentColor = style?.GetPropertyValue("color");
        if (string.IsNullOrWhiteSpace(currentColor) || currentColor == "transparent" || currentColor == "rgba(0, 0, 0, 0)")
            currentColor = "black";

        string? borderColor = style?.GetPropertyValue("border-color");
        string? topColor = style?.GetPropertyValue("border-top-color") ?? borderColor ?? currentColor;
        string? rightColor = style?.GetPropertyValue("border-right-color") ?? borderColor ?? currentColor;
        string? bottomColor = style?.GetPropertyValue("border-bottom-color") ?? borderColor ?? currentColor;
        string? leftColor = style?.GetPropertyValue("border-left-color") ?? borderColor ?? currentColor;

        string? topStyle = style?.GetPropertyValue("border-top-style") ?? style?.GetPropertyValue("border-style") ?? "solid";
        string? rightStyle = style?.GetPropertyValue("border-right-style") ?? style?.GetPropertyValue("border-style") ?? "solid";
        string? bottomStyle = style?.GetPropertyValue("border-bottom-style") ?? style?.GetPropertyValue("border-style") ?? "solid";
        string? leftStyle = style?.GetPropertyValue("border-left-style") ?? style?.GetPropertyValue("border-style") ?? "solid";

        float topW = block.BorderTop * MmToPt;
        float rightW = block.BorderRight * MmToPt;
        float bottomW = block.BorderBottom * MmToPt;
        float leftW = block.BorderLeft * MmToPt;

        bool allSameColor = topColor == rightColor && rightColor == bottomColor && bottomColor == leftColor;
        bool allSameStyle = topStyle == rightStyle && rightStyle == bottomStyle && bottomStyle == leftStyle;
        bool allSameWidth = Math.Abs(topW - rightW) < 0.001f && Math.Abs(rightW - bottomW) < 0.001f && Math.Abs(bottomW - leftW) < 0.001f;

        if (allSameColor && allSameStyle && allSameWidth && borderRadiusPt == 0 && topW > 0)
        {
            // Fast path: uniform border, no radius — single rect outline
            var fastAlpha = ParseColor(topColor, out var br, out var bg, out var bb);
            if (fastAlpha == 0) return;
            sb.AppendLine($"{br:F2} {bg:F2} {bb:F2} RG");

            float bwPt = Math.Max(topW, Math.Max(bottomW, Math.Max(leftW, rightW)));
            sb.AppendLine($"{bwPt:F2} w");
            SetDashPattern(sb, topStyle, bwPt);
            sb.AppendLine($"{rectLeftPt:F2} {rectBottomPt:F2} {rectWidthPt:F2} {rectHeightPt:F2} re S");
            ResetDashPattern(sb, topStyle);
            return;
        }

        // Per-side border drawing
        string? borderStyle = style?.GetPropertyValue("border-style") ?? "solid";
        float maxW = Math.Max(topW, Math.Max(bottomW, Math.Max(leftW, rightW)));

        // Top edge
        DrawBorderSide(sb, topStyle ?? borderStyle, topColor, rectLeftPt, rectBottomPt + rectHeightPt - topW / 2f,
            rectLeftPt + rectWidthPt, rectBottomPt + rectHeightPt - topW / 2f, topW, colorParser);

        // Bottom edge
        DrawBorderSide(sb, bottomStyle ?? borderStyle, bottomColor, rectLeftPt, rectBottomPt + bottomW / 2f,
            rectLeftPt + rectWidthPt, rectBottomPt + bottomW / 2f, bottomW, colorParser);

        // Left edge
        DrawBorderSide(sb, leftStyle ?? borderStyle, leftColor, rectLeftPt + leftW / 2f, rectBottomPt,
            rectLeftPt + leftW / 2f, rectBottomPt + rectHeightPt, leftW, colorParser);

        // Right edge
        DrawBorderSide(sb, rightStyle ?? borderStyle, rightColor, rectLeftPt + rectWidthPt - rightW / 2f, rectBottomPt,
            rectLeftPt + rectWidthPt - rightW / 2f, rectBottomPt + rectHeightPt, rightW, colorParser);

        // Corner fill: draw small rectangles at corners where adjacent sides meet
        if (topW > 0 && leftW > 0)
            FillCorner(sb, topColor, leftColor, rectLeftPt, rectBottomPt + rectHeightPt, leftW, topW, colorParser);
        if (topW > 0 && rightW > 0)
            FillCorner(sb, topColor, rightColor, rectLeftPt + rectWidthPt - rightW, rectBottomPt + rectHeightPt, rightW, topW, colorParser);
        if (bottomW > 0 && leftW > 0)
            FillCorner(sb, bottomColor, leftColor, rectLeftPt, rectBottomPt, leftW, bottomW, colorParser);
        if (bottomW > 0 && rightW > 0)
            FillCorner(sb, bottomColor, rightColor, rectLeftPt + rectWidthPt - rightW, rectBottomPt, rightW, bottomW, colorParser);
    }

    private void DrawBorderSide(StringBuilder sb, string? style, string? color,
        float x1, float y1, float x2, float y2, float width, ColorParser colorParser)
    {
        if (width <= 0 || string.IsNullOrWhiteSpace(color) || color == "transparent") return;

        ParseColorValue(color, colorParser, out var r, out var g, out var b, out var a);
        if (a == 0) return;

        if (style is "none" or "hidden") return;

        sb.AppendLine($"{r:F2} {g:F2} {b:F2} RG");
        sb.AppendLine($"{width:F2} w");
        SetDashPattern(sb, style, width);
        sb.AppendLine($"{x1:F2} {y1:F2} m {x2:F2} {y2:F2} l S");
        ResetDashPattern(sb, style);
    }

    private static void SetDashPattern(StringBuilder sb, string? style, float width)
    {
        if (style is "dashed")
        {
            float dashLen = Math.Max(1, width * 3f);
            float gapLen = Math.Max(1, width * 2f);
            sb.AppendLine($"[{dashLen:F2} {gapLen:F2}] 0 d");
        }
        else if (style is "dotted")
        {
            float dotLen = Math.Max(0.5f, width);
            float gapLen = Math.Max(0.5f, width * 2f);
            sb.AppendLine($"[{dotLen:F2} {gapLen:F2}] 0 d");
        }
        else if (style is "double")
        {
            // Double border not directly supported via dash pattern; draw two lines
            sb.AppendLine("[] 0 d");
        }
    }

    private static void ResetDashPattern(StringBuilder sb, string? style)
    {
        if (style is "dashed" or "dotted")
            sb.AppendLine("[] 0 d");
    }

    private static void FillCorner(StringBuilder sb, string? colorH, string? colorV,
        float x, float y, float w, float h, ColorParser colorParser)
    {
        // For diagonal corners, fill with the more visible color
        ParseColorValue(colorH ?? colorV, colorParser, out var r, out var g, out var b, out var a);
        if (a == 0) return;
        sb.AppendLine($"{r:F2} {g:F2} {b:F2} rg");
        sb.AppendLine($"{x:F2} {y:F2} {w:F2} {h:F2} re f");
    }

    private static void ParseColorValue(string? val, ColorParser parser,
        out float r, out float g, out float b, out float a)
    {
        r = g = b = 0; a = 0;
        if (string.IsNullOrWhiteSpace(val) || val == "transparent") return;
        if (!parser.TryParse(val, out var docColor) || docColor is not RgbColor rgb) return;
        a = rgb.A / 255f;
        if (a == 0) return;
        r = rgb.R / 255f; g = rgb.G / 255f; b = rgb.B / 255f;
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
