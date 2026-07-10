# Protocol Lab

Cinco protocolos de comunicação — **UDP, QUIC, MQTT, AMQP 0-9-1 e CoAP** — em execução lado a lado,
carregando exatamente a mesma carga de domínio, com um frontend React que explica os cenários de uso,
as boas práticas e os antipatterns de cada um enquanto você gera tráfego real.

Não há mocks. Cada botão da interface produz pacotes de verdade contra servidores de verdade.

Backend em .NET 10, frontend em React 19 + TypeScript, brokers em Docker.

---

## Por que existe um gateway no meio

Um navegador não fala UDP, QUIC bruto, MQTT sobre TCP, AMQP nem CoAP. Ele fala HTTP e WebSocket — e só.
Qualquer painel que afirme "mostrar MQTT" está, na verdade, conversando com um processo servidor que fala
MQTT por ele.

Tornar essa ponte explícita é metade da lição de arquitetura deste projeto: **a escolha do protocolo
acontece entre serviços, não entre o navegador e o mundo.**

```
┌─────────────────┐   HTTP + WebSocket (SignalR)   ┌──────────────────────────────┐
│  React (Vite)   │ ─────────────────────────────▶ │   ProtocolLab.Gateway        │
│  tabs/          │ ◀───────────────────────────── │   ASP.NET Core (.NET 10)     │
└─────────────────┘        stream de eventos       └───────────┬──────────────────┘
                                                               │
                        ┌──────────────────────────────────────┼───────────────────────────┐
                        │                                      │                           │
             servidores em processo                    clientes de broker                  │
                        │                                      │                           │
        ┌───────────────┼───────────────┐          ┌───────────┴──────────┐                │
        ▼               ▼               ▼          ▼                      ▼                │
   UDP :5001      QUIC :5002      CoAP :5683   Mosquitto :1883    RabbitMQ :5673           │
   (datagramas)   (TLS 1.3)       (RFC 7252)   (MQTT 5)           (AMQP 0-9-1)             │
                                                    │                      │               │
                                                    └────── docker compose ┘               │
                                                                                           │
        Todos emitem ProtocolEvent ──────────────────────────────────────────────────────▶─┘
```

O acoplamento é mínimo: cada biblioteca de protocolo conhece apenas a interface
[`IProtocolEventSink`](backend/Shared/Contracts/IProtocolEventSink.cs). Nenhuma delas sabe que o SignalR
existe. O gateway é o único que costura as duas pontas.

> **A exceção que vale conhecer.** MQTT sobre WebSocket permite que o navegador seja um cliente MQTT de
> primeira classe, sem gateway nenhum. O `mosquitto.conf` deste projeto habilita essa porta (9883 no host)
> justamente para você poder comparar as duas abordagens.

---

## Rodando

Pré-requisitos: **.NET SDK 10**, **Node 20+**, **Docker**.

```bash
# 1. Brokers (necessários só para as abas MQTT e AMQP)
docker compose -f infra/docker-compose.yml --env-file infra/.env up -d

# 2. Gateway — sobe os servidores UDP, QUIC e CoAP em processo
dotnet run --project backend/Gateway

# 3. Frontend
cd frontend && npm install && npm run dev
```

Abra o endereço que o Vite imprimir (normalmente <http://localhost:5173>).

As abas de **UDP, QUIC e CoAP funcionam sem Docker** — os servidores desses protocolos vivem dentro do
gateway. Se os brokers não estiverem no ar, as abas MQTT e AMQP mostram um aviso com o comando a rodar,
e o gateway reconecta sozinho assim que eles subirem.

### Portas

| Porta | Serviço | Observação |
|------:|---------|------------|
| 5080 | Gateway (HTTP) | REST + hub SignalR em `/hub/events` |
| 5443 | Gateway (HTTPS + **HTTP/3**) | Só se existir certificado de desenvolvimento |
| 5001 | Servidor UDP | Em processo |
| 5002 | Listener QUIC | Em processo, ALPN `protocol-lab` |
| 5683 | Servidor CoAP | Em processo, porta canônica do CoAP |
| 1883 | Mosquitto (MQTT/TCP) | Docker |
| 9883 | Mosquitto (MQTT/WebSocket) | Docker — deslocado da canônica 9001 |
| 5673 | RabbitMQ (AMQP) | Docker — deslocado da canônica 5672 |
| 15673 | RabbitMQ (UI de gestão) | `guest` / `guest` |

As portas do RabbitMQ e do MQTT-over-WebSocket foram deslocadas de propósito, para o laboratório poder
subir ao lado de outros containers que você já tenha rodando. Ajuste em [`infra/.env`](infra/.env).

### HTTP/3

O endpoint HTTPS só é criado se o gateway encontrar o certificado de desenvolvimento do ASP.NET Core.
Para habilitá-lo:

```bash
dotnet dev-certs https --trust
```

Sem ele, tudo o mais continua funcionando; o gateway apenas registra um aviso no startup. Isso é
deliberado — **HTTP/3 é QUIC, e não existe QUIC sem TLS 1.3**, nem em `localhost`.

---

## O que cada aba demonstra

### UDP — *datagramas sem promessa alguma*
Uma rajada de leituras numeradas, com perda e reordenação simuláveis por slider. O servidor compara os
números de sequência e traduz a diferença em linguagem de rede: buraco = perda, retrocesso = reordenação,
repetição = duplicata. **O cliente não recebe erro algum** pelos pacotes que somem — é o ponto inteiro.
Um segundo painel mostra os dois limites concretos de tamanho: fragmentação IP acima de 1472 bytes,
e `SocketError.MessageSize` acima de 65.507.

### QUIC — *transporte com TLS embutido e streams independentes*
Abre uma conexão e dispara N streams em paralelo. O primeiro é lento de propósito. Os outros respondem
em milissegundos, sem esperar por ele: é o head-of-line blocking do TCP sendo eliminado, medido no relógio.
Repare nos ids dos streams — 0, 4, 8, 12 — os dois bits menos significativos codificam quem abriu e se é
bidirecional.

### MQTT — *pub/sub leve, desenhado para redes ruins*
Publica nos três níveis de QoS e mostra a latência de cada um: QoS 0 não espera nada, QoS 1 espera o
PUBACK, QoS 2 faz um handshake de quatro etapas. Mensagens retidas, limpeza de retained (que só se faz
publicando payload vazio), e o Last Will and Testament — o broker agindo como testemunha da morte do
dispositivo.

### AMQP 0-9-1 — *roteamento decidido pelo consumidor*
O publicador não escolhe a fila: publica num exchange `topic` com uma routing key, e o exchange consulta
seus bindings. Publisher confirms, mensagens persistentes, ack manual, prefetch. Um botão publica uma
mensagem venenosa e você acompanha o caminho completo até a dead-letter queue — e outro drena a DLQ.

> ⚠️ **AMQP 0-9-1 e AMQP 1.0 são protocolos diferentes que compartilham o nome.** O 0-9-1 é o do RabbitMQ,
> e é o que este projeto demonstra. O 1.0 é um padrão OASIS/ISO com outro wire format, usado por Azure
> Service Bus e ActiveMQ Artemis. Um cliente de um não fala com um broker do outro.

### CoAP — *REST para dispositivos que contam bytes*
O modelo mental do HTTP comprimido num cabeçalho binário de 4 bytes, sobre UDP. Requisições confirmáveis
(CON) com retransmissão exponencial, não-confirmáveis (NON) descartáveis, respostas piggybacked dentro
do ACK, descoberta de recursos em `/.well-known/core`, e o padrão **Observe** — pub/sub direto entre
cliente e servidor, sem broker para provisionar, escalar e pagar.

---

## Comparação

| | UDP | QUIC | MQTT | AMQP 0-9-1 | CoAP |
|---|---|---|---|---|---|
| **Padrão** | RFC 768 | RFC 9000 | OASIS MQTT 5.0 | AMQP 0-9-1 | RFC 7252 |
| **Transporte** | IP, sem conexão | UDP, com conexão | TCP | TCP | UDP |
| **Topologia** | ponto-a-ponto | ponto-a-ponto | broker (pub/sub) | broker (exchange/fila) | cliente/servidor |
| **Entrega** | no máximo uma vez | confiável por stream | QoS 0, 1 ou 2 | at-least-once + ack | CON ou NON |
| **Ordenação** | nenhuma | por stream | por tópico | por fila | nenhuma |
| **Overhead** | 8 bytes | ~25–50 bytes + TLS | 2 bytes | ~8 bytes/frame | 4 bytes |

### Como escolher

- **Menor latência possível, e posso perder mensagens** → UDP. Se você começar a adicionar ACKs e
  retransmissão por cima, pare: está reescrevendo TCP, pior.
- **Confiabilidade e concorrência, na borda, com perda e mobilidade** → QUIC. No data center, HTTP/2 já resolve.
- **Muitos dispositivos publicando, consumidores desconhecidos, rede intermitente** → MQTT. Mas não é fila
  de trabalho: não há ack por consumidor nem dead-letter.
- **Distribuir trabalho entre workers, com retentativa e quarentena** → AMQP.
- **Dispositivo restrito, a bateria, onde cada byte custa energia** → CoAP.

---

## Estrutura

```
backend/
  ProtocolLab.slnx
  Directory.Build.props      # net10.0, nullable, warnings como erro
  Shared/                    # ProtocolEvent, TelemetryReading, IProtocolEventSink
  Udp/                       # servidor + cliente, detecção de perda/reordenação/duplicata
  Quic/                      # listener + cliente, certificado self-signed em memória
  Mqtt/                      # MQTTnet 5, QoS, retained, LWT, ack manual
  Amqp/                      # RabbitMQ.Client 7 (async), confirms, prefetch, DLQ
  Coap/                      # RFC 7252 escrito do zero sobre UdpClient, + Observe
  Gateway/                   # ASP.NET Core: minimal APIs, SignalR, HTTP/3

frontend/
  src/api/                   # cliente REST, tipos, hook do SignalR
  src/content/               # conteúdo didático por protocolo, separado dos componentes
  src/components/            # DocsPanel, EventLog, controles
  src/tabs/                  # uma aba por protocolo + visão geral

infra/
  docker-compose.yml         # Mosquitto + RabbitMQ
  mosquitto/mosquitto.conf
  .env                       # portas publicadas no host
```

### Decisões que valem explicação

**CoAP foi implementado à mão.** O ecossistema .NET para CoAP está estagnado — o pacote mais baixado não
recebe manutenção desde 2019, e os demais não trazem servidor. Num repositório didático, escrever o codec
do RFC 7252 sobre `UdpClient` elimina o risco de dependência morta e, principalmente, expõe justamente o
conteúdo que interessa: o formato binário. Veja [`CoapMessage.cs`](backend/Coap/CoapMessage.cs). Ficam de
fora transferência em blocos (RFC 7959), deduplicação por Message ID e DTLS — os três obrigatórios em
produção, e os três anotados no código.

**O sink de eventos nunca bloqueia.** [`ProtocolEventStream`](backend/Gateway/Realtime/ProtocolEventStream.cs)
deposita eventos num `Channel` limitado com `DropOldest` e um `BackgroundService` os drena para o SignalR.
Se o sink escrevesse direto no hub, a latência do WebSocket do usuário entraria no caminho quente do
consumidor MQTT e do laço de recepção UDP — um navegador lento em aba de fundo aplicaria contrapressão no
broker. Descartar telemetria de diagnóstico é sempre melhor do que degradar o sistema observado.

**O mesmo objeto serve como singleton e como hosted service.** `MqttDemoService` e `AmqpDemoService` são
registrados com `AddSingleton` e depois `AddHostedService(sp => sp.GetRequiredService<...>())`. Fazer
`AddHostedService<MqttDemoService>()` criaria uma segunda instância — e, com o mesmo client id, as duas
conexões MQTT se derrubariam mutuamente num laço infinito.

**CORS existe, mas o dev server não precisa dele.** O `vite.config.ts` encaminha `/api` e `/hub` ao gateway,
então em desenvolvimento o navegador fala com a própria origem. Defina `VITE_API_BASE` para falar direto
com o gateway e exercitar o caminho com CORS — que é o que valerá em produção, e que não pode usar
`AllowAnyOrigin` porque o SignalR envia credenciais no handshake.

---

## Segurança: o que este laboratório faz e você não deve copiar

Cada um destes pontos está anotado no código, no lugar onde acontece.

- **`RemoteCertificateValidationCallback = (_,_,_,_) => true`** no cliente QUIC. O servidor usa um
  certificado self-signed gerado em memória, então não há cadeia para validar. Em produção isto anula todo
  o TLS: um atacante em posição de man-in-the-middle apresenta o certificado dele e você o aceita. Linhas
  assim têm o hábito de sobreviver ao merge.
- **`allow_anonymous true`** no Mosquitto. Aceitável num broker efêmero preso a loopback. Há scanners
  públicos indexando brokers MQTT abertos na internet, com telemetria industrial legível.
- **`coap://` sem DTLS.** Tudo em claro, e UDP não protege contra spoofing de origem. Produção usa
  `coaps://` na 5684.
- **Servidor UDP sem rate limit.** Se uma requisição pequena gerasse uma resposta grande, o serviço viraria
  amplificador de DDoS. Aqui ele está preso a `127.0.0.1`.

---

## Referências

- [RFC 768](https://www.rfc-editor.org/rfc/rfc768) — User Datagram Protocol
- [RFC 9000](https://www.rfc-editor.org/rfc/rfc9000) — QUIC: A UDP-Based Multiplexed and Secure Transport
- [RFC 7252](https://www.rfc-editor.org/rfc/rfc7252) — The Constrained Application Protocol (CoAP)
- [RFC 7641](https://www.rfc-editor.org/rfc/rfc7641) — Observing Resources in CoAP
- [MQTT 5.0](https://docs.oasis-open.org/mqtt/mqtt/v5.0/mqtt-v5.0.html) — OASIS Standard
- [AMQP 0-9-1](https://www.rabbitmq.com/tutorials/amqp-concepts) — conceitos, na documentação do RabbitMQ
