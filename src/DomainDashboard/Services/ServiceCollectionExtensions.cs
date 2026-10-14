using System.Net.Security;
using Microsoft.Extensions.Options;

namespace DomainDashboard.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDashboard(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.Configure<DashboardOptions>(
            configuration.GetSection(DashboardOptions.SectionName)
        );
        services.AddSingleton<StatusStore>();
        services.AddSingleton<DomainChecker>();
        services.AddHostedService<RefreshWorker>();

        services
            .AddHttpClient<TraefikClient>(client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(sp =>
                CreateTraefikHandler(sp.GetRequiredService<IOptions<DashboardOptions>>().Value)
            );

        return services;
    }

    private static SocketsHttpHandler CreateTraefikHandler(DashboardOptions o) =>
        new()
        {
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            SslOptions = o.TraefikApiVerifySsl
                ? new SslClientAuthenticationOptions()
                : new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, _, _, _) => true,
                },
        };
}
