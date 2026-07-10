using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using ProtocolLab.Amqp;
using ProtocolLab.Coap;
using ProtocolLab.Gateway.Endpoints;
using ProtocolLab.Gateway.Infrastructure;
using ProtocolLab.Gateway.Realtime;
using ProtocolLab.Mqtt;
using ProtocolLab.Quic;
using ProtocolLab.Shared.Contracts;
using ProtocolLab.Udp;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Kestrel: HTTP/1.1 + HTTP/2 em texto claro, e HTTP/3 sobre TLS quando possível.
//
// HTTP/3 é QUIC. Ele não existe sem TLS 1.3, e portanto não existe sem certificado —
// nem em localhost. É o mesmo motivo pelo qual o listener QUIC bruto da aba QUIC
// precisa gerar um certificado self-signed em memória.
// ---------------------------------------------------------------------------
var httpPort = builder.Configuration.GetValue("Gateway:HttpPort", 5080);
var httpsPort = builder.Configuration.GetValue("Gateway:HttpsPort", 5443);
var devCertificate = DevCertificateLocator.TryFind();

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.ListenLocalhost(httpPort, listen => listen.Protocols = HttpProtocols.Http1AndHttp2);

    if (devCertificate is not null)
    {
        kestrel.ListenLocalhost(httpsPort, listen =>
        {
            // Http1AndHttp2AndHttp3: o cliente chega por TCP, o Kestrel anuncia o header
            // Alt-Svc, e o cliente migra para QUIC nas requisições seguintes. É assim que
            // HTTP/3 é descoberto na prática — não há negociação no primeiro contato.
            listen.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
            listen.UseHttps(devCertificate);
        });
    }
});

// ---------------------------------------------------------------------------
// Serialização: enums como string em toda a superfície pública (HTTP e SignalR),
// porque o frontend usa "Mqtt"/"Udp" como chave de aba, não índices numéricos.
// ---------------------------------------------------------------------------
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services
    .AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.PayloadSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

const string FrontendCors = "frontend";
var allowedOrigins = builder.Configuration
    .GetSection("Gateway:AllowedOrigins")
    .Get<string[]>() ?? ["http://localhost:5173"];

builder.Services.AddCors(options => options.AddPolicy(FrontendCors, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()

    // Exigido pelo SignalR: o handshake do WebSocket precisa enviar credenciais.
    // AllowCredentials é incompatível com AllowAnyOrigin — daí a lista explícita de origens.
    .AllowCredentials()));

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<BrokerUnavailableExceptionHandler>();

// ---------------------------------------------------------------------------
// O stream de eventos costura tudo: as bibliotecas de protocolo só conhecem
// IProtocolEventSink; apenas o gateway sabe que os eventos acabam num WebSocket.
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<ProtocolEventStream>();
builder.Services.AddSingleton<IProtocolEventSink>(sp => sp.GetRequiredService<ProtocolEventStream>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<ProtocolEventStream>());

builder.Services.AddUdpDemo(builder.Configuration);
builder.Services.AddQuicDemo(builder.Configuration);
builder.Services.AddMqttDemo(builder.Configuration);
builder.Services.AddAmqpDemo(builder.Configuration);
builder.Services.AddCoapDemo(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors(FrontendCors);

app.MapOpenApi();

app.MapUdpEndpoints();
app.MapQuicEndpoints();
app.MapMqttEndpoints();
app.MapAmqpEndpoints();
app.MapCoapEndpoints();
app.MapMetaEndpoints();

app.MapHub<ProtocolEventsHub>("/hub/events");

app.MapGet("/", () => Results.Ok(new
{
    service = "ProtocolLab Gateway",
    openapi = "/openapi/v1.json",
    hub = "/hub/events",
    http3 = devCertificate is not null
        ? $"https://localhost:{httpsPort}"
        : "desabilitado — rode 'dotnet dev-certs https --trust'"
}));

if (devCertificate is null)
{
    app.Logger.LogWarning(
        "Certificado de desenvolvimento não encontrado: HTTPS e HTTP/3 desabilitados. " +
        "Rode 'dotnet dev-certs https --trust' para habilitá-los. As demais demos funcionam normalmente.");
}

app.Run();
