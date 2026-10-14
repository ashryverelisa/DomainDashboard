using DomainDashboard.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using MudBlazor;

namespace DomainDashboard.Components.Layout;

public partial class MainLayout
{
    private MudThemeProvider _themeProvider = null!;
    private bool _isDarkMode;

    [Inject]
    private IOptions<DashboardOptions> Options { get; set; } = null!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _isDarkMode = await _themeProvider.GetSystemDarkModeAsync();
            StateHasChanged();
        }
    }

    private void ToggleDarkMode() => _isDarkMode = !_isDarkMode;
}
