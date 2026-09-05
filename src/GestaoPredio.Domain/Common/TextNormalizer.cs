using System.Globalization;
using System.Text;

namespace GestaoPredio.Domain.Common;

public static class TextNormalizer
{
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var collapsedWhitespace = new StringBuilder(value.Length);
        var previousWasWhitespace = true;

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                previousWasWhitespace = true;
                continue;
            }

            if (collapsedWhitespace.Length > 0 && previousWasWhitespace)
            {
                collapsedWhitespace.Append(' ');
            }

            collapsedWhitespace.Append(character);
            previousWasWhitespace = false;
        }

        var decomposed = collapsedWhitespace.ToString().Normalize(NormalizationForm.FormD);
        var withoutDiacritics = new StringBuilder(decomposed.Length);

        foreach (var rune in decomposed.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.NonSpacingMark)
            {
                withoutDiacritics.Append(rune);
            }
        }

        return withoutDiacritics
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .ToUpperInvariant()
            .Replace("ß", "SS", StringComparison.Ordinal);
    }
}
