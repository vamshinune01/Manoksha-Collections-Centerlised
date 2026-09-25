using Manoksha.Application.Abstractions;
using Manoksha.Persistence.Idempotency;
using Manoksha.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Manoksha.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddManokshaPersistence(this IServiceCollection services, string connectionString)
    {
        var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        services.AddSingleton(dataSource);
        services.AddDbContext<ManokshaDbContext>(options => Configure(options, dataSource));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOutbox, TransactionalOutbox>();
        services.AddScoped<IIdempotencyService, IdempotencyService>();
        return services;
    }

    public static void Configure(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options
            .UseNpgsql(dataSource, npgsql => npgsql
                .MigrationsAssembly(ManokshaDbContext.MigrationsAssembly)
                .MigrationsHistoryTable("ef_migrations_history", ManokshaDbContext.PlatformSchema))
            .UseSnakeCaseNamingConvention();
}
