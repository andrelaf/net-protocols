using ProtocolLab.Mqtt;

namespace ProtocolLab.Gateway.Endpoints;

public static class MqttEndpoints
{
    public static IEndpointRouteBuilder MapMqttEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/mqtt").WithTags("MQTT");

        group.MapPost("/publish", async (MqttPublishRequest request, MqttDemoService mqtt, CancellationToken ct) =>
        {
            if (request.Qos is < 0 or > 2)
            {
                return Results.BadRequest(new { error = "QoS precisa ser 0, 1 ou 2." });
            }

            var result = await mqtt.PublishTelemetryAsync(request.Qos, request.Retain, request.DeviceId, ct);
            return Results.Ok(result);
        })
        .WithSummary("Publica uma leitura. Compare a latência entre QoS 0, 1 e 2.");

        group.MapPost("/clear-retained", async (MqttClearRetainedRequest request, MqttDemoService mqtt, CancellationToken ct) =>
        {
            await mqtt.ClearRetainedAsync(request.DeviceId, ct);
            return Results.Ok(new { request.DeviceId, cleared = true });
        })
        .WithSummary("Apaga a mensagem retida de um dispositivo publicando payload vazio com retain=true.");

        group.MapGet("/status", (MqttDemoService mqtt) => Results.Ok(mqtt.Status))
            .WithSummary("Estado da sessão MQTT.");

        return app;
    }
}
