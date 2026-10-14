namespace DomainDashboard.Services;

public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";
    private const int MinRefreshInterval = 10;

    public string TraefikApiUrl { get; set; } = "http://traefik:8080";
    public string? TraefikApiUser { get; set; }
    public string? TraefikApiPassword { get; set; }
    public bool TraefikApiVerifySsl { get; set; } = true;

    public string Title { get; set; } = "Network Domains";
    public int RefreshInterval { get; set; } = 60;
    public double CheckTimeout { get; set; } = 5;
    public int CheckWorkers { get; set; } = 16;

    public string? CheckViaIp { get; set; }
    public int HttpsPort { get; set; } = 443;
    public int HttpPort { get; set; } = 80;

    public string? CaFile { get; set; }
    public int CertWarnDays { get; set; } = 14;

    public string? ExcludeDomains { get; set; }
    public string? ExcludeRouters { get; set; }

    /// <summary>Refresh interval in seconds, clamped to a sane minimum.</summary>
    public int EffectiveRefreshInterval => Math.Max(MinRefreshInterval, RefreshInterval);
}
