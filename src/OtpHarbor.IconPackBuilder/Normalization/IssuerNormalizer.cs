using System.Globalization;
using System.Text;

namespace OtpHarbor.IconPackBuilder.Normalization;

public static class IssuerNormalizer
{
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var result = new StringBuilder(normalized.Length);
        var pendingSeparator = false;

        foreach (var rune in normalized.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber)
            {
                if (pendingSeparator && result.Length > 0)
                    result.Append(' ');
                result.Append(rune);
                pendingSeparator = false;
            }
            else if (rune.Value == '&')
            {
                if (pendingSeparator && result.Length > 0)
                    result.Append(' ');
                result.Append(rune);
                pendingSeparator = false;
            }
            else if (Rune.IsWhiteSpace(rune) || IsSafeSeparator(rune)
                || category is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
                    or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation
                    or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation
                    or UnicodeCategory.OtherPunctuation)
            {
                pendingSeparator = true;
            }
            else
            {
                // Symbols such as '+' and '&' carry identity and are retained.
                if (category is UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol
                    or UnicodeCategory.OtherSymbol)
                {
                    if (pendingSeparator && result.Length > 0)
                        result.Append(' ');
                    result.Append(rune);
                    pendingSeparator = false;
                }
            }
        }

        return result.ToString();
    }

    private static bool IsSafeSeparator(Rune rune) => rune.Value is '-' or '_' or '.' or '/' or '\\' or ':';
}
