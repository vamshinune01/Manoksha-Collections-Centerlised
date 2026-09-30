namespace Manoksha.Modules.Reporting.Contracts;

public sealed record LowStockRow(Guid BranchId, string BranchName, Guid SkuId, string SkuCode, string ProductName, string VariantName, int Available);

/// <param name="Revenue">Merchandise value of confirmed, not-cancelled orders (shipping excluded).</param>
/// <param name="Cost">FIFO cost of those sales; null when some sale has no recorded cost.</param>
public sealed record ChannelSales(string Channel, int Orders, int Units, decimal Revenue, decimal ShippingFees, decimal? Cost);

public sealed record ExceptionCount(string Type, int Open);

/// <summary>Read-only business queries shared with the Notifications module (low-stock alerts, the Owner's daily summary).</summary>
public interface IReportingQueries
{
    /// <param name="branchIds">Null = all branches.</param>
    Task<IReadOnlyList<LowStockRow>> LowStockAsync(IReadOnlyCollection<Guid>? branchIds, CancellationToken cancellationToken = default);

    /// <summary>Sales per channel for business dates (IST) from–to inclusive.</summary>
    Task<IReadOnlyList<ChannelSales>> SalesByChannelAsync(DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? branchIds, CancellationToken cancellationToken = default);

    /// <summary>Open items per Exception Center type (whole business).</summary>
    Task<IReadOnlyList<ExceptionCount>> OpenExceptionCountsAsync(CancellationToken cancellationToken = default);
}
