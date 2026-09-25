namespace Manoksha.Modules.Resellers.Application;

public sealed record ResellerProfileDto(string ContactName, string? BusinessName, string Email, string AddressLine, string City, string State, string Pin, string? Notes);

public sealed record CreateResellerRequest(
    string ContactName,
    string? BusinessName,
    string Mobile,
    string Email,
    string AddressLine,
    string City,
    string State,
    string Pin,
    decimal ResellerDiscountPct,
    string? Notes,
    string Reason);

public sealed record UpdateResellerRequest(string ContactName, string? BusinessName, string Email, string AddressLine, string City, string State, string Pin, string? Notes, string Reason);

public sealed record ChangeResellerStatusRequest(string Status, string Reason);

public sealed record ChangeTermsRequest(decimal ResellerDiscountPct, string? Notes, string Reason);

public sealed record CommercialTermDto(Guid Id, int Version, decimal DiscountPct, string? Notes, string Reason, DateTimeOffset EffectiveFrom, bool IsCurrent);

public sealed record StatusChangeDto(string? FromStatus, string ToStatus, string Reason, Guid? ActorUserId, DateTimeOffset OccurredAt);

public sealed record ResellerSummaryDto(Guid Id, string ResellerNumber, string ContactName, string? BusinessName, string Mobile, string City, string Status,
    decimal CurrentDiscountPct, decimal WalletBalance, DateTimeOffset CreatedAt, DateTimeOffset? ActivatedAt);

public sealed record ResellerDetailDto(
    Guid Id,
    string ResellerNumber,
    Guid UserId,
    string Mobile,
    string Status,
    ResellerProfileDto Profile,
    decimal WalletBalance,
    CommercialTermDto CurrentTerms,
    IReadOnlyList<CommercialTermDto> TermsHistory,
    IReadOnlyList<StatusChangeDto> StatusHistory,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ActivatedAt);

/// <summary>What the reseller sees about themselves (no internal notes).</summary>
public sealed record ResellerSelfDto(
    string ResellerNumber,
    string ContactName,
    string? BusinessName,
    string Mobile,
    string Email,
    string Status,
    bool CanPlaceOrders,
    decimal ResellerDiscountPct,
    int TermsVersion,
    decimal WalletBalance);

public sealed record ResellerTermDto(int Version, decimal DiscountPct, string? Notes, DateTimeOffset EffectiveFrom, bool IsCurrent);
