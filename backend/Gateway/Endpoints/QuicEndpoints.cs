using ProtocolLab.Quic;

namespace ProtocolLab.Gateway.Endpoints;

public static class QuicEndpoints
{
    public static IEndpointRouteBuilder MapQuicEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/quic").WithTags("QUIC");

        group.MapPost("/run", async (QuicRunRequest request, QuicEchoClient client, CancellationToken ct) =>
        {
            if (!QuicEchoClient.IsSupported)
            {
                return Results.Problem(
                    detail: "QUIC não está disponível neste host. No Linux instale libmsquic; no Windows use Win11 ou Server 2022+.",
                    statusCode: StatusCodes.Status501NotImplemented,
                    title: "QUIC indisponível");
            }

            var result = await client.RunParallelStreamsAsync(request.Streams, request.Message, request.SlowFirstStream, ct);
            return Results.Ok(result);
        })
        .WithSummary("Abre N streams paralelos numa conexão. O primeiro pode ser lento, para evidenciar a independência entre streams.");

        return app;
    }
}
