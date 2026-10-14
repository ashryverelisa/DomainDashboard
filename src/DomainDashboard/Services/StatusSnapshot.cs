namespace DomainDashboard.Services;

public sealed record StatusSnapshot(
    DateTimeOffset? Updated,
    string? Error,
    IReadOnlyList<DomainStatus> Items,
    bool Checking,
    long? DurationMs
)
{
    public static StatusSnapshot Empty { get; } = new(null, null, [], false, null);
}
