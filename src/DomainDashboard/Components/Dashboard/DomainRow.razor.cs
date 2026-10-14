using DomainDashboard.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace DomainDashboard.Components.Dashboard;

public partial class DomainRow
{
    private string _details = "";
    private List<(Color Color, string Text)> _problems = [];

    [Parameter, EditorRequired] public DomainStatus Item { get; set; } = null!;
    [Parameter] public int CertWarnDays { get; set; } = 14;

    private bool CertExpiresSoon => Item.CertExpiresWithin(CertWarnDays);

    private Color CodeColor =>
        Item.IsServerError ? Color.Error
        : Item.IsClientError ? Color.Warning
        : Color.Default;

    protected override void OnParametersSet()
    {
        _details = BuildDetails();
        _problems = [.. GetProblems()];
    }

    private string BuildDetails()
    {
        List<string> parts =
        [
            "Service " + (Item.Services.Count > 0 ? string.Join(", ", Item.Services) : "–"),
            "Router " + string.Join(", ", Item.Routers),
        ];
        if (Item.Ip is not null)
            parts.Add("IP " + Item.Ip);
        return string.Join(" · ", parts);
    }

    private IEnumerable<(Color Color, string Text)> GetProblems()
    {
        if (Item.Error is not null)
            yield return (Color.Error, Item.Error);
        if (Item.IsServerError)
            yield return (Color.Error, $"Server error {Item.Code}");
        else if (Item.IsClientError)
            yield return (Color.Warning, $"Response {Item.Code}");
        if (Item.CertValid == false)
            yield return (Color.Warning, $"Certificate invalid: {Item.CertError}");
        if (CertExpiresSoon)
            yield return (Color.Warning, $"Certificate expires in {Item.CertDays} days");
        if (!Item.IsRouterEnabled)
            yield return (Color.Warning, $"Router status in Traefik: {Item.RouterStatus}"
                                         + (Item.RouterError is { Length: > 0 } err ? $" ({err})" : ""));
    }
}
