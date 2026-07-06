using System.Text.RegularExpressions;
using PdfEr.Core.Domain.Styles;

namespace PdfEr.Core.Application.HtmlProcessing;

public sealed class CssNormalizer
{
    private static readonly Regex NumberWithUnit = new(@"^([+-]?\d*\.?\d+)\s*(mm|cm|in|pt|pc|px|em|rem|ex|%|vh|vw)?$", RegexOptions.Compiled);
    private static readonly Regex UrlPattern = new(@"url\([""']?([^""'\)]+)[""']?\)", RegexOptions.Compiled);

    public CssDeclarationBlock ExpandShorthands(CssDeclarationBlock declarations)
    {
        var result = declarations.Clone();

        ExpandMarginPadding(result, "margin");
        ExpandMarginPadding(result, "padding");
        ExpandBorder(result);
        ExpandFont(result);
        ExpandBackground(result);
        ExpandListStyle(result);
        ExpandTextDecoration(result);

        return result;
    }

    private static void ExpandMarginPadding(CssDeclarationBlock block, string property)
    {
        var shorthand = block.GetPropertyValue(property);
        if (shorthand == null) return;

        var parts = shorthand.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            block.SetProperty($"{property}-top", parts[0]);
            block.SetProperty($"{property}-right", parts[0]);
            block.SetProperty($"{property}-bottom", parts[0]);
            block.SetProperty($"{property}-left", parts[0]);
        }
        else if (parts.Length == 2)
        {
            block.SetProperty($"{property}-top", parts[0]);
            block.SetProperty($"{property}-bottom", parts[0]);
            block.SetProperty($"{property}-left", parts[1]);
            block.SetProperty($"{property}-right", parts[1]);
        }
        else if (parts.Length == 3)
        {
            block.SetProperty($"{property}-top", parts[0]);
            block.SetProperty($"{property}-right", parts[1]);
            block.SetProperty($"{property}-left", parts[1]);
            block.SetProperty($"{property}-bottom", parts[2]);
        }
        else if (parts.Length >= 4)
        {
            block.SetProperty($"{property}-top", parts[0]);
            block.SetProperty($"{property}-right", parts[1]);
            block.SetProperty($"{property}-bottom", parts[2]);
            block.SetProperty($"{property}-left", parts[3]);
        }

        block.RemoveProperty(property);
    }

    private void ExpandBorder(CssDeclarationBlock block)
    {
        var border = block.GetPropertyValue("border");
        if (border != null)
        {
            var parts = border.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                if (IsBorderWidth(p)) { SetAllBorders(block, "border-width", p); }
                else if (IsBorderStyle(p)) { SetAllBorders(block, "border-style", p); }
                else { SetAllBorders(block, "border-color", p); }
            }
        }

        block.RemoveProperty("border");

        foreach (var side in new[] { "top", "right", "bottom", "left" })
        {
            var sideBorder = block.GetPropertyValue($"border-{side}");
            if (sideBorder == null) continue;
            var parts = sideBorder.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                if (IsBorderWidth(p)) block.SetProperty($"border-{side}-width", p);
                else if (IsBorderStyle(p)) block.SetProperty($"border-{side}-style", p);
                else block.SetProperty($"border-{side}-color", p);
            }
            block.RemoveProperty($"border-{side}");
        }
    }

    private static void SetAllBorders(CssDeclarationBlock block, string prop, string val)
    {
        block.SetProperty($"border-top-{prop.Split('-')[1]}", val);
        block.SetProperty($"border-right-{prop.Split('-')[1]}", val);
        block.SetProperty($"border-bottom-{prop.Split('-')[1]}", val);
        block.SetProperty($"border-left-{prop.Split('-')[1]}", val);
    }

    private static bool IsBorderWidth(string v) => NumberWithUnit.IsMatch(v) || v is "thin" or "medium" or "thick";
    private static bool IsBorderStyle(string v) => v is "none" or "solid" or "dashed" or "dotted" or "double" or "groove" or "ridge" or "inset" or "outset";

    private void ExpandFont(CssDeclarationBlock block)
    {
        var font = block.GetPropertyValue("font");
        if (font == null) return;

        var parts = font.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        var sizeIdx = -1;
        for (int i = 0; i < parts.Length; i++)
        {
            if (NumberWithUnit.IsMatch(parts[i]) || IsFontSizeKeyword(parts[i]))
            {
                sizeIdx = i;
                break;
            }
        }

        if (sizeIdx >= 0)
        {
            if (sizeIdx + 1 < parts.Length && parts[sizeIdx + 1].StartsWith('/'))
            {
                var lineHeight = parts[sizeIdx + 1].TrimStart('/');
                block.SetProperty("line-height", lineHeight);
                block.SetProperty("font-size", parts[sizeIdx]);
                var familyStart = sizeIdx + 2;
                if (familyStart < parts.Length)
                    block.SetProperty("font-family", string.Join(" ", parts.Skip(familyStart)));
            }
            else
            {
                block.SetProperty("font-size", parts[sizeIdx]);
                if (sizeIdx + 1 < parts.Length)
                    block.SetProperty("font-family", string.Join(" ", parts.Skip(sizeIdx + 1)));
            }

            for (int i = 0; i < sizeIdx; i++)
            {
                if (parts[i] is "normal" or "italic" or "oblique") block.SetProperty("font-style", parts[i]);
                else if (parts[i] is "normal" or "bold" or "bolder" or "lighter") block.SetProperty("font-weight", parts[i]);
                else if (parts[i] is "normal" or "small-caps") block.SetProperty("font-variant", parts[i]);
            }
        }

        block.RemoveProperty("font");
    }

    private static bool IsFontSizeKeyword(string v) => v is "xx-small" or "x-small" or "small" or "medium" or "large" or "x-large" or "xx-large" or "smaller" or "larger";

    private void ExpandBackground(CssDeclarationBlock block)
    {
        var bg = block.GetPropertyValue("background");
        if (bg == null) return;

        var parts = bg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (UrlPattern.IsMatch(p)) block.SetProperty("background-image", p);
            else if (p is "none" or "transparent") block.SetProperty("background-color", p);
            else if (p is "repeat" or "repeat-x" or "repeat-y" or "no-repeat") block.SetProperty("background-repeat", p);
            else if (p is "scroll" or "fixed") block.SetProperty("background-attachment", p);
            else if (p is "left" or "center" or "right" or "top" or "bottom" || NumberWithUnit.IsMatch(p)) block.SetProperty("background-position", p);
        }

        block.RemoveProperty("background");
    }

    private void ExpandListStyle(CssDeclarationBlock block)
    {
        var ls = block.GetPropertyValue("list-style");
        if (ls == null) return;

        var parts = ls.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (UrlPattern.IsMatch(p)) block.SetProperty("list-style-image", p);
            else if (p is "inside" or "outside") block.SetProperty("list-style-position", p);
            else block.SetProperty("list-style-type", p);
        }

        block.RemoveProperty("list-style");
    }

    private static void ExpandTextDecoration(CssDeclarationBlock block)
    {
        var td = block.GetPropertyValue("text-decoration");
        if (td == null) return;
        block.SetProperty("text-decoration-line", td);
        block.RemoveProperty("text-decoration");
    }
}
