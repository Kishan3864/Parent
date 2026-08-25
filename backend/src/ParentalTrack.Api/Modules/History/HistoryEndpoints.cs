using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using ParentalTrack.Api.Common;
using ParentalTrack.Api.Options;
using ParentalTrack.Api.Security;

namespace ParentalTrack.Api.Modules.History;

/// <summary>
/// Parent-facing read endpoints. A device that is not the caller's answers 404 rather than 403 so
/// the API never confirms that somebody else's device id exists.
/// </summary>
internal static class HistoryEndpoints
{
    private const int DefaultLimit = 1_000;
    private const int MaxLimit = 5_000;
    private const string AscendingOrder = "asc";
    private const string DescendingOrder = "desc";

    private static readonly TimeSpan DefaultRange = TimeSpan.FromHours(24);

    internal static void Map(IEndpointRouteBuilder app)
    {
        var devices = app.MapGroup("/api/v1/devices/{deviceId:guid}")
            .WithTags("History")
            .RequireAuthorization(AuthConstants.ParentPolicy);

        devices.MapGet("/location/current", GetCurrentAsync)
            .WithName("GetCurrentLocation")
            .WithSummary("Latest known position of one child device.")
            .Produces<LocationSnapshotDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        devices.MapGet("/locations", GetHistoryAsync)
            .WithName("GetLocationHistory")
            .WithSummary("Recorded track of one child device over a time range.")
            .Produces<LocationHistoryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/v1/config", GetConfig)
            .WithTags("History")
            .RequireAuthorization(AuthConstants.ParentPolicy)
            .WithName("GetAppConfig")
            .WithSummary("Thresholds and map settings the parent dashboard renders with.")
            .Produces<AppConfigDto>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> GetCurrentAsync(
        Guid deviceId,
        ClaimsPrincipal user,
        HistoryService history,
        CancellationToken ct)
    {
        var result = await history.GetCurrentAsync(user.GetParentId(), deviceId, ct);

        switch (result.Outcome)
        {
            case CurrentLocationOutcome.Found when result.Snapshot is not null:
                return TypedResults.Ok(result.Snapshot);
            case CurrentLocationOutcome.NeverReported:
                return TypedResults.NoContent();
            default:
                return ApiResults.NotFound("Device not found", "No such device on this account.");
        }
    }

    private static async Task<IResult> GetHistoryAsync(
        Guid deviceId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? limit,
        string? order,
        double? minAccuracyMeters,
        bool? simplify,
        ClaimsPrincipal user,
        HistoryService history,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var toUtc = (to ?? now).ToUniversalTime();
        var fromUtc = (from ?? toUtc - DefaultRange).ToUniversalTime();

        if (fromUtc > toUtc)
        {
            return ApiResults.BadRequest(
                "Invalid history range",
                "'from' must not be later than 'to'.");
        }

        bool descending;
        if (string.IsNullOrWhiteSpace(order) || order.Equals(AscendingOrder, StringComparison.OrdinalIgnoreCase))
        {
            descending = false;
        }
        else if (order.Equals(DescendingOrder, StringComparison.OrdinalIgnoreCase))
        {
            descending = true;
        }
        else
        {
            return ApiResults.BadRequest(
                "Invalid history query",
                $"'order' must be '{AscendingOrder}' or '{DescendingOrder}'.");
        }

        // "NaN" is a parseable double, so the guard has to be explicit.
        if (minAccuracyMeters is { } minAccuracy && (double.IsNaN(minAccuracy) || minAccuracy <= 0))
        {
            return ApiResults.BadRequest(
                "Invalid history query",
                "'minAccuracyMeters' must be greater than zero.");
        }

        var query = new HistoryQuery(
            fromUtc,
            toUtc,
            Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit),
            descending,
            minAccuracyMeters,
            simplify ?? false);

        var response = await history.GetHistoryAsync(user.GetParentId(), deviceId, query, ct);
        if (response is null)
        {
            return ApiResults.NotFound("Device not found", "No such device on this account.");
        }

        return TypedResults.Ok(response);
    }

    private static Ok<AppConfigDto> GetConfig(IOptions<TrackingOptions> tracking)
    {
        var options = tracking.Value;

        return TypedResults.Ok(new AppConfigDto(
            options.OnlineThresholdSeconds,
            options.StaleThresholdSeconds,
            options.DefaultRefreshSeconds,
            options.MapTileUrl,
            options.MapAttribution));
    }
}
