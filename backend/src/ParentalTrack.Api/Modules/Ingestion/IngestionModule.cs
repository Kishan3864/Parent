using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Routing;
using ParentalTrack.Api.Options;

namespace ParentalTrack.Api.Modules.Ingestion;

/// <summary>
/// Composition root for the ingest slice: the queue, the writer that drains it and the retention
/// worker that ages its output out again.
/// </summary>
public static class IngestionModule
{
    public static IServiceCollection AddIngestionModule(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        services.Configure<IngestionOptions>(config.GetSection(IngestionOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<LocationIngestQueue>();
        services.AddHostedService<LocationIngestWorker>();
        services.AddHostedService<LocationRetentionWorker>();

        return services;
    }

    public static IEndpointRouteBuilder MapIngestionModule(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        IngestionEndpoints.Map(app);
        return app;
    }
}
