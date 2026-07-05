using PdfEr.Core.Domain.Barcodes;

namespace PdfEr.Core.Application.Services;

public sealed class BarcodeEncoder
{
    public BarcodeRenderResult Encode(BarcodeDefinition definition)
    {
        return definition.Type switch
        {
            BarcodeType.Code128 => EncodeCode128(definition),
            BarcodeType.Ean13 => EncodeEan13(definition),
            BarcodeType.Ean8 => EncodeEan8(definition),
            BarcodeType.UpcA => EncodeUpcA(definition),
            BarcodeType.UpcE => EncodeUpcE(definition),
            BarcodeType.Code39 => EncodeCode39(definition),
            BarcodeType.QrCode => EncodeQrCode(definition),
            BarcodeType.DataMatrix => EncodeDataMatrix(definition),
            BarcodeType.Pdf417 => EncodePdf417(definition),
            _ => throw new ArgumentOutOfRangeException(nameof(definition.Type))
        };
    }

    public BarcodeRenderResult EncodeCode128(BarcodeDefinition def)
    {
        var result = new BarcodeRenderResult
        {
            EncodedData = def.Value,
            WidthMm = def.HeightMm * 3,
            HeightMm = def.HeightMm
        };

        char start = 'Ì';
        char stop = 'Î';
        char check = (char)104;
        string full = start + def.Value + check + stop;

        foreach (char c in full)
        {
            int code = c switch
            {
                >= ' ' and <= '?' => c - ' ',
                >= '@' and <= '~' => c - '@' + 32 + 64,
                _ => 0
            };
            result.Bars.Add(new BarcodeBar { Width = def.NarrowBarWidthMm * (code % 2 == 0 ? 2 : 1), Height = def.HeightMm, IsBar = true });
            result.Bars.Add(new BarcodeBar { Width = def.NarrowBarWidthMm, Height = def.HeightMm, IsBar = false });
        }

        return result;
    }

    public BarcodeRenderResult EncodeEan13(BarcodeDefinition def)
    {
        string value = def.Value.PadLeft(13, '0');
        if (value.Length > 13) value = value[..13];

        var result = new BarcodeRenderResult
        {
            EncodedData = value,
            WidthMm = def.HeightMm * 2.2f,
            HeightMm = def.HeightMm
        };

        float x = 0;
        result.Bars.Add(new BarcodeBar { X = x, Width = def.NarrowBarWidthMm, Height = def.HeightMm, IsBar = true });
        x += def.NarrowBarWidthMm * 2;
        result.Bars.Add(new BarcodeBar { X = x, Width = def.NarrowBarWidthMm, Height = def.HeightMm, IsBar = false });

        for (int i = 0; i < value.Length; i++)
        {
            int digit = value[i] - '0';
            string pattern = EanParityPatterns[digit];
            foreach (char p in pattern)
            {
                bool isBar = p == '1';
                float w = def.NarrowBarWidthMm * (isBar ? 1 : 1);
                result.Bars.Add(new BarcodeBar { X = x, Width = w, Height = def.HeightMm, IsBar = isBar });
                x += w;
            }
            if (i == 6)
            {
                x += def.NarrowBarWidthMm * 2;
                result.Bars.Add(new BarcodeBar { X = x, Width = def.NarrowBarWidthMm, Height = def.HeightMm, IsBar = true });
                x += def.NarrowBarWidthMm * 2;
            }
        }

        result.WidthMm = x;
        return result;
    }

    public BarcodeRenderResult EncodeEan8(BarcodeDefinition def)
    {
        string value = def.Value.PadLeft(8, '0');
        if (value.Length > 8) value = value[..8];
        return EncodeEan13(new BarcodeDefinition
        {
            Type = BarcodeType.Ean13,
            Value = value.PadRight(13, '0'),
            HeightMm = def.HeightMm,
            NarrowBarWidthMm = def.NarrowBarWidthMm,
            ShowText = def.ShowText
        });
    }

    public BarcodeRenderResult EncodeUpcA(BarcodeDefinition def)
    {
        string value = def.Value.PadLeft(12, '0');
        string ean13Value = "0" + value;
        return EncodeEan13(new BarcodeDefinition
        {
            Type = BarcodeType.Ean13,
            Value = ean13Value,
            HeightMm = def.HeightMm,
            NarrowBarWidthMm = def.NarrowBarWidthMm,
            ShowText = def.ShowText
        });
    }

    public BarcodeRenderResult EncodeUpcE(BarcodeDefinition def)
    {
        string value = def.Value.PadLeft(8, '0');
        string upcA = UpcEtoUpcA(value);
        return EncodeUpcA(new BarcodeDefinition
        {
            Type = BarcodeType.UpcA,
            Value = upcA,
            HeightMm = def.HeightMm,
            NarrowBarWidthMm = def.NarrowBarWidthMm,
            ShowText = def.ShowText
        });
    }

    public BarcodeRenderResult EncodeCode39(BarcodeDefinition def)
    {
        var result = new BarcodeRenderResult
        {
            EncodedData = def.Value,
            HeightMm = def.HeightMm
        };

        float x = 0;
        string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%*";
        foreach (char c in def.Value.ToUpperInvariant())
        {
            int idx = chars.IndexOf(c);
            if (idx < 0) continue;

            string pattern = Code39Patterns[idx];
            foreach (char p in pattern)
            {
                bool isBar = p == '1';
                float w = def.NarrowBarWidthMm * (isBar ? (p == '1' ? 1 : 1) : 1);
                result.Bars.Add(new BarcodeBar { X = x, Width = w, Height = def.HeightMm, IsBar = isBar });
                x += w;
            }
            x += def.NarrowBarWidthMm;
        }

        result.WidthMm = x;
        return result;
    }

    public BarcodeRenderResult EncodeQrCode(BarcodeDefinition def)
    {
        var result = new BarcodeRenderResult
        {
            EncodedData = def.Value,
            WidthMm = def.HeightMm,
            HeightMm = def.HeightMm
        };

        int moduleCount = 21;
        float moduleMm = def.HeightMm / moduleCount;
        for (int y = 0; y < moduleCount; y++)
        {
            for (int x = 0; x < moduleCount; x++)
            {
                bool filled = ((x + y) % 2 == 0);
                result.Bars.Add(new BarcodeBar
                {
                    X = x * moduleMm,
                    Y = y * moduleMm,
                    Width = moduleMm,
                    Height = moduleMm,
                    IsBar = filled
                });
            }
        }

        return result;
    }

    public BarcodeRenderResult EncodeDataMatrix(BarcodeDefinition def) => new() { EncodedData = def.Value, WidthMm = def.HeightMm, HeightMm = def.HeightMm };

    public BarcodeRenderResult EncodePdf417(BarcodeDefinition def) => new() { EncodedData = def.Value, WidthMm = def.HeightMm * 3, HeightMm = def.HeightMm };

    private static string UpcEtoUpcA(string upcE)
    {
        char last = upcE[^1];
        string middle = upcE[1..7];
        return last switch
        {
            '0' or '1' or '2' => upcE[..2] + middle + "0000" + last,
            '3' => upcE[..3] + "00000" + last,
            '4' => upcE[..4] + "00000" + last,
            _ => upcE[..6] + "0000" + last,
        };
    }

    private static readonly string[] EanParityPatterns = [
        "0011001", "1100110", "1101100", "1000010", "1011100",
        "1001110", "1010000", "1000100", "1001000", "1110100"
    ];

    private static readonly string[] Code39Patterns = [
        "000010100011101", "100100001110001", "010000101110001", "110000001011001",  // 0,1,2,3
        "001000101110001", "101000001011001", "011000001001101", "000010101011001",  // 4,5,6,7
        "100010001001101", "010010001001101", "000100101110001", "100011001001001",  // 8,9,A,B
        "010011001001001", "110001001001001", "001011001001001", "101001001001001",  // C,D,E,F
        "011001001001001", "000101001001001", "100101001001001", "010101001001001",  // G,H,I,J
        "001101001001001", "100110001001001", "010110001001001", "001110001001001",  // K,L,M,N
        "100100011001001", "010100011001001", "001100011001001", "100100001011001",  // O,P,Q,R
        "010100001011001", "001100001011001", "000100101011001", "100100101001001",  // S,T,U,V
        "010100101001001", "001100101001001", "100100100011001", "010100100011001",  // W,X,Y,Z
        "001100100011001", "000100100011101", "001001001001101", "000001010011101",  // -,., ,$
        "000001001011101", "000001001001101", "000010100011001", "100010001011001",  // /,+,,%*
    ];
}
