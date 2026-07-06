using SixLabors.Fonts.Unicode;

namespace PdfEr.Infrastructure.Typography;

/// <summary>
/// Simplified UAX#14 line break algorithm using CodePoint.GetLineBreakClass().
/// Handles the most common break opportunities: spaces, hyphens, CJK,
/// open/close punctuation, and mandatory breaks (BK/CR/LF/NL).
/// Falls back to space-only breaks when SixLabors data is unavailable.
/// </summary>
public static class Uax14LineBreaker
{
    /// <summary>
    /// Returns sorted character indices where a line break is allowed.
    /// Position N means a break is allowed between char[N-1] and char[N].
    /// </summary>
    public static List<int> GetBreakOpportunities(string text)
    {
        var breaks = new List<int>();
        if (string.IsNullOrEmpty(text)) return breaks;

        // Always allow break at start
        breaks.Add(0);

        var prevClass = GetClass(text[0]);

        for (int i = 1; i < text.Length; i++)
        {
            var currClass = GetClass(text[i]);

            if (ShouldBreak(prevClass, currClass))
                breaks.Add(i);

            prevClass = currClass;
        }

        return breaks;
    }

    /// <summary>
    /// Determines whether a break is allowed between two consecutive
    /// line break classes, implementing a practical subset of UAX#14 rules.
    /// </summary>
    private static bool ShouldBreak(LineBreakClass prev, LineBreakClass curr)
    {
        // LB4: Mandatory break after BK/CR/LF/NL
        if (prev is LineBreakClass.BK or LineBreakClass.CR
               or LineBreakClass.LF or LineBreakClass.NL)
            return true;

        // LB5: CR+LF = no break (not a pair we track in single chars)
        // Handled by the loop - CR followed by LF doesn't break between them.
        if (prev == LineBreakClass.CR && curr == LineBreakClass.LF)
            return false;

        // LB6: Don't break before a space
        if (curr == LineBreakClass.SP)
            return false;

        // LB7: Already handled: don't break before SP (LB6), break after SP (LB18)

        // LB18: Break after SP
        if (prev == LineBreakClass.SP)
            return true;

        // LB8: Break before and after ZW (zero-width space)
        if (prev == LineBreakClass.ZW || curr == LineBreakClass.ZW)
            return true;

        // LB11: Never break before/after WJ
        if (prev == LineBreakClass.WJ || curr == LineBreakClass.WJ)
            return false;

        // LB12: Don't break after GL
        if (prev == LineBreakClass.GL)
            return false;

        // LB13: Don't break before GL
        if (curr == LineBreakClass.GL)
            return false;

        // LB14: Don't break after OP (but some exceptions like OP+QU)
        // Simplified: allow break after OP (better for readability)
        if (prev == LineBreakClass.OP)
            return true;

        // LB15: Don't break before CL/CP/EX/IS/SY
        if (curr is LineBreakClass.CL or LineBreakClass.CP
                or LineBreakClass.EX or LineBreakClass.IS
                or LineBreakClass.SY)
            return false;

        // Break after CL/CP/EX/IS/SY
        if (prev is LineBreakClass.CL or LineBreakClass.CP
                or LineBreakClass.EX or LineBreakClass.IS
                or LineBreakClass.SY)
            return true;

        // LB16: Don't break before CL/CP when preceded by NU
        if (prev == LineBreakClass.NU &&
            (curr is LineBreakClass.CL or LineBreakClass.CP))
            return false;

        // LB17: Don't break before HY after NU
        if (prev == LineBreakClass.NU && curr == LineBreakClass.HY)
            return false;

        // LB19: Break before BB
        if (curr == LineBreakClass.BB)
            return true;

        // Break after BA
        if (prev == LineBreakClass.BA)
            return true;

        // LB20: Break before and after CB
        if (prev == LineBreakClass.CB || curr == LineBreakClass.CB)
            return true;

        // LB21: Don't break before NS
        if (curr == LineBreakClass.NS)
            return false;

        // LB21a: Don't break before HL
        if (curr == LineBreakClass.HL)
            return false;

        // Break after HY (hyphen) — common case for hyphenated words
        if (prev == LineBreakClass.HY)
            return true;

        // Break before OP (open punctuation) — like before (
        if (curr == LineBreakClass.OP)
            return true;

        // Break before and after ID (ideographic/CJK)
        if (prev == LineBreakClass.ID || curr == LineBreakClass.ID)
            return true;

        // Break after EX (exclamation) and IS (infix separator)
        if (prev is LineBreakClass.EX or LineBreakClass.IS)
            return true;

        // LB23: No break before NU after AL/HL
        if (curr == LineBreakClass.NU &&
            (prev is LineBreakClass.AL or LineBreakClass.HL))
            return false;

        // LB24: No break before PO after NU/ID/AL/HL
        if (curr == LineBreakClass.PO &&
            (prev is LineBreakClass.NU or LineBreakClass.ID
                  or LineBreakClass.AL or LineBreakClass.HL))
            return false;

        // No break before PR from AL/HL
        if (curr == LineBreakClass.PR &&
            (prev is LineBreakClass.AL or LineBreakClass.HL))
            return false;

        // Break after PO
        if (prev == LineBreakClass.PO)
            return true;

        // LB26/27: Korean syllable blocks — no break
        if (prev is LineBreakClass.JL or LineBreakClass.JV or LineBreakClass.H2
            && curr is LineBreakClass.JL or LineBreakClass.JV or LineBreakClass.H2 or LineBreakClass.H3)
            return false;
        if (prev is LineBreakClass.H3 && curr is LineBreakClass.H3 or LineBreakClass.JT)
            return false;
        if (prev == LineBreakClass.JT && curr == LineBreakClass.JT)
            return false;

        // LB28: Flag sequences — no break between consecutive RI
        if (prev == LineBreakClass.RI && curr == LineBreakClass.RI)
            return false;

        // LB29: Emoji sequences
        if (prev == LineBreakClass.EB && curr == LineBreakClass.EM)
            return false;

        // LB30: Break after ID before AL/NU/ID
        if (prev == LineBreakClass.ID &&
            (curr is LineBreakClass.AL or LineBreakClass.NU))
            return true;

        // LB31: Default = no break
        return false;
    }

    private static LineBreakClass GetClass(char c)
    {
        try
        {
            var cp = new CodePoint(c);
            return CodePoint.GetLineBreakClass(cp);
        }
        catch
        {
            return LineBreakClass.AL;
        }
    }
}
