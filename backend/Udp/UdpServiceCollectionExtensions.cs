using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtocolLab.Shared.Contracts;

namespace ProtocolLab.Udp;

public static class UdpServiceCollectionExtensions
{
    public static IServiceCollection AddUdpDemo(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<UdpDemoOptions>(configuration.GetSection(UdpDemoOptions.SectionName));

        // TryAdd: o gerador é compartilhado por todas as demos, e cada AddXxxDemo o registra.
        services.TryAddSingleton<TelemetryGenerator>();

        services.AddSingleton<UdpTelemetryClient>();
        services.AddHostedService<UdpTelemetryServer>();
        return services;
    }
}
