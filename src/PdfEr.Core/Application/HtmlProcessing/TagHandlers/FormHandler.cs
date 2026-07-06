using PdfEr.Core.Application.Interfaces;
using PdfEr.Core.Domain.Forms;
using PdfEr.Core.Domain.Layout;

namespace PdfEr.Core.Application.HtmlProcessing.TagHandlers;

public sealed class FormHandler : ITagHandler
{
    public string[] HandledTags => new[] { "form" };

    public void Open(TagContext context)
    {
        var box = context.LayoutEngine.CreateBlock(
            context.TagName, context.Attributes, context.ParentStyle, context.Element);
        context.CurrentBlock = box;
    }

    public void Close(TagContext context)
    {
    }
}

public sealed class InputHandler : ITagHandler
{
    public string[] HandledTags => new[] { "input" };

    public void Open(TagContext context)
    {
        var box = new BlockBox
        {
            TagName = "input",
            ComputedStyle = context.ComputedStyle,
            X = context.LayoutEngine.CurrentPage.ContentBox.X,
            Y = context.LayoutEngine.CurrentY,
            Width = 40,
            Height = 6
        };

        var inputType = context.Attributes.GetValueOrDefault("type", "text").ToLowerInvariant();
        var fieldName = context.Attributes.GetValueOrDefault("name", "");
        var fieldValue = context.Attributes.GetValueOrDefault("value", "");

        if (!string.IsNullOrWhiteSpace(fieldName))
        {
            var formField = new FormField
            {
                Name = fieldName,
                Type = inputType switch
                {
                    "checkbox" => FormFieldType.CheckBox,
                    "radio" => FormFieldType.RadioButton,
                    "submit" or "button" => FormFieldType.PushButton,
                    "password" => FormFieldType.Text,
                    _ => FormFieldType.Text
                },
                X = box.X,
                Y = box.Y,
                Width = box.Width,
                Height = box.Height,
                DefaultValue = fieldValue,
                Value = fieldValue
            };

            box.TextContent = "";
            var inline = new InlineBox
            {
                Type = InlineBoxType.Text,
                Text = inputType == "checkbox" ? "\u2610" : inputType == "radio" ? "\u25CB" : "",
                X = box.X,
                Y = box.Y,
                Width = box.Width,
                Height = box.Height,
                ComputedStyle = context.ComputedStyle
            };
            box.InlineContent.Add(inline);
        }

        context.CurrentBlock = box;
    }

    public void Close(TagContext context)
    {
    }
}

public sealed class TextareaHandler : ITagHandler
{
    public string[] HandledTags => new[] { "textarea" };

    public void Open(TagContext context)
    {
        var box = new BlockBox
        {
            TagName = "textarea",
            ComputedStyle = context.ComputedStyle,
            X = context.LayoutEngine.CurrentPage.ContentBox.X,
            Y = context.LayoutEngine.CurrentY,
            Width = 80,
            Height = 20
        };
        context.CurrentBlock = box;
    }

    public void Close(TagContext context)
    {
    }
}

public sealed class SelectHandler : ITagHandler
{
    public string[] HandledTags => new[] { "select" };

    public void Open(TagContext context)
    {
        var box = new BlockBox
        {
            TagName = "select",
            ComputedStyle = context.ComputedStyle,
            X = context.LayoutEngine.CurrentPage.ContentBox.X,
            Y = context.LayoutEngine.CurrentY,
            Width = 50,
            Height = 6
        };
        context.CurrentBlock = box;
    }

    public void Close(TagContext context)
    {
    }
}
