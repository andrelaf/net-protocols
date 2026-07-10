using ProtocolLab.Udp;

namespace ProtocolLab.Gateway.Endpoints;

public static class UdpEndpoints
{
    public static IEndpointRouteBuilder MapUdpEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/udp").WithTags("UDP");

        group.MapPost("/burst", async (UdpBurstRequest request, UdpTelemetryClient client, CancellationToken ct) =>
        {
            var result = await client.SendBurstAsync(
                request.Count,
                request.DeviceId,
                request.LossPercent,
                request.ReorderPercent,
                ct);

            return Results.Ok(result);
        })
        .WithSummary("Envia uma rajada de datagramas, opcionalmente simulando perda e reordenação.");

        group.MapPost("/oversized", async (UdpOversizedRequest request, UdpTelemetryClient client, CancellationToken ct) =>
        {
            var detail = await client.SendOversizedAsync(request.SizeBytes, ct);
            return Results.Ok(new { request.SizeBytes, detail });
        })
        .WithSummary("Demonstra fragmentação IP acima de 1472 bytes e o limite absoluto de 65507.");

        return app;
    }
}
