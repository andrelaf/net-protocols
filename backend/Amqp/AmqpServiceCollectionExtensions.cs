using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ProtocolLab.Shared.Contracts;

namespace ProtocolLab.Amqp;

public static class AmqpServiceCollectionExtensions
{
    public static IServiceCollection AddAmqpDemo(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AmqpDemoOptions>(configuration.GetSection(AmqpDemoOptions.SectionName));
        services.TryAddSingleton<TelemetryGenerator>();

        // Mesma instância como singleton e como hosted service: a conexão e os canais
        // pertencem ao serviço, e os endpoints HTTP publicam através dele.
        services.AddSingleton<AmqpDemoService>();
        services.AddHostedService(sp => sp.GetRequiredService<AmqpDemoService>());

        return services;
    }
}
