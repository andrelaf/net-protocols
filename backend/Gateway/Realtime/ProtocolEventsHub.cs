using Microsoft.AspNetCore.SignalR;
using ProtocolLab.Shared.Contracts;

namespace ProtocolLab.Gateway.Realtime;

/// <summary>Métodos que o servidor invoca no cliente. Tipado, para não errar o nome em string.</summary>
public interface IProtocolEventsClient
{
    Task ProtocolEvent(ProtocolEvent protocolEvent);
}

/// <summary>
/// Hub que transmite os eventos de todos os protocolos ao navegador.
///
/// <para>
/// <b>Por que um gateway existe neste projeto.</b> Um navegador não fala UDP, QUIC bruto,
/// MQTT sobre TCP, AMQP nem CoAP. Ele fala HTTP e WebSocket — e só. Qualquer UI que
/// "mostre MQTT" na verdade está falando com um processo servidor que fala MQTT por ela.
/// Este hub é essa ponte, e torná-la explícita é metade da lição de arquitetura aqui:
/// a escolha do protocolo acontece entre serviços, não entre o navegador e o mundo.
/// </para>
///
/// <para>
/// (A exceção é MQTT sobre WebSocket, que brokers como o Mosquitto expõem numa porta
/// separada — habilitamos essa porta no <c>docker-compose.yml</c> justamente para você
/// poder comparar as duas abordagens.)
/// </para>
/// </summary>
public sealed class ProtocolEventsHub : Hub<IProtocolEventsClient>;
