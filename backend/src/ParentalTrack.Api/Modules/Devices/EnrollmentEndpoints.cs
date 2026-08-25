using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using ParentalTrack.Api.Common;
using ParentalTrack.Api.Security;

namespace ParentalTrack.Api.Modules.Devices;

/// <summary>
/// Device-facing routes of the Devices module. Both patterns are literals and are registered before
/// the parent routes, which are all constrained to <c>{deviceId:guid}</c>, so neither can ever be
/// mistaken for a device id.
/// </summary>
internal static class EnrollmentEndpoints
{
    private const string EnrollmentTag = "Enrollment";

    private const string InvalidPairingCodeTitle = "Invalid pairing code";

    /// <summary>
    /// One message for unknown, expired, already-used and disabled-device codes alike, so the
    /// endpoint cannot be used to discover which codes exist. It spells out the single-use rule
    /// because a device that is already paired hits exactly this response.
    /// </summary>
    private const string InvalidPairingCodeDetail =
        "The pairing code is unknown, has expired, or has already been used. Pairing codes are single " +
        "use: generate a fresh code for this device in the parent dashboard and enter it again.";

    private const string DeviceNotFoundDetail = "Device not found.";

    internal static IEndpointRouteBuilder MapEnrollmentEndpoints(this IEndpointRouteBuilder devices)
    {
        devices.MapPost("/enroll", EnrollAsync)
            .AllowAnonymous()
            .RequireRateLimiting(AuthConstants.EnrollRateLimit)
            .WithTags(EnrollmentTag)
            .WithName("EnrollDevice")
            .Produces<EnrollResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        devices.MapGet("/me", GetSelfAsync)
            .RequireAuthorization(AuthConstants.DevicePolicy)
            .WithTags(EnrollmentTag)
            .WithName("GetEnrolledDevice")
            .Produces<DeviceSelfDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return devices;
    }

    private static async Task<IResult> EnrollAsync(
        [FromBody] EnrollRequest request,
        [FromServices] EnrollmentService service,
        HttpContext http,
        CancellationToken ct)
    {
        var userAgent = http.Request.Headers.UserAgent.ToString();
        var response = await service.EnrollAsync(request, userAgent, ct);

        return response is null
            ? Results.Problem(
                detail: InvalidPairingCodeDetail,
                statusCode: StatusCodes.Status400BadRequest,
                title: InvalidPairingCodeTitle)
            : Results.Ok(response);
    }

    private static async Task<IResult> GetSelfAsync(
        ClaimsPrincipal user,
        [FromServices] EnrollmentService service,
        CancellationToken ct)
    {
        var self = await service.GetSelfAsync(user.GetDeviceId(), ct);
        return self is null ? ApiResults.NotFound(DeviceNotFoundDetail) : Results.Ok(self);
    }
}
