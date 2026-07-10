using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ProtocolLab.Gateway.Infrastructure;

/// <summary>
/// Traduz "broker fora do ar" em 503 com uma mensagem acionável, em vez de deixar
/// vazar um 500 genérico. As demos de MQTT e AMQP dependem de containers que podem
/// simplesmente não estar rodando, e isso é uma condição esperada — não um bug.
/// </summary>
public sealed class BrokerUnavailableExceptionHandler(ILogger<BrokerUnavailableExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            InvalidOperationException => (StatusCodes.Status503ServiceUnavailable, "Dependência indisponível"),
            PlatformNotSupportedException => (StatusCodes.Status501NotImplemented, "Não suportado neste host"),
            ArgumentOutOfRangeException => (StatusCodes.Status400BadRequest, "Parâmetro inválido"),
            _ => (0, string.Empty)
        };

        if (statusCode == 0)
        {
            return false; // Deixa o pipeline padrão tratar: é um erro de verdade.
        }

        logger.LogWarning(exception, "Requisição para {Path} falhou com {StatusCode}", httpContext.Request.Path, statusCode);

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception.Message,
            Instance = httpContext.Request.Path
        }, cancellationToken);

        return true;
    }
}
