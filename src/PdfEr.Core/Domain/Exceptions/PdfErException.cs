namespace PdfEr.Core.Domain.Exceptions;

public abstract class PdfErException : Exception
{
    protected PdfErException(string message) : base(message) { }
    protected PdfErException(string message, Exception inner) : base(message, inner) { }
}

public sealed class ConfigurationException : PdfErException
{
    public ConfigurationException(string message) : base(message) { }
    public ConfigurationException(string message, Exception inner) : base(message, inner) { }
}

public sealed class FontException : PdfErException
{
    public FontException(string message) : base(message) { }
    public FontException(string message, Exception inner) : base(message, inner) { }
}

public sealed class ImageException : PdfErException
{
    public ImageException(string message) : base(message) { }
    public ImageException(string message, Exception inner) : base(message, inner) { }
}

public sealed class AssetFetchException : PdfErException
{
    public AssetFetchException(string message) : base(message) { }
    public AssetFetchException(string message, Exception inner) : base(message, inner) { }
}

public sealed class PdfSecurityException : PdfErException
{
    public PdfSecurityException(string message) : base(message) { }
    public PdfSecurityException(string message, Exception inner) : base(message, inner) { }
}

public sealed class HtmlParseException : PdfErException
{
    public HtmlParseException(string message) : base(message) { }
    public HtmlParseException(string message, Exception inner) : base(message, inner) { }
}

public sealed class InvalidPageFormatException : PdfErException
{
    public InvalidPageFormatException(string message) : base(message) { }
    public InvalidPageFormatException(string message, Exception inner) : base(message, inner) { }
}
