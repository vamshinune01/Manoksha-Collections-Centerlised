using FluentValidation;
using Manoksha.Application.Security;
using Manoksha.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Manoksha.Application.Http;

public static class AuthorizationPolicyNames
{
    public const string PermissionPrefix = "perm:";
    public const string AudiencePrefix = "aud:";

    public static string Permission(string code) => PermissionPrefix + code;

    public static string Audience(params string[] audiences) => AudiencePrefix + string.Join(',', audiences);
}

public static class EndpointExtensions
{
    /// <summary>Requires an authenticated token issued for one of the given audiences.</summary>
    public static TBuilder RequireAudience<TBuilder>(this TBuilder builder, params string[] audiences)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(AuthorizationPolicyNames.Audience(audiences));

    /// <summary>
    /// Requires the permission (globally or in at least one branch). Branch-specific checks are performed
    /// again in the service with <see cref="IPermissionService.EnsurePermissionForBranchAsync"/>.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(AuthorizationPolicyNames.Permission(permission));

    /// <summary>Validates the request body with its registered FluentValidation validator.</summary>
    public static RouteHandlerBuilder Validate<TRequest>(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var request = context.Arguments.OfType<TRequest>().FirstOrDefault();
            var validator = context.HttpContext.RequestServices.GetService(typeof(IValidator<TRequest>)) as IValidator<TRequest>;
            if (request is not null && validator is not null)
            {
                var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);
                if (!result.IsValid)
                {
                    return Results.ValidationProblem(
                        result.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
                        title: "Validation failed",
                        extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.ValidationFailed });
                }
            }
            return await next(context);
        });

    /// <summary>Reads and validates the Idempotency-Key header.</summary>
    public static string GetRequiredIdempotencyKey(this HttpRequest request)
    {
        var key = request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length is < 8 or > 128)
        {
            throw new BusinessRuleException(ErrorCodes.IdempotencyKeyRequired, "A valid Idempotency-Key header (8–128 characters) is required.", 400);
        }
        return key;
    }
}

/// <summary>Named rate-limit policies (configured by the API host).</summary>
public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Otp = "otp";
}
