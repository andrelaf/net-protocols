using ProtocolLab.Amqp;

namespace ProtocolLab.Gateway.Endpoints;

public static class AmqpEndpoints
{
    public static IEndpointRouteBuilder MapAmqpEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/amqp").WithTags("AMQP");

        group.MapPost("/publish", async (AmqpPublishRequest request, AmqpDemoService amqp, CancellationToken ct) =>
        {
            var result = await amqp.PublishAsync(request.Poison, request.Persistent, request.DeviceId, request.RoutingKey, ct);
            return Results.Ok(result);
        })
        .WithSummary("Publica no exchange topic. 'poison' força o caminho da DLQ; uma routing key sem binding dispara basic.return.");

        group.MapPost("/dlq/drain", async (AmqpDemoService amqp, CancellationToken ct) =>
        {
            var drained = await amqp.DrainDeadLetterQueueAsync(ct: ct);
            return Results.Ok(new { drained });
        })
        .WithSummary("Consome mensagens da dead-letter queue, como faria uma rotina de inspeção.");

        group.MapGet("/status", (AmqpDemoService amqp) => Results.Ok(amqp.Status))
            .WithSummary("Estado da conexão AMQP.");

        return app;
    }
}
