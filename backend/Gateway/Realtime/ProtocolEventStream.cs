using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using ProtocolLab.Shared.Contracts;

namespace ProtocolLab.Gateway.Realtime;

/// <summary>
/// Implementação de <see cref="IProtocolEventSink"/> que empurra eventos para o navegador.
///
/// <para>
/// <b>O detalhe que importa:</b> <see cref="PublishAsync"/> nunca bloqueia e nunca lança.
/// Ele deposita o evento num canal limitado e retorna. Um <see cref="BackgroundService"/>
/// separado drena o canal e faz o envio pelo SignalR.
/// </para>
///
/// <para>
/// Se o sink escrevesse direto no hub, a latência do WebSocket do usuário entraria no
/// caminho quente do consumidor MQTT e do laço de recepção UDP. Um navegador lento em
/// aba de fundo aplicaria contrapressão no broker. Com o canal em
/// <see cref="BoundedChannelFullMode.DropOldest"/>, a UI perde eventos antigos quando não
/// acompanha — que é exatamente o comportamento correto para telemetria de observabilidade:
/// <i>descartar dados de diagnóstico é sempre melhor do que degradar o sistema observado.</i>
/// </para>
/// </summary>
public sealed class ProtocolEventStream : BackgroundService, IProtocolEventSink
{
    private const int ChannelCapacity = 2048;
    private const int RecentBufferCapacity = 250;

    private readonly Channel<ProtocolEvent> _channel = Channel.CreateBounded<ProtocolEvent>(
        new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    /// <summary>Últimos eventos, para que uma aba aberta depois não veja uma tela vazia.</summary>
    private readonly ConcurrentQueue<ProtocolEvent> _recent = new();

    private readonly IHubContext<ProtocolEventsHub, IProtocolEventsClient> _hub;
    private readonly ILogger<ProtocolEventStream> _logger;

    private long _dropped;

    public ProtocolEventStream(
        IHubContext<ProtocolEventsHub, IProtocolEventsClient> hub,
        ILogger<ProtocolEventStream> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public long DroppedEvents => Interlocked.Read(ref _dropped);

    public IReadOnlyCollection<ProtocolEvent> Recent => _recent.ToArray();

    public ValueTask PublishAsync(ProtocolEvent protocolEvent, CancellationToken cancellationToken = default)
    {
        if (!_channel.Writer.TryWrite(protocolEvent))
        {
            Interlocked.Increment(ref _dropped);
        }

        return ValueTask.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var protocolEvent in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            _recent.Enqueue(protocolEvent);
            while (_recent.Count > RecentBufferCapacity && _recent.TryDequeue(out _))
            {
            }

            try
            {
                await _hub.Clients.All.ProtocolEvent(protocolEvent);
            }
            catch (Exception ex)
            {
                // Um cliente WebSocket problemático não pode derrubar o dispatcher.
                _logger.LogDebug(ex, "Falha ao transmitir evento {EventId}", protocolEvent.Id);
            }
        }
    }
}
