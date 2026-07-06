namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class ListHandler : BlockTagHandler
{
    public override string[] HandledTags => new[] { "ul", "ol" };

    public override void Open(TagContext context)
    {
        var isOrdered = string.Equals(context.TagName, "ol", StringComparison.OrdinalIgnoreCase);

        var start = 1;
        if (context.Attributes.TryGetValue("start", out var startStr) && int.TryParse(startStr, out var s))
            start = s;

        var listStyleType = context.ComputedStyle?.GetPropertyValue("list-style-type");
        if (string.IsNullOrWhiteSpace(listStyleType))
            listStyleType = isOrdered ? "decimal" : "disc";

        context.ListStack.Push(new ListState(isOrdered, start, listStyleType));

        var box = context.LayoutEngine.CreateBlock(
            context.TagName, context.Attributes, context.ParentStyle, context.Element);

        var currentPage = context.CurrentPage;
        box.X = currentPage.ContentBox.X;
        box.Y = currentPage.ContentBox.Y;

        context.CurrentBlock = box;
    }

    public override void Close(TagContext context)
    {
        if (context.ListStack.Count > 0)
            context.ListStack.Pop();

        var box = context.CurrentBlock;
        if (box == null) return;

        var currentPage = context.CurrentPage;
        context.LayoutEngine.LayoutBlock(box, context.Config);
        if (!currentPage.Blocks.Contains(box))
            currentPage.Blocks.Add(box);
    }
}
