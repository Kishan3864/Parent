using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParentalTrack.Infrastructure.Persistence;

namespace ParentalTrack.Infrastructure;

/// <summary>
/// Composition root for the persistence layer. The API host calls
/// <see cref="AddInfrastructure(IServiceCollection, IConfiguration)"/> once at startup.
/// </summary>
public static class DependencyInjection
{
    /// <summary>Name of the connection string under <c>ConnectionStrings</c>.</summary>
    public const string ConnectionStringName = "Postgres";

    /// <summary>
    /// Registers <see cref="AppDbContext"/> against PostgreSQL with transient-fault retries enabled.
    /// Health checks and migrations are the host's responsibility.
    /// </summary>
    /// <param name="services">Service collection to add to.</param>
    /// <param name="configuration">Application configuration; must contain <c>ConnectionStrings:Postgres</c>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The connection string is missing or blank.</exception>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'ConnectionStrings:Postgres' is not configured. Set it in appsettings.json " +
                "or supply the ConnectionStrings__Postgres environment variable.");
        }

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorCodesToAdd: null)));

        return services;
    }
}
