namespace PdfEr.Core.Domain.Enums;

[Flags]
public enum PdfPermissions
{
    Print = 1,
    ModifyContent = 2,
    CopyContent = 4,
    ModifyAnnotations = 8,
    FillForms = 16,
    ExtractAccessibility = 32,
    AssembleDocument = 64,
    PrintHighQuality = 128,
    All = 255
}
