using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParentalTrack.Api.Options;
using ParentalTrack.Infrastructure.Persistence;

namespace ParentalTrack.Api.Modules.Ingestion;

/// <summary>
/// Data minimisation (contract §4.4): location history older than
/// <see cref="IngestionOptions.RetentionDays"/> is deleted every six hours. Deletes run in capped
/// chunks so a first pass over a large table cannot hold one long lock or a giant transaction.
/// </summary>
internal sealed class LocationRetentionWorker : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private const int ChunkSize = 5_000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LocationRetentionWorker> _logger;
    private readonly int _retentionDays;

    public LocationRetentionWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<IngestionOptions> options,
        TimeProvider timeProvider,
        ILogger<LocationRetentionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;

        // A misconfigured zero would otherwise mean "delete everything on the next tick".
        _retentionDays = Math.Max(1, options.Value.RetentionDays);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Let the app finish starting before competing with it for the database.
            await Task.Delay(StartupDelay, _timeProvider, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval, _timeProvider);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Location retention pass failed; retrying at the next interval.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task PurgeAsync(CancellationToken ct)
    {
        var cutoff = _timeProvider.GetUtcNow().AddDays(-_retentionDays);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var deleted = 0;

        while (!ct.IsCancellationRequested)
        {
            var ids = await db.LocationRecords
                .AsNoTracking()
                .Where(record => record.RecordedAt < cutoff)
                .OrderBy(record => record.Id)
                .Select(record => record.Id)
                .Take(ChunkSize)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (ids.Count == 0)
            {
                break;
            }

            deleted += await db.LocationRecords
                .Where(record => ids.Contains(record.Id))
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);

            if (ids.Count < ChunkSize)
            {
                break;
            }
        }

        _logger.LogInformation(
            "Retention: deleted {DeletedCount} location records recorded before {Cutoff:O} ({RetentionDays}d).",
            deleted,
            cutoff,
            _retentionDays);
    }
}
