using DomainDashboard.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace DomainDashboard.Components.Pages;

public partial class Home : IAsyncDisposable
{
    private static readonly TimeSpan RefreshButtonTimeout = TimeSpan.FromSeconds(60);

    private static readonly Func<DomainStatus, object> ByState = i => i.State;
    private static readonly Func<DomainStatus, object> ByDomain = i => i.DisplayName;
    private static readonly Func<DomainStatus, object> ByCode = i => i.Code ?? -1;
    private static readonly Func<DomainStatus, object> ByLatency = i =>
        i.LatencyMs ?? long.MaxValue;
    private static readonly Func<DomainStatus, object> ByCertDays = i => i.CertDays ?? int.MaxValue;

    private static readonly (DomainState State, string Label)[] StatTiles =
    [
        (DomainState.Up, "Reachable"),
        (DomainState.Warn, "Reachable with warnings"),
        (DomainState.Down, "Unreachable"),
    ];

    private readonly CancellationTokenSource _disposed = new();
    private StatusSnapshot _data = StatusSnapshot.Empty;
    private bool _problemsOnly;
    private string? _query = "";
    private bool _waitingForRefresh;
    private int _refreshTicket;
    private DateTimeOffset? _updatedBeforeRefresh;
    private TimeZoneInfo _timeZone = TimeZoneInfo.Local;
    private IJSObjectReference? _module;

    [Inject]
    private StatusStore Store { get; set; } = null!;

    [Inject]
    private IOptions<DashboardOptions> Options { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private int RefreshSeconds => Options.Value.EffectiveRefreshInterval;
    private int CertWarnDays => Options.Value.CertWarnDays;

    private string Headline =>
        _data.Items.Count switch
        {
            0 when _data.Updated is null => "Loading …",
            0 => "No domains found yet",
            var total => $"{total - CountOf(DomainState.Down)} of {total} domains reachable",
        };

    private List<DomainStatus> Visible => _data.Items.Where(Matches).ToList();

    private int CountOf(DomainState state) => _data.Items.Count(i => i.State == state);

    protected override void OnInitialized()
    {
        _data = Store.Current;
        Store.Changed += OnStoreChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        _module = await JS.InvokeAsync<IJSObjectReference>(
            "import",
            "./Components/Pages/Home.razor.js"
        );

        var zone = await _module.InvokeAsync<string?>("timeZone");
        if (zone is not null && TimeZoneInfo.TryFindSystemTimeZoneById(zone, out var tz))
        {
            _timeZone = tz;
            StateHasChanged();
        }
    }

    private void OnStoreChanged(StatusSnapshot snapshot) =>
        _ = InvokeAsync(() =>
        {
            _data = snapshot;
            if (
                _waitingForRefresh
                && !snapshot.Checking
                && snapshot.Updated != _updatedBeforeRefresh
            )
                _waitingForRefresh = false;
            StateHasChanged();
        });

    private bool Matches(DomainStatus item)
    {
        if (_problemsOnly && item.State == DomainState.Up)
            return false;
        var query = _query?.Trim() ?? "";
        if (query.Length == 0)
            return true;

        string[] haystack =
        [
            item.Domain,
            item.Path,
            item.Ip ?? "",
            .. item.Routers,
            .. item.Services,
            .. item.Providers,
        ];
        return haystack.Any(s => s.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private async Task RefreshNowAsync()
    {
        _waitingForRefresh = true;
        _updatedBeforeRefresh = _data.Updated;
        var ticket = ++_refreshTicket;
        Store.RequestRefresh();

        try
        {
            await Task.Delay(RefreshButtonTimeout, _disposed.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ticket == _refreshTicket)
            _waitingForRefresh = false;
    }

    public async ValueTask DisposeAsync()
    {
        Store.Changed -= OnStoreChanged;
        _disposed.Cancel();
        _disposed.Dispose();

        if (_module is null)
            return;
        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // Circuit is already gone, nothing to clean up in the browser
        }
    }
}
