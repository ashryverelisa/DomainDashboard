using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace DomainDashboard.Services;

public sealed class RefreshWorker(
    TraefikClient traefik,
    DomainChecker checker,
    StatusStore store,
    IOptions<DashboardOptions> options,
    ILogger<RefreshWorker> log
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        var interval = TimeSpan.FromSeconds(o.EffectiveRefreshInterval);
        log.LogInformation(
            "Traefik API: {Url}, interval: {Interval}s",
            o.TraefikApiUrl,
            interval.TotalSeconds
        );

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Refresh failed");
                store.Set(store.Current with { Checking = false });
            }

            await store.WaitForNextRunAsync(interval, stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var o = options.Value;
        store.Set(store.Current with { Checking = true });
        var stopwatch = Stopwatch.StartNew();

        string? error = null;
        IReadOnlyList<DomainStatus> entries;
        try
        {
            entries = RouterParser.Build(await traefik.GetRoutersAsync(ct), o);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Keep checking the last known domains while Traefik is unreachable
            error = $"Traefik API at {o.TraefikApiUrl} is unreachable: {ex.Message}";
            log.LogWarning("{Error}", error);
            entries = store.Current.Items;
        }

        var results = await CheckAllAsync(entries, Math.Max(1, o.CheckWorkers), ct);
        var sorted = results
            .OrderBy(r => r.State)
            .ThenBy(r => r.Domain, StringComparer.Ordinal)
            .ThenBy(r => r.Path, StringComparer.Ordinal)
            .ToList();

        store.Set(
            new StatusSnapshot(
                DateTimeOffset.UtcNow,
                error,
                sorted,
                false,
                stopwatch.ElapsedMilliseconds
            )
        );
        log.LogInformation(
            "{Count} domains checked ({Online} online)",
            sorted.Count,
            sorted.Count(r => r.State != DomainState.Down)
        );
    }

    private async Task<DomainStatus[]> CheckAllAsync(
        IReadOnlyList<DomainStatus> entries,
        int workers,
        CancellationToken ct
    )
    {
        var results = new DomainStatus[entries.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, entries.Count),
            new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = ct },
            async (i, token) => results[i] = await checker.CheckAsync(entries[i], token)
        );
        return results;
    }
}
