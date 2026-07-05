using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Layout;
using PdfEr.Core.Domain.Tables;
using AngleSharp.Dom;

namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class ColgroupHandler : ITagHandler
{
    public string[] HandledTags => new[] { "colgroup", "col" };

    public void Open(TagContext context)
    {
        var tableDef = context.TableDef?.Current;
        if (tableDef == null) return;

        if (context.TagName == "colgroup" || context.TagName == "col")
        {
            string? width = null;
            if (context.Attributes.TryGetValue("width", out var w))
                width = w;
            else if (context.ComputedStyle != null)
                width = context.ComputedStyle.GetPropertyValue("width");

            int span = 1;
            if (context.Attributes.TryGetValue("span", out var s) && int.TryParse(s, out var sp))
                span = sp;

            if (width != null)
            {
                float wMm = 0;
                if (width.EndsWith("px")) float.TryParse(width[..^2], out wMm);
                else if (width.EndsWith("pt")) { float.TryParse(width[..^2], out var pt); wMm = pt * 0.3528f; }
                else if (width.EndsWith("mm")) float.TryParse(width[..^2], out wMm);
                else if (width.EndsWith("%")) { float.TryParse(width[..^1], out var pct); wMm = pct; } // percentage

                for (int i = 0; i < span; i++)
                    tableDef.FixedColWidths.Add(wMm > 0 ? wMm : 0);
            }
            else
            {
                for (int i = 0; i < span; i++)
                    tableDef.FixedColWidths.Add(0);
            }
        }

        context.CurrentBlock = new BlockBox { TagName = context.TagName };
    }

    public void Close(TagContext context)
    {
    }
}
