using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using ParentalTrack.Api.Common;
using ParentalTrack.Api.Security;

namespace ParentalTrack.Api.Modules.Devices;

/// <summary>
/// Parent-scoped device management. Every route is constrained with <c>{deviceId:guid}</c> so the
/// device-facing literals (<c>/enroll</c>, <c>/me</c>) can never be captured by them.
/// </summary>
internal static class DeviceEndpoints
{
    /// <summary>
    /// The same body for "no such device" and "that device is not yours" — the parent must not be
    /// able to tell the two apart.
    /// </summary>
    private const string DeviceNotFoundDetail = "Device not found.";

    private const string DevicesTag = "Devices";

    internal static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder devices)
    {
        devices.MapGet("/", ListDevicesAsync)
            .RequireAuthorization(AuthConstants.ParentPolicy)
            .WithTags(DevicesTag)
            .WithName("ListDevices")
            .Produces<DeviceSummaryDto[]>();

        devices.MapPost("/", CreateDeviceAsync)
            .RequireAuthorization(AuthConstants.ParentPolicy)
            .WithTags(DevicesTag)
            .WithName("CreateDevice")
            .Produces<DeviceDetailDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        devices.MapGet("/{deviceId:guid}", GetDeviceAsync)
            .RequireAuthorization(AuthConstants.ParentPolicy)
            .WithTags(DevicesTag)
            .WithName("GetDevice")
            .Produces<DeviceDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        devices.MapPatch("/{deviceId:guid}", UpdateDeviceAsync)
            .RequireAuthorization(AuthConstants.ParentPolicy)
            .WithTags(DevicesTag)
            .WithName("UpdateDevice")
            .Produces<DeviceDetailDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        devices.MapDelete("/{deviceId:guid}", DeleteDeviceAsync)
            .RequireAuthorization(AuthConstants.ParentPolicy)
            .WithTags(DevicesTag)
            .WithName("DeleteDevice")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        devices.MapPost("/{deviceId:guid}/pairing-code", RegeneratePairingCodeAsync)
            .RequireAuthorization(AuthConstants.ParentPolicy)
            .WithTags(DevicesTag)
            .WithName("RegeneratePairingCode")
            .Produces<PairingCodeDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        devices.MapPost("/{deviceId:guid}/revoke", RevokeDeviceAsync)
            .RequireAuthorization(AuthConstants.ParentPolicy)
            .WithTags(DevicesTag)
            .WithName("RevokeDevice")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return devices;
    }

    private static async Task<IResult> ListDevicesAsync(
        ClaimsPrincipal user,
        [FromServices] DeviceService service,
        CancellationToken ct)
    {
        var devices = await service.ListAsync(user.GetParentId(), ct);
        return Results.Ok(devices);
    }

    private static async Task<IResult> CreateDeviceAsync(
        [FromBody] CreateDeviceRequest request,
        ClaimsPrincipal user,
        [FromServices] DeviceService service,
        CancellationToken ct)
    {
        var invalid = Validate(request.ChildName, request.DeviceLabel, childNameRequired: true);
        if (invalid is not null)
        {
            return invalid;
        }

        var device = await service.CreateAsync(user.GetParentId(), request, ct);
        return Results.Created($"/api/v1/devices/{device.Id}", device);
    }

    private static async Task<IResult> GetDeviceAsync(
        [FromRoute] Guid deviceId,
        ClaimsPrincipal user,
        [FromServices] DeviceService service,
        CancellationToken ct)
    {
        var device = await service.GetAsync(user.GetParentId(), deviceId, ct);
        return device is null ? ApiResults.NotFound(DeviceNotFoundDetail) : Results.Ok(device);
    }

    private static async Task<IResult> UpdateDeviceAsync(
        [FromRoute] Guid deviceId,
        [FromBody] UpdateDeviceRequest request,
        ClaimsPrincipal user,
        [FromServices] DeviceService service,
        CancellationToken ct)
    {
        var invalid = Validate(request.ChildName, request.DeviceLabel, childNameRequired: false);
        if (invalid is not null)
        {
            return invalid;
        }

        var device = await service.UpdateAsync(user.GetParentId(), deviceId, request, ct);
        return device is null ? ApiResults.NotFound(DeviceNotFoundDetail) : Results.Ok(device);
    }

    private static async Task<IResult> DeleteDeviceAsync(
        [FromRoute] Guid deviceId,
        ClaimsPrincipal user,
        [FromServices] DeviceService service,
        CancellationToken ct)
    {
        var deleted = await service.DeleteAsync(user.GetParentId(), deviceId, ct);
        return deleted ? Results.NoContent() : ApiResults.NotFound(DeviceNotFoundDetail);
    }

    private static async Task<IResult> RegeneratePairingCodeAsync(
        [FromRoute] Guid deviceId,
        ClaimsPrincipal user,
        [FromServices] DeviceService service,
        CancellationToken ct)
    {
        var code = await service.RegeneratePairingCodeAsync(user.GetParentId(), deviceId, ct);
        return code is null ? ApiResults.NotFound(DeviceNotFoundDetail) : Results.Ok(code);
    }

    private static async Task<IResult> RevokeDeviceAsync(
        [FromRoute] Guid deviceId,
        ClaimsPrincipal user,
        [FromServices] DeviceService service,
        CancellationToken ct)
    {
        var revoked = await service.RevokeAsync(user.GetParentId(), deviceId, ct);
        return revoked ? Results.NoContent() : ApiResults.NotFound(DeviceNotFoundDetail);
    }

    /// <summary>
    /// PATCH treats a missing member as "leave it alone", so only the members that were sent are
    /// checked. An empty <c>deviceLabel</c> is a deliberate clear, not an error.
    /// </summary>
    private static IResult? Validate(string? childName, string? deviceLabel, bool childNameRequired)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (childName is null)
        {
            if (childNameRequired)
            {
                errors["childName"] = ["childName is required."];
            }
        }
        else if (string.IsNullOrWhiteSpace(childName))
        {
            errors["childName"] = ["childName must not be blank."];
        }
        else if (childName.Trim().Length > DeviceService.MaxChildNameLength)
        {
            errors["childName"] = [$"childName must be {DeviceService.MaxChildNameLength} characters or fewer."];
        }

        if (deviceLabel is not null && deviceLabel.Trim().Length > DeviceService.MaxDeviceLabelLength)
        {
            errors["deviceLabel"] = [$"deviceLabel must be {DeviceService.MaxDeviceLabelLength} characters or fewer."];
        }

        return errors.Count == 0
            ? null
            : Results.ValidationProblem(errors, title: "Invalid device request");
    }
}
