namespace Manoksha.Application.Security;

/// <summary>
/// Account context. The same person/mobile may hold a CUSTOMER and a RESELLER account; they are separate
/// identities with separate tokens and permissions (ADR-001 §2).
/// </summary>
public enum AccountType
{
    Internal = 1,
    Customer = 2,
    Reseller = 3,
}

/// <summary>Token audiences. A token is valid only for the route groups of its own audience.</summary>
public static class Audiences
{
    public const string Admin = "admin";
    public const string Pos = "pos";
    public const string Customer = "customer";
    public const string Reseller = "reseller";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Admin, Pos, Customer, Reseller };

    public static bool IsValidFor(string audience, AccountType accountType) => accountType switch
    {
        AccountType.Internal => audience is Admin or Pos,
        AccountType.Customer => audience == Customer,
        AccountType.Reseller => audience == Reseller,
        _ => false,
    };
}

public static class ManokshaClaimTypes
{
    public const string Subject = "sub";
    public const string AccountType = "acct";
    public const string SessionId = "sid";
    public const string SecurityStamp = "stamp";
    public const string Audience = "aud";
}
