namespace Manoksha.Application.Modules;

/// <summary>
/// Well-known identifiers for LOCAL/DEVELOPMENT seed data only (never used in staging/production).
/// Branch ids are shared so the branch master seeded in Phase 2 matches role assignments seeded in Phase 1.
/// </summary>
public static class DevelopmentSeedData
{
    public static readonly Guid BranchKarimnagar = Guid.Parse("01920000-0000-7000-8000-000000000001");
    public static readonly Guid BranchHyderabad = Guid.Parse("01920000-0000-7000-8000-000000000002");
    public static readonly Guid BranchMulugu = Guid.Parse("01920000-0000-7000-8000-000000000003");

    public const string OwnerEmail = "owner@manoksha.local";
    public const string ManagerEmail = "manager.karimnagar@manoksha.local";
    public const string SalesEmail = "sales.karimnagar@manoksha.local";
    public const string InventoryEmail = "inventory.karimnagar@manoksha.local";
}
