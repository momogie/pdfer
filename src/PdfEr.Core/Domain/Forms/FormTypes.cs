namespace PdfEr.Core.Domain.Forms;

public enum FormFieldType
{
    Text,
    CheckBox,
    RadioButton,
    ComboBox,
    ListBox,
    PushButton,
    Signature
}

public sealed class FormDefinition
{
    public List<FormField> Fields { get; } = new();
    public bool NeedAppearances { get; set; }
}

public sealed class FormField
{
    public string Name { get; set; } = string.Empty;
    public FormFieldType Type { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public string? DefaultValue { get; set; }
    public string? Value { get; set; }
    public bool ReadOnly { get; set; }
    public bool Required { get; set; }
    public int MaxLength { get; set; } = -1;
    public string? FontName { get; set; }
    public float FontSize { get; set; } = 12f;
    public string? MappingName { get; set; }
    public string? AlternateName { get; set; }
    public List<string>? Options { get; set; }
    public bool IsChecked { get; set; }
    public float BorderWidth { get; set; } = 1f;
}

public sealed class FormFieldResult
{
    public int ObjectNumber { get; set; }
    public int AnnotObjectNumber { get; set; }
}

public sealed class FormRenderResult
{
    public List<FormFieldResult> FieldResults { get; } = new();
    public int AcroFormObjectNumber { get; set; }
    public string? FormContent { get; set; }
}
