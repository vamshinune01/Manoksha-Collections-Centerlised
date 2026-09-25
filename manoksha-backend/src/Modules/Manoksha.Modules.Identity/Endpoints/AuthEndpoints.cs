using Manoksha.Application.Http;
using Manoksha.Application.Security;
using Manoksha.Modules.Identity.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Manoksha.Modules.Identity.Endpoints;

internal static class AuthEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/v1/auth").WithTags("Auth");

        // Internal users (Owner / managers / employees) — admin web and POS.
        auth.MapPost("/internal/login", (InternalLoginRequest request, InternalAuthService service, CancellationToken ct) =>
                service.LoginAsync(request, ct))
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth).WithName("InternalLogin");

        auth.MapPost("/internal/password/first-change", (FirstPasswordChangeRequest request, InternalAuthService service, CancellationToken ct) =>
                service.CompleteFirstPasswordChangeAsync(request, ct))
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth).WithName("InternalFirstPasswordChange");

        auth.MapPost("/internal/password/change", async (ChangePasswordRequest request, InternalAuthService service, CancellationToken ct) =>
            {
                await service.ChangePasswordAsync(request, ct);
                return Results.NoContent();
            })
            .RequireAudience(Audiences.Admin, Audiences.Pos).RequireRateLimiting(RateLimitPolicies.Auth).WithName("InternalChangePassword");

        auth.MapPost("/internal/mfa/verify", (MfaVerifyRequest request, InternalAuthService service, CancellationToken ct) =>
                service.VerifyMfaAsync(request, ct))
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth).WithName("InternalMfaVerify");

        auth.MapPost("/internal/mfa/enroll/start", (MfaEnrollmentStartRequest request, InternalAuthService service, CancellationToken ct) =>
                service.StartMfaEnrollmentAsync(request, ct))
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth).WithName("InternalMfaEnrollStart");

        auth.MapPost("/internal/mfa/enroll/confirm", async (MfaEnrollmentConfirmRequest request, InternalAuthService service, CancellationToken ct) =>
            {
                var result = await service.ConfirmMfaEnrollmentAsync(request, ct);
                return result is null ? Results.NoContent() : Results.Ok(result);
            })
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth).WithName("InternalMfaEnrollConfirm");

        // Customers and resellers — mobile OTP.
        auth.MapPost("/otp/request", (OtpRequestRequest request, OtpService service, CancellationToken ct) =>
                service.RequestAsync(request.Mobile, request.Context, ct))
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Otp).WithName("OtpRequest");

        auth.MapPost("/otp/verify", (OtpVerifyRequest request, ExternalAuthService service, CancellationToken ct) =>
                service.VerifyOtpAsync(request, ct))
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Otp).WithName("OtpVerify");

        auth.MapPost("/customer/register", (CustomerRegistrationRequest request, ExternalAuthService service, CancellationToken ct) =>
                service.RegisterCustomerAsync(request, ct))
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Otp).WithName("CustomerRegister");

        // Sessions.
        auth.MapPost("/refresh", (RefreshRequest request, SessionService service, CancellationToken ct) =>
                service.RefreshAsync(request.RefreshToken, ct))
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicies.Auth).WithName("RefreshToken");

        auth.MapPost("/logout", async (LogoutRequest request, SessionService service, CancellationToken ct) =>
            {
                await service.RevokeByRefreshTokenAsync(request.RefreshToken, ct);
                return Results.NoContent();
            })
            .AllowAnonymous().WithName("Logout");

        auth.MapGet("/me", (MeService service, CancellationToken ct) => service.GetAsync(ct))
            .RequireAuthorization().WithName("Me");
    }
}
