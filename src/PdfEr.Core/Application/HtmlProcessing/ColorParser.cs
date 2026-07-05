using System.Globalization;
using System.Text.RegularExpressions;
using PdfEr.Core.Domain.ValueObjects;

namespace PdfEr.Core.Application.HtmlProcessing;

public sealed class ColorParser
{
    private static readonly Regex HexColor = new(@"^#?([0-9a-fA-F]{3,8})$", RegexOptions.Compiled);
    private static readonly Regex RgbColor = new(@"rgba?\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+))?\s*\)", RegexOptions.Compiled);
    private static readonly Regex HslColor = new(@"hsla?\s*\(\s*([\d.]+)\s*,\s*([\d.]+)%\s*,\s*([\d.]+)%\s*(?:,\s*([\d.]+))?\s*\)", RegexOptions.Compiled);
    private static readonly Regex CmykColor = new(@"cmyka?\s*\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*(?:,\s*([\d.]+))?\s*\)", RegexOptions.Compiled);

    private static readonly Dictionary<string, RgbColor> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = new(0, 0, 0), ["white"] = new(255, 255, 255), ["red"] = new(255, 0, 0),
        ["green"] = new(0, 128, 0), ["blue"] = new(0, 0, 255), ["yellow"] = new(255, 255, 0),
        ["orange"] = new(255, 165, 0), ["purple"] = new(128, 0, 128), ["gray"] = new(128, 128, 128),
        ["grey"] = new(128, 128, 128), ["silver"] = new(192, 192, 192), ["maroon"] = new(128, 0, 0),
        ["navy"] = new(0, 0, 128), ["olive"] = new(128, 128, 0), ["lime"] = new(0, 255, 0),
        ["aqua"] = new(0, 255, 255), ["teal"] = new(0, 128, 128), ["fuchsia"] = new(255, 0, 255),
        ["transparent"] = new(0, 0, 0, 0),
    };

    public DocumentColor Parse(string colorString)
    {
        if (TryParse(colorString, out var color) && color != null)
            return color;
        return new RgbColor(0, 0, 0);
    }

    public bool TryParse(string colorString, out DocumentColor? color)
    {
        color = null;
        if (string.IsNullOrWhiteSpace(colorString)) return false;

        var val = colorString.Trim();

        if (NamedColors.TryGetValue(val, out var named))
        {
            color = named;
            return true;
        }

        var hexMatch = HexColor.Match(val);
        if (hexMatch.Success)
        {
            var hex = hexMatch.Groups[1].Value;
            color = ParseHex(hex);
            return true;
        }

        var rgbMatch = RgbColor.Match(val);
        if (rgbMatch.Success)
        {
            byte r = byte.Parse(rgbMatch.Groups[1].Value);
            byte g = byte.Parse(rgbMatch.Groups[2].Value);
            byte b = byte.Parse(rgbMatch.Groups[3].Value);
            byte a = 255;
            if (rgbMatch.Groups[4].Success)
                a = (byte)(float.Parse(rgbMatch.Groups[4].Value, CultureInfo.InvariantCulture) * 255);
            color = new RgbColor(r, g, b, a);
            return true;
        }

        var hslMatch = HslColor.Match(val);
        if (hslMatch.Success)
        {
            var h = float.Parse(hslMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var s = float.Parse(hslMatch.Groups[2].Value, CultureInfo.InvariantCulture) / 100f;
            var l = float.Parse(hslMatch.Groups[3].Value, CultureInfo.InvariantCulture) / 100f;
            color = HslToRgb(h, s, l);
            return true;
        }

        var cmykMatch = CmykColor.Match(val);
        if (cmykMatch.Success)
        {
            var c = float.Parse(cmykMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var m = float.Parse(cmykMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            var y = float.Parse(cmykMatch.Groups[3].Value, CultureInfo.InvariantCulture);
            var k = float.Parse(cmykMatch.Groups[4].Value, CultureInfo.InvariantCulture);
            color = new CmykColor(c, m, y, k);
            return true;
        }

        return false;
    }

    private static RgbColor ParseHex(string hex)
    {
        if (hex.Length == 3)
        {
            var r = Convert.ToByte(hex[..1] + hex[..1], 16);
            var g = Convert.ToByte(hex[1..2] + hex[1..2], 16);
            var b = Convert.ToByte(hex[2..3] + hex[2..3], 16);
            return new RgbColor(r, g, b);
        }
        if (hex.Length == 6)
        {
            var r = Convert.ToByte(hex[..2], 16);
            var g = Convert.ToByte(hex[2..4], 16);
            var b = Convert.ToByte(hex[4..6], 16);
            return new RgbColor(r, g, b);
        }
        if (hex.Length == 8)
        {
            var r = Convert.ToByte(hex[..2], 16);
            var g = Convert.ToByte(hex[2..4], 16);
            var b = Convert.ToByte(hex[4..6], 16);
            var a = Convert.ToByte(hex[6..8], 16);
            return new RgbColor(r, g, b, a);
        }
        return new RgbColor(0, 0, 0);
    }

    private static RgbColor HslToRgb(float h, float s, float l)
    {
        h /= 360f;
        float r, g, b;
        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            float q = l < 0.5f ? l * (1 + s) : l + s - l * s;
            float p = 2 * l - q;
            r = HueToRgb(p, q, h + 1f / 3f);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1f / 3f);
        }
        return new RgbColor((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private static float HueToRgb(float p, float q, float t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1f / 6f) return p + (q - p) * 6 * t;
        if (t < 1f / 2f) return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6;
        return p;
    }
}
