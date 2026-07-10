using ProtocolLab.Coap;

namespace ProtocolLab.Gateway.Endpoints;

public static class CoapEndpoints
{
    public static IEndpointRouteBuilder MapCoapEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/coap").WithTags("CoAP");

        group.MapPost("/get", async (CoapGetRequest request, CoapClient client, CancellationToken ct) =>
        {
            var response = await client.GetAsync(request.Path, request.Confirmable, ct);
            return Results.Ok(response);
        })
        .WithSummary("GET num recurso. CON retransmite até o ACK; NON dispara e esquece.");

        group.MapPost("/observe", async (CoapObserveRequest request, CoapClient client, CancellationToken ct) =>
        {
            var result = await client.ObserveAsync(request.MaxNotifications, ct);
            return Results.Ok(result);
        })
        .WithSummary("Observe (RFC 7641): pub/sub sem broker, direto entre cliente e servidor.");

        return app;
    }
}
