using Manoksha.Modules.Reporting.Contracts;

namespace Manoksha.Modules.Reporting.Application;

public sealed record BranchRefDto(Guid Id, string Name);

/// <param name="Cost">FIFO cost; null when the viewer may not see cost or a sale has no recorded cost.</param>
/// <param name="GrossProfit">Revenue − FIFO cost. Gross, never net: expenses are not captured (SPEC §31).</param>
public sealed record SalesBlockDto(int Orders, int Units, decimal Revenue, decimal ShippingFees, decimal? Cost, decimal? GrossProfit, decimal? MarginPct,
    IReadOnlyList<ChannelSales> ByChannel);

public sealed record DailySalesPointDto(DateOnly Date, int Orders, decimal Revenue, decimal? GrossProfit);

public sealed record BranchSalesDto(Guid BranchId, string BranchName, int OrdersToday, decimal RevenueToday, int OrdersMonth, decimal RevenueMonth, decimal? GrossProfitMonth);

/// <param name="StockValue">FIFO value of stock on hand (Owner only).</param>
public sealed record InventoryBlockDto(int AvailableUnits, int ReservedUnits, int InTransitUnits, int OnHandUnits, decimal? StockValue);

public sealed record OperationsBlockDto(int TransfersAwaitingApproval, int TransfersInProgress, int AdjustmentsPending, int DiscrepanciesOpen, int? DepositsPending);

public sealed record PriceChangeDto(DateTimeOffset At, string SkuCode, string ProductName, decimal? OldPrice, decimal NewPrice, string Reason);

public sealed record AttendanceBlockDto(int ClockedInNow, int ClockedInToday, int ActiveEmployees);

public sealed record TopResellerDto(Guid ResellerId, string ResellerNumber, string Name, int Orders, decimal Revenue);

public sealed record ResellerBlockDto(int Active, int Frozen, decimal WalletBalances, IReadOnlyList<TopResellerDto> TopThisMonth);

/// <summary>Owner / branch dashboard (SPEC §31). Branch users see only their branches and never cost, profit or stock value.</summary>
public sealed record DashboardDto(
    DateOnly Today,
    IReadOnlyList<BranchRefDto> Branches,
    bool CostVisible,
    SalesBlockDto SalesToday,
    SalesBlockDto SalesMonth,
    IReadOnlyList<DailySalesPointDto> Last14Days,
    IReadOnlyList<BranchSalesDto> BranchSales,
    InventoryBlockDto Inventory,
    int LowStockThreshold,
    int LowStockCount,
    IReadOnlyList<LowStockRow> LowStock,
    OperationsBlockDto Operations,
    AttendanceBlockDto Attendance,
    IReadOnlyList<PriceChangeDto>? PriceChanges,
    ResellerBlockDto? Resellers,
    IReadOnlyList<ExceptionCount>? OpenExceptions);

public sealed record SalesReportRowDto(string Key, string Label, int Orders, int Units, decimal Revenue, decimal ShippingFees, decimal? Cost, decimal? GrossProfit,
    decimal? MarginPct);

/// <param name="GroupBy">day, branch, channel or reseller.</param>
public sealed record SalesReportDto(DateOnly From, DateOnly To, string GroupBy, bool CostVisible, IReadOnlyList<SalesReportRowDto> Rows, SalesReportRowDto Total);

public sealed record ProductSalesRowDto(Guid SkuId, string SkuCode, string ProductName, string VariantName, int Units, decimal Revenue, decimal? Cost,
    decimal? GrossProfit, decimal? MarginPct);

public sealed record ProductSalesReportDto(DateOnly From, DateOnly To, bool CostVisible, IReadOnlyList<ProductSalesRowDto> Rows);

public sealed record ValuationRowDto(Guid BranchId, string BranchName, Guid SkuId, string SkuCode, string ProductName, string VariantName, int AvailableUnits,
    int OnHandUnits, int CostedUnits, decimal Value);

public sealed record ValuationBranchTotalDto(Guid BranchId, string BranchName, int CostedUnits, decimal Value);

public sealed record InventoryValuationDto(DateTimeOffset AsOf, IReadOnlyList<ValuationBranchTotalDto> Branches, decimal TotalValue, IReadOnlyList<ValuationRowDto> Rows);

public sealed record LowStockReportDto(int Threshold, IReadOnlyList<LowStockRow> Rows);

public sealed record ResellerReportRowDto(Guid ResellerId, string ResellerNumber, string Name, string Status, decimal DiscountPct, int Orders, decimal Revenue,
    decimal WalletBalance, DateTimeOffset? LastOrderAt);

public sealed record ResellerReportDto(DateOnly From, DateOnly To, IReadOnlyList<ResellerReportRowDto> Rows);

public sealed record ExceptionItemDto(string Type, Guid Id, string Reference, string Severity, Guid? BranchId, string? BranchName, DateTimeOffset CreatedAt, string Detail,
    Guid? RelatedId);

public sealed record ExceptionCenterDto(IReadOnlyList<ExceptionCount> Counts, IReadOnlyList<ExceptionItemDto> Items);

public sealed record ResellerDashboardDto(int OrdersThisMonth, decimal SpentThisMonth, int OpenOrders, int DeliveredThisMonth, decimal WalletBalance,
    decimal SpentAllTime, int OrdersAllTime);
