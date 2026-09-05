using System.Text.RegularExpressions;

namespace GestaoPredio.Domain.Professionals;

public static class WhatsAppNormalizer
{
    private static readonly Regex BrazilianFormattedNumber = new(
        "^(?:\\(\\d{2}\\)|\\d{2})[ .-]?\\d{4,5}[ .-]?\\d{4}$",
        RegexOptions.CultureInvariant);

    public static bool TryNormalize(string? input, out string canonical)
    {
        canonical = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var value = input.Trim();

        if (value[0] == '+')
        {
            if (IsE164(value))
            {
                canonical = value;
                return true;
            }

            if (!value.StartsWith("+55", StringComparison.Ordinal)
                || !TryNormalizeBrazilianNumber(value[3..].Trim(), out var nationalNumber))
            {
                return false;
            }

            canonical = $"+55{nationalNumber}";
            return true;
        }

        if (!TryNormalizeBrazilianNumber(value, out var national))
        {
            return false;
        }

        canonical = $"+55{national}";
        return true;
    }

    private static bool IsE164(string value)
    {
        if (value.Length is < 3 or > 16 || value[1] == '0')
        {
            return false;
        }

        foreach (var character in value.AsSpan(1))
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryNormalizeBrazilianNumber(string value, out string national)
    {
        national = string.Empty;

        var digitsOnly = true;
        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                digitsOnly = false;
                break;
            }
        }

        if (!digitsOnly && !BrazilianFormattedNumber.IsMatch(value))
        {
            return false;
        }

        Span<char> digits = stackalloc char[11];
        var digitsLength = 0;

        foreach (var character in value)
        {
            if (character is >= '0' and <= '9')
            {
                if (digitsLength == digits.Length)
                {
                    return false;
                }

                digits[digitsLength++] = character;
            }
        }

        if (digitsLength is not (10 or 11))
        {
            return false;
        }

        national = new string(digits[..digitsLength]);
        return true;
    }
}
