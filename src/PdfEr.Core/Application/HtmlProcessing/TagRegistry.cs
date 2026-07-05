using PdfEr.Core.Application.Interfaces;

namespace PdfEr.Core.Application.HtmlProcessing;

public sealed class TagRegistry
{
    private readonly Dictionary<string, ITagHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);

    public TagRegistry(IEnumerable<ITagHandler> handlers)
    {
        foreach (var handler in handlers)
        {
            foreach (var tag in handler.HandledTags)
            {
                _handlers[tag] = handler;
            }
        }
    }

    public ITagHandler? GetHandler(string tagName)
    {
        _handlers.TryGetValue(tagName, out var handler);
        return handler;
    }

    public bool HasHandler(string tagName) => _handlers.ContainsKey(tagName);

    public void OpenTag(string tagName, TagContext context)
    {
        if (_handlers.TryGetValue(tagName, out var handler))
            handler.Open(context);
    }

    public void CloseTag(string tagName, TagContext context)
    {
        if (_handlers.TryGetValue(tagName, out var handler))
            handler.Close(context);
    }

    public IReadOnlyCollection<string> RegisteredTags => _handlers.Keys;
}
