using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Routing;
using ParentalTrack.Api.Options;
using ParentalTrack.Api.Security;

namespace ParentalTrack.Api.Modules.Auth;

/// <summary>
/// Composition root for the Auth module. Also owns the security primitives every other module
/// depends on (<see cref="TokenService"/>, <see cref="DeviceSessionValidator"/>), so registering
/// this module is enough to make device tokens verifiable.
/// </summary>
public static class AuthModule
{
    public static IServiceCollection AddAuthModule(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));
        services.Configure<AuthOptions>(config.GetSection(AuthOptions.SectionName));

        // Backing store for the device-session revocation cache.
        services.AddMemoryCache();

        // TryAdd: the composition root may have registered the same primitives already.
        services.TryAddSingleton<TokenService>();
        services.TryAddScoped<DeviceSessionValidator>();
        services.TryAddScoped<AuthService>();

        return services;
    }

    public static IEndpointRouteBuilder MapAuthModule(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapAuthEndpoints();
        return app;
    }
}
