using Manoksha.Application.Http;
using Manoksha.Application.Security;
using Manoksha.Modules.Payments.Application;
using Manoksha.Modules.Payments.Simulator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Manoksha.Modules.Payments.Endpoints;

internal static class PaymentEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints, bool simulatorEnabled)
    {
        // Provider callbacks: anonymous but signature-verified; 2xx only after the event is durably stored (design §10).
        endpoints.MapPost("/api/v1/webhooks/payments/{provider}", async (string provider, HttpRequest http, PaymentWebhookService webhooks, CancellationToken ct) =>
            {
                if (http.ContentLength > PaymentWebhookService.MaxPayloadBytes)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }
                using var reader = new StreamReader(http.Body);
                var body = await reader.ReadToEndAsync(ct);
                if (body.Length > PaymentWebhookService.MaxPayloadBytes)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }
                var headers = http.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
                return await webhooks.ReceiveAsync(provider, headers, body, ct) switch
                {
                    WebhookReceipt.Accepted => Results.Ok(new { received = true, duplicate = false }),
                    WebhookReceipt.Duplicate => Results.Ok(new { received = true, duplicate = true }),
                    WebhookReceipt.InvalidSignature => Results.Unauthorized(),
                    _ => Results.NotFound(),
                };
            })
            .AllowAnonymous().WithTags("Payment webhooks").WithName("PaymentWebhook").ExcludeFromDescription();

        var admin = endpoints.MapGroup("/api/v1/admin").WithTags("Payments").RequireAudience(Audiences.Admin);
        admin.MapGet("/payments", (string? status, string? purpose, Guid? referenceId, PaymentQueryService s, CancellationToken ct) =>
            s.ListAttemptsAsync(status, purpose, referenceId, ct)).RequirePermission(Permissions.Exceptions.View).WithName("ListPaymentAttempts");
        admin.MapGet("/payment-reconciliations", (string? status, PaymentQueryService s, CancellationToken ct) => s.ListReconciliationsAsync(status, ct))
            .RequirePermission(Permissions.Exceptions.View).WithName("ListPaymentReconciliations");
        admin.MapGet("/payment-reconciliations/{id:guid}", (Guid id, PaymentQueryService s, CancellationToken ct) => s.GetReconciliationAsync(id, ct))
            .RequirePermission(Permissions.Exceptions.View).WithName("GetPaymentReconciliation");
        admin.MapGet("/payment-reconciliations/{id:guid}/history", (Guid id, ReconciliationService s, CancellationToken ct) => s.HistoryAsync(id, ct))
            .RequirePermission(Permissions.Exceptions.View).WithName("GetPaymentReconciliationHistory");
        // Owner-only (SPEC §36): markers and notes; refunds themselves happen outside the application.
        admin.MapPost("/payment-reconciliations/{id:guid}/actions", (Guid id, ReconciliationActionRequest r, ReconciliationService s, CancellationToken ct) =>
            s.ActAsync(id, r, ct)).RequirePermission(Permissions.Exceptions.ReconciliationManage).WithName("ActOnPaymentReconciliation");

        if (simulatorEnabled)
        {
            // The payer's simulated UPI app. Mapped only when the simulator is the configured provider (never in Production).
            var sim = endpoints.MapGroup("/api/v1/payment-simulator/{providerOrderRef}").WithTags("Payment simulator (development)").AllowAnonymous();
            sim.MapGet("", (string providerOrderRef, SimulatorService s, CancellationToken ct) => s.GetAsync(providerOrderRef, ct)).WithName("SimulatorGetPayment");
            sim.MapPost("/approve", (string providerOrderRef, SimulatorApproveRequest r, SimulatorService s, CancellationToken ct) => s.ApproveAsync(providerOrderRef, r, ct))
                .WithName("SimulatorApprovePayment");
            sim.MapPost("/decline", (string providerOrderRef, SimulatorDeclineRequest r, SimulatorService s, CancellationToken ct) => s.DeclineAsync(providerOrderRef, r, ct))
                .WithName("SimulatorDeclinePayment");
        }
    }
}
