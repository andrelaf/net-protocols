using ProtocolLab.Amqp;
using ProtocolLab.Gateway.Realtime;
using ProtocolLab.Mqtt;
using ProtocolLab.Quic;

namespace ProtocolLab.Gateway.Endpoints;

/// <param name="Available">Se a demonstração pode ser executada agora.</param>
/// <param name="Requires">O que precisa estar de pé para ela funcionar.</param>
/// <param name="Detail">Diagnóstico legível quando indisponível.</param>
public sealed record ProtocolAvailability(string Protocol, bool Available, string Requires, string? Detail = null);

public sealed record GatewayStatus(
    IReadOnlyList<ProtocolAvailability> Protocols,
    long DroppedEvents,
    string Runtime);

public static class MetaEndpoints
{
    public static IEndpointRouteBuilder MapMetaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Meta");

        // A UI chama isto ao carregar para saber quais abas podem executar demos de verdade
        // e quais precisam do docker compose no ar. Melhor do que deixar o usuário clicar
        // num botão e receber um erro de conexão sem contexto.
        group.MapGet("/status", (MqttDemoService mqtt, AmqpDemoService amqp, ProtocolEventStream stream) =>
        {
            var mqttStatus = mqtt.Status;
            var amqpStatus = amqp.Status;

            var protocols = new List<ProtocolAvailability>
            {
                new("Udp", true, "Nada — servidor embutido no gateway."),
                new("Quic", QuicEchoClient.IsSupported, "msquic (embutido no Windows 11+; libmsquic no Linux).",
                    QuicEchoClient.IsSupported ? null : "QuicConnection.IsSupported = false."),
                new("Mqtt", mqttStatus.Connected, $"Broker Mosquitto em {mqttStatus.Broker}.", mqttStatus.LastError),
                new("Amqp", amqpStatus.Connected, $"Broker RabbitMQ em {amqpStatus.Broker}.", amqpStatus.LastError),
                new("Coap", true, "Nada — servidor embutido no gateway.")
            };

            return Results.Ok(new GatewayStatus(protocols, stream.DroppedEvents, Environment.Version.ToString()));
        })
        .WithSummary("Disponibilidade de cada demonstração e saúde dos brokers.");

        group.MapGet("/events/recent", (ProtocolEventStream stream) => Results.Ok(stream.Recent))
            .WithSummary("Últimos eventos, para preencher a UI de uma aba aberta agora.");

        return app;
    }
}
