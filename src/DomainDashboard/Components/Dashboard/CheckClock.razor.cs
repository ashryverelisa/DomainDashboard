using Microsoft.AspNetCore.Components;

namespace DomainDashboard.Components.Dashboard;

public partial class CheckClock : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private Timer? _timer;

    [Parameter]
    public DateTimeOffset? Updated { get; set; }

    [Parameter]
    public bool Checking { get; set; }

    [Parameter]
    public int RefreshSeconds { get; set; }

    [Parameter]
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Local;

    private string Text
    {
        get
        {
            if (Checking)
                return "Checking …";
            if (Updated is not { } updated)
                return "Not checked yet";

            var local = TimeZoneInfo.ConvertTime(updated, TimeZone);
            var remaining = updated.AddSeconds(RefreshSeconds) - DateTimeOffset.UtcNow;
            var next = Math.Max(0, (int)Math.Round(remaining.TotalSeconds));
            return $"Checked at {local:HH:mm:ss}, next check in {next} s";
        }
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
            _timer = new Timer(_ => InvokeAsync(StateHasChanged), null, TickInterval, TickInterval);
    }

    public void Dispose() => _timer?.Dispose();
}
