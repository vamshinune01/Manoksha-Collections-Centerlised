using Manoksha.Application.Http;
using Manoksha.Application.Security;
using Manoksha.Modules.Resellers.Contracts;
using Manoksha.Modules.Wallet.Application;
using Manoksha.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Manoksha.Modules.Wallet.Endpoints;

internal static class WalletEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin/wallet").WithTags("Wallet").RequireAudience(Audiences.Admin);
        admin.MapGet("/resellers/{resellerId:guid}/ledger", (Guid resellerId, long? beforeSeq, int? limit, WalletService s, CancellationToken ct) =>
            s.LedgerAsync(resellerId, beforeSeq, limit, ct)).RequirePermission(Permissions.Wallet.View).WithName("GetResellerLedger");
        admin.MapPost("/resellers/{resellerId:guid}/adjustments", (Guid resellerId, ManualAdjustmentRequest r, WalletService s, CancellationToken ct) =>
            s.AdjustAsync(resellerId, r, ct)).RequirePermission(Permissions.Wallet.Adjust).WithName("AdjustResellerWallet");
        admin.MapGet("/deposits", (string? status, Guid? resellerId, DepositService s, CancellationToken ct) => s.ListAsync(status, resellerId, ct))
            .RequirePermission(Permissions.Wallet.View).WithName("ListDeposits");
        admin.MapPost("/deposits/{id:guid}/approve", (Guid id, ReviewDepositRequest r, DepositService s, CancellationToken ct) => s.ApproveAsync(id, r, ct))
            .RequirePermission(Permissions.Wallet.DepositApprove).WithName("ApproveDeposit");
        admin.MapPost("/deposits/{id:guid}/reject", (Guid id, RejectDepositRequest r, DepositService s, CancellationToken ct) => s.RejectAsync(id, r, ct))
            .RequirePermission(Permissions.Wallet.DepositApprove).WithName("RejectDeposit");
        admin.MapGet("/deposits/{id:guid}/proof", async (Guid id, DepositService s, CancellationToken ct) =>
            {
                var (content, type) = await s.OpenProofAsync(id, asReseller: false, ct);
                return Results.File(content, type);
            }).RequirePermission(Permissions.Wallet.View).WithName("GetDepositProof");

        // Reseller self-service: the reseller is resolved from the token only.
        var self = endpoints.MapGroup("/api/v1/reseller/wallet").WithTags("Reseller portal").RequireAudience(Audiences.Reseller);
        self.MapGet("/", async (long? beforeSeq, int? limit, IResellerDirectory resellers, ICurrentUser user, WalletService s, CancellationToken ct) =>
        {
            var me = await resellers.FindByUserAsync(user.UserId, ct) ?? throw new ForbiddenException(ErrorCodes.Forbidden, "No reseller account is linked to this sign-in.");
            return await s.LedgerAsync(me.ResellerId, beforeSeq, limit, ct);
        }).WithName("ResellerWallet");
        self.MapGet("/deposits", (DepositService s, CancellationToken ct) => s.ListMineAsync(ct)).WithName("ResellerDeposits");
        self.MapPost("/deposits", ([FromForm] decimal amount, [FromForm] string method, [FromForm] string reference, [FromForm] string? note, IFormFile? proof,
                DepositService s, CancellationToken ct) => s.SubmitAsync(amount, method, reference, note, proof, ct))
            .DisableAntiforgery() // token-authenticated API; CSRF is handled by the BFF's same-origin + custom-header checks
            .WithName("SubmitDeposit");
        self.MapGet("/deposits/{id:guid}/proof", async (Guid id, DepositService s, CancellationToken ct) =>
        {
            var (content, type) = await s.OpenProofAsync(id, asReseller: true, ct);
            return Results.File(content, type);
        }).WithName("ResellerDepositProof");
    }
}
