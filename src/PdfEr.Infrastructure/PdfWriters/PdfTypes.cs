namespace PdfEr.Infrastructure.PdfWriters;

public sealed class PdfEncryptionOptions
{
    public bool Encrypt { get; set; }
    public string? UserPassword { get; set; }
    public string? OwnerPassword { get; set; }
    public EncryptionLevel Level { get; set; } = EncryptionLevel.Aes128;
    public PdfPermissions Permissions { get; set; } = PdfPermissions.FullAccess;
}

public enum EncryptionLevel { Aes40, Aes128, Aes256 }

[Flags]
public enum PdfPermissions
{
    FullAccess = 0,
    NoPrint = 1,
    NoModify = 2,
    NoCopy = 4,
    NoAnnotate = 8,
    NoFillForms = 16,
    NoAccessibility = 32,
    NoAssemble = 64,
    NoPrintHighQuality = 128
}

public sealed class PdfMetadata
{
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Keywords { get; set; } = string.Empty;
    public string Creator { get; set; } = "PdfEr";
    public string Producer { get; set; } = "PdfEr";
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime Modified { get; set; } = DateTime.UtcNow;
}
