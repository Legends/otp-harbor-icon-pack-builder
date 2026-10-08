using System.Globalization;
using System.Text;

namespace OtpHarbor.IconPackBuilder.Normalization;

public static class CanonicalId
{
    public static string FromName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var decomposed = name.Normalize(NormalizationForm.FormD).ToLowerInvariant();
        var result = new StringBuilder();
        var separator = false;

        foreach (var rune in decomposed.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;
            if (Rune.IsLetterOrDigit(rune))
            {
                if (separator && result.Length > 0)
                    result.Append('-');
                result.Append(rune);
                separator = false;
            }
            else if (rune.Value == '+')
            {
                if (result.Length > 0 && result[^1] != '-') result.Append('-');
                result.Append("plus");
                separator = true;
            }
            else
            {
                separator = true;
            }
        }

        var id = result.ToString();
        if (id.Length == 0)
            throw new InputValidationException($"Cannot generate a canonical ID from '{name}'.");
        return id;
    }
}
