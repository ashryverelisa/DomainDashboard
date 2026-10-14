using System.Text.Json;
using System.Text.Json.Serialization;
using DomainDashboard.Services;
using Microsoft.Extensions.Options;

namespace DomainDashboard.Api;

public static class DashboardEndpoints
{
    public static IServiceCollection AddDashboardApi(this IServiceCollection services) =>
        services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            json.SerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)
            );
        });

    public static IEndpointRouteBuilder MapDashboardApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/status", GetStatus);
        app.MapPost("/api/refresh", RequestRefresh);
        app.MapGet("/healthz", () => Results.Text("ok"));
        return app;
    }

    private static IResult GetStatus(
        StatusStore store,
        IOptions<DashboardOptions> options,
        HttpContext http
    )
    {
        http.Response.Headers.CacheControl = "no-store";
        var s = store.Current;
        var o = options.Value;
        return Results.Json(
            new
            {
                s.Updated,
                s.Error,
                s.Items,
                s.Checking,
                s.DurationMs,
                Config = new
                {
                    o.Title,
                    Refresh = o.EffectiveRefreshInterval,
                    o.CertWarnDays,
                },
            }
        );
    }

    private static IResult RequestRefresh(StatusStore store)
    {
        store.RequestRefresh();
        return Results.Accepted();
    }
}
