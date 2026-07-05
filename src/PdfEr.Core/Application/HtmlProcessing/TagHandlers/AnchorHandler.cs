using PdfEr.Core.Domain.Layout;

namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class AnchorHandler : InlineTagHandler
{
    public override string[] HandledTags => new[] { "a" };

    public override void Open(TagContext context)
    {
        if (context.Attributes.TryGetValue("href", out var href) && !string.IsNullOrWhiteSpace(href))
        {
            var box = new BlockBox
            {
                TagName = "a",
                ComputedStyle = context.ComputedStyle,
                LinkUrl = href,
                X = context.LayoutEngine.CurrentPage.ContentBox.X,
                Y = context.LayoutEngine.CurrentY,
                Width = context.LayoutEngine.CurrentPage.ContentBox.Width,
                Height = 0
            };
            context.CurrentBlock = box;
        }

        if (context.Attributes.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name))
        {
            var box = new BlockBox
            {
                TagName = "a",
                ComputedStyle = context.ComputedStyle,
                AnchorName = name,
                X = context.LayoutEngine.CurrentPage.ContentBox.X,
                Y = context.LayoutEngine.CurrentY,
                Width = 1,
                Height = 1
            };
            context.CurrentBlock = box;
        }
    }
}
