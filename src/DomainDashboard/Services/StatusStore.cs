using System.Threading.Channels;

namespace DomainDashboard.Services;

public sealed class StatusStore
{
    private volatile StatusSnapshot _current = StatusSnapshot.Empty;
    private readonly Channel<bool> _trigger = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite }
    );

    public event Action<StatusSnapshot>? Changed;

    public StatusSnapshot Current => _current;

    public void Set(StatusSnapshot snapshot)
    {
        _current = snapshot;
        Changed?.Invoke(snapshot);
    }

    public void RequestRefresh() => _trigger.Writer.TryWrite(true);

    public async Task WaitForNextRunAsync(TimeSpan interval, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(interval);

        try
        {
            await _trigger.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }
}
