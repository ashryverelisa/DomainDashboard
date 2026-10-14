using DomainDashboard.Services;
using MudBlazor;

namespace DomainDashboard.Components.Dashboard;

public static class DomainStateText
{
    extension(DomainState state)
    {
        public Color ToColor() =>
            state switch
            {
                DomainState.Up => Color.Success,
                DomainState.Warn => Color.Warning,
                DomainState.Down => Color.Error,
                _ => Color.Default,
            };

        public string ToIcon() =>
            state switch
            {
                DomainState.Up => Icons.Material.Filled.CheckCircle,
                DomainState.Warn => Icons.Material.Filled.Warning,
                DomainState.Down => Icons.Material.Filled.Error,
                _ => Icons.Material.Filled.Help,
            };

        public string ToText() =>
            state switch
            {
                DomainState.Up => "reachable",
                DomainState.Warn => "reachable with warnings",
                DomainState.Down => "unreachable",
                _ => "unknown",
            };
    }
}
