using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ParentalTrack.Api.Options;

namespace ParentalTrack.Api.Modules.Devices;

/// <summary>
/// Composition surface of the Devices module: parent device management plus device enrollment.
/// Extracting this module into its own service means moving this folder and calling the same two
/// methods from the new host.
/// </summary>
public static class DevicesModule
{
    public static IServiceCollection AddDevicesModule(this IServiceCollection services, IConfiguration config)
    {
        // Binding is additive and idempotent, so it is safe for the composition root to bind the
        // shared Tracking section as well.
        services.Configure<DevicesOptions>(config.GetSection(DevicesOptions.SectionName));
        services.Configure<TrackingOptions>(config.GetSection(TrackingOptions.SectionName));

        services.AddScoped<DeviceService>();
        services.AddScoped<EnrollmentService>();

        return services;
    }

    public static IEndpointRouteBuilder MapDevicesModule(this IEndpointRouteBuilder app)
    {
        var devices = app.MapGroup("/api/v1/devices");

        // Literal device-facing routes first; the parent routes below are all {deviceId:guid}.
        devices.MapEnrollmentEndpoints();
        devices.MapDeviceEndpoints();

        return app;
    }
}
