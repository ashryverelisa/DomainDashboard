using System.Text.Json;
using System.Text.RegularExpressions;

namespace DomainDashboard.Services;

public static partial class RouterParser
{
    [GeneratedRegex(@"\bHost\s*\(([^)]*)\)")]
    private static partial Regex HostRule();

    [GeneratedRegex(@"\bPathPrefix\s*\(\s*[`""]([^`""]*)[`""]")]
    private static partial Regex PathPrefixRule();

    [GeneratedRegex(@"[`""]([^`""]+)[`""]")]
    private static partial Regex QuotedString();

    private sealed class Accumulator
    {
        public required string Domain;
        public required string Path;
        public bool Https;
        public readonly List<string> Routers = [];
        public readonly SortedSet<string> Services = [];
        public readonly SortedSet<string> Providers = [];
        public readonly SortedSet<string> Entrypoints = [];
        public string RouterStatus = DomainStatus.RouterStatusEnabled;
        public string? RouterError;

        public DomainStatus ToStatus() =>
            new()
            {
                Domain = Domain,
                Path = Path,
                Https = Https,
                Routers = Routers,
                Services = [.. Services],
                Providers = [.. Providers],
                Entrypoints = [.. Entrypoints],
                RouterStatus = RouterStatus,
                RouterError = RouterError,
            };
    }

    public static List<DomainStatus> Build(IEnumerable<JsonElement> routers, DashboardOptions o)
    {
        var excludeDomains = CreateFilter(o.ExcludeDomains);
        var excludeRouters = CreateFilter(o.ExcludeRouters);
        var entries = new Dictionary<(string, string), Accumulator>();

        foreach (var r in routers)
        {
            var name = GetString(r, "name") ?? "";
            if (name.EndsWith("@internal", StringComparison.Ordinal))
                continue;
            if (excludeRouters?.IsMatch(name) == true)
                continue;

            var rule = GetString(r, "rule") ?? "";
            var hosts = ParseHosts(rule).Where(h => excludeDomains?.IsMatch(h) != true).ToList();
            if (hosts.Count == 0)
                continue;

            var path = ParsePath(rule);
            var hasTls = r.TryGetProperty("tls", out _);
            var status = GetString(r, "status") ?? DomainStatus.RouterStatusEnabled;
            var error = GetError(r);
            var service = GetString(r, "service");
            var provider = GetString(r, "provider");
            var entrypoints = GetStrings(r, "entryPoints");

            foreach (var host in hosts)
            {
                if (!entries.TryGetValue((host, path), out var e))
                    entries[(host, path)] = e = new Accumulator { Domain = host, Path = path };

                e.Https |= hasTls;
                e.Routers.Add(name);
                if (!string.IsNullOrEmpty(service))
                    e.Services.Add(service);
                if (!string.IsNullOrEmpty(provider))
                    e.Providers.Add(provider);
                e.Entrypoints.UnionWith(entrypoints);

                if (status == DomainStatus.RouterStatusEnabled)
                    continue;
                e.RouterStatus = status;
                e.RouterError = error;
            }
        }

        return [.. entries.Values.Select(e => e.ToStatus())];
    }

    private static Regex? CreateFilter(string? pattern) =>
        string.IsNullOrWhiteSpace(pattern) ? null : new Regex(pattern, RegexOptions.IgnoreCase);

    private static IEnumerable<string> ParseHosts(string rule) =>
        HostRule()
            .Matches(rule)
            .SelectMany(m => QuotedString().Matches(m.Groups[1].Value))
            .Select(q => q.Groups[1].Value.Trim().ToLowerInvariant())
            .Where(h => h.Length > 0);

    private static string ParsePath(string rule)
    {
        var match = PathPrefixRule().Match(rule);
        var path = match.Success ? match.Groups[1].Value : "/";
        return path.StartsWith('/') ? path : "/" + path;
    }

    private static string? GetError(JsonElement router)
    {
        if (!router.TryGetProperty("error", out var error))
            return null;
        return error.ValueKind == JsonValueKind.Array
            ? string.Join("; ", error.EnumerateArray().Select(e => e.ToString()))
            : error.ToString();
    }

    private static string? GetString(JsonElement el, string property) =>
        el.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static List<string> GetStrings(JsonElement el, string property) =>
        el.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Array
            ?
            [
                .. v.EnumerateArray()
                    .Where(i => i.ValueKind == JsonValueKind.String)
                    .Select(i => i.GetString()!),
            ]
            : [];
}
