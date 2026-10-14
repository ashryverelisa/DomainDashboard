using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace DomainDashboard.Services;

public sealed class TraefikClient(HttpClient http, IOptions<DashboardOptions> options)
{
    private const int PageSize = 100;
    private const int MaxPages = 50;

    public async Task<List<JsonElement>> GetRoutersAsync(CancellationToken ct)
    {
        var o = options.Value;
        var baseUrl = o.TraefikApiUrl.TrimEnd('/');
        var auth = CreateAuthHeader(o);
        var routers = new List<JsonElement>();
        var page = 1;

        while (page <= MaxPages)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{baseUrl}/api/http/routers?per_page={PageSize}&page={page}"
            );
            request.Headers.Authorization = auth;

            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                routers.AddRange(doc.RootElement.EnumerateArray().Select(router => router.Clone()));

            var next = GetNextPage(response);
            if (next <= page)
                break;
            page = next;
        }

        return routers;
    }

    private static AuthenticationHeaderValue? CreateAuthHeader(DashboardOptions o)
    {
        if (string.IsNullOrEmpty(o.TraefikApiUser))
            return null;

        var token = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{o.TraefikApiUser}:{o.TraefikApiPassword}")
        );
        return new AuthenticationHeaderValue("Basic", token);
    }

    private static int GetNextPage(HttpResponseMessage response) =>
        response.Headers.TryGetValues("X-Next-Page", out var values)
        && int.TryParse(values.FirstOrDefault(), out var next)
            ? next
            : 0;
}
