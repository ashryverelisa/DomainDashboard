namespace DomainDashboard.Services;

public sealed record DomainStatus
{
    public const string RouterStatusEnabled = "enabled";

    public required string Domain { get; init; }
    public required string Path { get; init; }
    public bool Https { get; init; }
    public IReadOnlyList<string> Routers { get; init; } = [];
    public IReadOnlyList<string> Services { get; init; } = [];
    public IReadOnlyList<string> Providers { get; init; } = [];
    public IReadOnlyList<string> Entrypoints { get; init; } = [];
    public string RouterStatus { get; init; } = RouterStatusEnabled;
    public string? RouterError { get; init; }

    public string Url { get; init; } = "";
    public int? Code { get; init; }
    public long? LatencyMs { get; init; }
    public string? Ip { get; init; }
    public bool? CertValid { get; init; }
    public int? CertDays { get; init; }
    public string? CertError { get; init; }
    public string? Error { get; init; }

    public DomainState State { get; init; } = DomainState.Unknown;

    public string DisplayName => Path == "/" ? Domain : Domain + Path;

    public bool IsRouterEnabled => RouterStatus == RouterStatusEnabled;

    public bool IsServerError => Code >= 500;

    /// <summary>4xx responses other than 401/403 (those just mean the site needs a login).</summary>
    public bool IsClientError => Code is >= 400 and < 500 and not 401 and not 403;

    public bool CertExpiresWithin(int days) => CertDays < days;
}
