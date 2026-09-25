using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Manoksha.SharedKernel;

/// <summary>
/// Normalizes Indian mobile numbers to E.164 (+91XXXXXXXXXX). Mobile numbers are identifiers/attributes,
/// never primary keys (SPEC §2).
/// </summary>
public static class MobileNumber
{
    public static bool TryNormalize(string? input, [NotNullWhen(true)] out string? e164)
    {
        e164 = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        var hasPlus = trimmed.StartsWith('+');
        var digits = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (char.IsAsciiDigit(ch))
            {
                digits.Append(ch);
            }
            else if (ch is not (' ' or '-' or '(' or ')' or '+'))
            {
                return false;
            }
        }

        var d = digits.ToString();
        if (hasPlus)
        {
            if (!d.StartsWith("91", StringComparison.Ordinal) || d.Length != 12)
            {
                return false;
            }
            d = d[2..];
        }
        else if (d.Length == 12 && d.StartsWith("91", StringComparison.Ordinal))
        {
            d = d[2..];
        }
        else if (d.Length == 11 && d.StartsWith('0'))
        {
            d = d[1..];
        }

        if (d.Length != 10 || d[0] is < '6' or > '9')
        {
            return false;
        }

        e164 = "+91" + d;
        return true;
    }

    public static string Normalize(string? input) =>
        TryNormalize(input, out var e164)
            ? e164
            : throw new BusinessRuleException(ErrorCodes.InvalidMobileNumber, "Enter a valid 10-digit Indian mobile number.", 400);

    /// <summary>Masks for logs: +91******4304.</summary>
    public static string Mask(string e164) => e164.Length > 4 ? $"{e164[..3]}******{e164[^4..]}" : "****";
}

public static class EmailAddress
{
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    public static bool IsValid(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254)
        {
            return false;
        }
        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && at == email.LastIndexOf('@') && at < email.Length - 3 && email.IndexOf('.', at) > at + 1 && !email.Any(char.IsWhiteSpace);
    }
}
