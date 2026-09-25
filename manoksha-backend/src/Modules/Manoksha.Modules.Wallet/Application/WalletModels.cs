namespace Manoksha.Modules.Wallet.Application;

public sealed record LedgerEntryDto(
    Guid Id,
    long Seq,
    DateTimeOffset CreatedAt,
    string Type,
    string Direction,
    decimal Amount,
    decimal BalanceBefore,
    decimal BalanceAfter,
    Guid? OrderId,
    string? OrderNumber,
    Guid? DepositRequestId,
    Guid? ReversesEntryId,
    string? Reason,
    Guid? CreatedBy);

public sealed record LedgerPage(decimal Balance, IReadOnlyList<LedgerEntryDto> Entries, long? NextBeforeSeq);

public sealed record ManualAdjustmentRequest(string Direction, decimal Amount, string Reason);

public sealed record DepositDto(
    Guid Id,
    string Number,
    Guid ResellerId,
    string? ResellerNumber,
    string? ResellerName,
    decimal Amount,
    string Method,
    string Reference,
    string? ResellerNote,
    string Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ReviewedAt,
    string? ReviewNote,
    Guid? LedgerEntryId);

public sealed record ReviewDepositRequest(string? Note);

public sealed record RejectDepositRequest(string Reason);
