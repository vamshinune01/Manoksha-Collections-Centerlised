using Manoksha.Persistence.Idempotency;
using Manoksha.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Manoksha.Persistence;

/// <summary>
/// The single database context of the modular monolith. Each module contributes its own entity
/// configurations (in its own PostgreSQL schema) through <see cref="IModuleModelConfiguration"/>. Sharing
/// one request-scoped context is what makes cross-module operations (e.g. wallet debit + inventory commit
/// + order creation) one real database transaction.
/// </summary>
public sealed class ManokshaDbContext : DbContext
{
    public const string PlatformSchema = "platform";
    public const string MigrationsAssembly = "Manoksha.Migrations";

    private readonly IReadOnlyList<IModuleModelConfiguration> _moduleConfigurations;

    public ManokshaDbContext(DbContextOptions<ManokshaDbContext> options, IEnumerable<IModuleModelConfiguration> moduleConfigurations)
        : base(options)
    {
        _moduleConfigurations = moduleConfigurations.OrderBy(c => c.Schema, StringComparer.Ordinal).ToList();
    }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration());

        foreach (var configuration in _moduleConfigurations)
        {
            configuration.Configure(modelBuilder);
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money and other decimals default to numeric(14,2); percentages override explicitly.
        configurationBuilder.Properties<decimal>().HavePrecision(14, 2);
        configurationBuilder.Properties<string>().HaveMaxLength(500);
    }
}

/// <summary>A module's EF Core model contribution. All its tables live in <see cref="Schema"/>.</summary>
public interface IModuleModelConfiguration
{
    string Schema { get; }

    void Configure(ModelBuilder modelBuilder);
}
