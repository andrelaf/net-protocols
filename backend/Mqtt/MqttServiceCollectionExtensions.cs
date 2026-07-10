using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ProtocolLab.Shared.Contracts;

namespace ProtocolLab.Mqtt;

public static class MqttServiceCollectionExtensions
{
    public static IServiceCollection AddMqttDemo(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MqttDemoOptions>(configuration.GetSection(MqttDemoOptions.SectionName));
        services.TryAddSingleton<TelemetryGenerator>();

        // Registrado duas vezes de propósito: uma como singleton (endpoints HTTP publicam
        // por ele) e outra como hosted service resolvendo a MESMA instância. Fazer
        // AddHostedService<MqttDemoService>() criaria um segundo objeto, com uma segunda
        // conexão ao broker usando o mesmo client id — e as duas se derrubariam em laço.
        services.AddSingleton<MqttDemoService>();
        services.AddHostedService(sp => sp.GetRequiredService<MqttDemoService>());

        return services;
    }
}
