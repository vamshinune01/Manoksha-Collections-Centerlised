namespace Manoksha.SharedKernel;

/// <summary>
/// A business rule was violated. Carries a stable machine-readable <see cref="Code"/> that clients use,
/// and the HTTP status the API should return.
/// </summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string code, string message, int statusCode = 422)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }

    public int StatusCode { get; }

    public IDictionary<string, object?> Details { get; } = new Dictionary<string, object?>();
}

public sealed class NotFoundException(string code, string message) : BusinessRuleException(code, message, 404);

public sealed class ForbiddenException(string code, string message) : BusinessRuleException(code, message, 403);

public sealed class ConflictException(string code, string message) : BusinessRuleException(code, message, 409);

/// <summary>Cross-cutting error codes. Modules define their own codes next to their rules.</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string InvalidMobileNumber = "INVALID_MOBILE_NUMBER";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string IdempotencyKeyRequired = "IDEMPOTENCY_KEY_REQUIRED";
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
}
