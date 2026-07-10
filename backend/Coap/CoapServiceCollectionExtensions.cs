using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtocolLab.Shared.Contracts;

namespace ProtocolLab.Coap;

public static class CoapServiceCollectionExtensions
{
    public static IServiceCollection AddCoapDemo(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CoapDemoOptions>(configuration.GetSection(CoapDemoOptions.SectionName));
        services.TryAddSingleton<TelemetryGenerator>();
        services.AddSingleton<CoapClient>();
        services.AddHostedService<CoapServer>();
        return services;
    }
}
