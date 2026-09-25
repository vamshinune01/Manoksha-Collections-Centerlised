using Manoksha.Modules.Wallet.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Manoksha.Modules.Wallet;

/// <summary>Background operations the Worker host runs for the Wallet module.</summary>
public static class WalletJobs
{
    /// <returns>Number of integrity issues found (0 = healthy). Findings are audited and raised as a CRITICAL event.</returns>
    public static async Task<int> RunIntegrityCheckAsync(IServiceProvider scopedServices, CancellationToken ct) =>
        (await scopedServices.GetRequiredService<WalletIntegrityService>().CheckAsync(recordFindings: true, ct)).Issues.Count;
}
