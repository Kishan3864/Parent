using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParentalTrack.Api.Options;
using ParentalTrack.Domain.Entities;

namespace ParentalTrack.Api.Modules.Ingestion;

/// <summary>
/// Hand-off between the ingest endpoint and <see cref="LocationIngestWorker"/>. Bounded so a burst
/// of devices cannot grow the heap without limit: when the queue is full, writers wait, and a
/// writer that waits longer than <see cref="EnqueueTimeout"/> is told to give up so the endpoint
/// can answer 503 and let the device retry with its points still on disk.
/// </summary>
public sealed class LocationIngestQueue
{
    private static readonly TimeSpan EnqueueTimeout = TimeSpan.FromSeconds(2);

    private readonly Channel<IReadOnlyList<LocationRecord>> _channel;
    private readonly ILogger<LocationIngestQueue> _logger;

    public LocationIngestQueue(IOptions<IngestionOptions> options, ILogger<LocationIngestQueue> logger)
    {
        _logger = logger;

        var capacity = Math.Max(1, options.Value.QueueCapacity);
        _channel = Channel.CreateBounded<IReadOnlyList<LocationRecord>>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>
    /// Queues one batch. Returns <c>false</c> when the queue stayed full for two seconds — the
    /// caller must then reject the request rather than block the device any longer.
    /// </summary>
    public async ValueTask<bool> EnqueueAsync(IReadOnlyList<LocationRecord> records, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            return true;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(EnqueueTimeout);

        try
        {
            await _channel.Writer.WriteAsync(records, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Ingest queue was full for {TimeoutSeconds}s; rejecting a batch of {PointCount} points.",
                EnqueueTimeout.TotalSeconds,
                records.Count);
            return false;
        }
        catch (ChannelClosedException)
        {
            // The application is shutting down; the device will retry against the next instance.
            return false;
        }
    }

    public IAsyncEnumerable<IReadOnlyList<LocationRecord>> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
