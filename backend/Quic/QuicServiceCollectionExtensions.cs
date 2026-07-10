using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtocolLab.Shared.Contracts;

namespace ProtocolLab.Quic;

public static class QuicServiceCollectionExtensions
{
    public static IServiceCollection AddQuicDemo(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<QuicDemoOptions>(configuration.GetSection(QuicDemoOptions.SectionName));
        services.TryAddSingleton<TelemetryGenerator>();
        services.AddSingleton<QuicEchoClient>();
        services.AddHostedService<QuicEchoServer>();
        return services;
    }
}
