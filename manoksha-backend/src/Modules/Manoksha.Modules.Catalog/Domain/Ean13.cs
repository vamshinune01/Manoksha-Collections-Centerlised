namespace Manoksha.Modules.Catalog.Domain;

/// <summary>GTIN helpers. Internal barcodes use EAN-13 in the "in-store" prefix range 2x, which is never assigned to retail brands.</summary>
internal static class Gtin
{
    public const string InternalPrefix = "29";

    public static string CreateInternalEan13(long sequence)
    {
        if (sequence is < 1 or > 9_999_999_999)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }
        var body = InternalPrefix + sequence.ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
        return body + CheckDigit(body);
    }

    /// <summary>Standard GS1 mod-10 check digit for a GTIN body (all digits except the check digit).</summary>
    public static char CheckDigit(string body)
    {
        var sum = 0;
        for (var i = 0; i < body.Length; i++)
        {
            var digit = body[body.Length - 1 - i] - '0';
            sum += i % 2 == 0 ? digit * 3 : digit;
        }
        return (char)('0' + ((10 - (sum % 10)) % 10));
    }

    public static bool IsValidGtin(string code) =>
        code.Length is 8 or 12 or 13 or 14 && code.All(char.IsAsciiDigit) && CheckDigit(code[..^1]) == code[^1];
}
