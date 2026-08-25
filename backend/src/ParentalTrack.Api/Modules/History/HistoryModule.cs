using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ParentalTrack.Api.Options;

namespace ParentalTrack.Api.Modules.History;

/// <summary>
/// Composition root for the parent-facing read slice: current position, track history and the
/// client configuration those two are rendered with.
/// </summary>
public static class HistoryModule
{
    public static IServiceCollection AddHistoryModule(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        services.Configure<TrackingOptions>(config.GetSection(TrackingOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<HistoryService>();

        return services;
    }

    public static IEndpointRouteBuilder MapHistoryModule(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        HistoryEndpoints.Map(app);
        return app;
    }
}
