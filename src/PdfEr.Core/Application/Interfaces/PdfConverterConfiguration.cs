using PdfEr.Core.Domain.Enums;
using PdfEr.Core.Domain.ValueObjects;

namespace PdfEr.Core.Application.Interfaces;

public sealed class PdfConverterConfiguration
{
    public PageFormat PageFormat { get; set; } = PageFormat.A4;
    public PageOrientation Orientation { get; set; } = PageOrientation.Portrait;
    public float MarginTop { get; set; } = 15f;
    public float MarginBottom { get; set; } = 15f;
    public float MarginLeft { get; set; } = 15f;
    public float MarginRight { get; set; } = 15f;
    public float MarginHeader { get; set; } = 5f;
    public float MarginFooter { get; set; } = 5f;
    public string DefaultFontFamily { get; set; } = "DejaVu Sans";
    public float DefaultFontSize { get; set; } = 10f;
    public int Dpi { get; set; } = 96;
    public bool EnableCompression { get; set; } = true;
    public PdfVersion PdfVersion { get; set; } = PdfVersion.V1_7;
    public string[] FontDirectories { get; set; } = Array.Empty<string>();
    public string TempDirectory { get; set; } = "./temp";
    public int CacheCleanupIntervalSeconds { get; set; } = 3600;
    public int HttpTimeoutSeconds { get; set; } = 5;
    public string? HttpUserAgent { get; set; }
    public bool HttpFollowRedirects { get; set; } = true;
    public bool EnableEncryption { get; set; }
    public string? UserPassword { get; set; }
    public string? OwnerPassword { get; set; }
    public EncryptionLevel EncryptionLevel { get; set; } = EncryptionLevel.Aes128Bit;
    public bool DebugMode { get; set; }
    public bool ShowImageErrors { get; set; }

    public string? Title { get; set; }

    public DocumentMargins GetMargins() => new(MarginTop, MarginBottom, MarginLeft, MarginRight, MarginHeader, MarginFooter);
}
