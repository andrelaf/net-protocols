import type { ProtocolKind } from '../api/types';

export interface DocEntry {
  title: string;
  body: string;
}

export interface ProtocolDoc {
  name: string;
  tagline: string;
  rfc: string;
  transport: string;
  delivery: string;
  ordering: string;
  overhead: string;
  topology: string;
  /** Resumo em prosa: o que o protocolo é e por que ele existe. */
  essence: string;
  useCases: DocEntry[];
  bestPractices: DocEntry[];
  antipatterns: DocEntry[];
  /** Quando escolher outra coisa. Tão importante quanto saber quando usar. */
  avoidWhen: string;
}

export const protocolDocs: Record<Exclude<ProtocolKind, 'Gateway'>, ProtocolDoc> = {
  Udp: {
    name: 'UDP',
    tagline: 'Datagramas sem promessa alguma',
    rfc: 'RFC 768 (1980)',
    transport: 'IP, sem conexão',
    delivery: 'No máximo uma vez — e possivelmente nenhuma',
    ordering: 'Nenhuma garantia',
    overhead: '8 bytes de cabeçalho',
    topology: 'Ponto-a-ponto, multicast ou broadcast',
    essence:
      'UDP não é "TCP sem as partes boas". Ele é a interface crua do IP exposta à aplicação: um cabeçalho de 8 bytes com portas e checksum, e nada mais. Não há handshake, conexão, retransmissão, controle de fluxo ou de congestionamento. O que você ganha em troca é controle: quando a aplicação sabe mais sobre seus próprios dados do que um algoritmo genérico poderia saber, entregar essa decisão ao kernel é desperdício. Um quadro de vídeo atrasado é inútil — retransmiti-lo piora a chamada em vez de melhorá-la. É por isso que QUIC, DNS, DHCP, NTP, WebRTC e praticamente todo jogo online são construídos sobre UDP: não porque abrem mão da confiabilidade, mas porque querem definir eles mesmos o que "confiável" significa.',
    useCases: [
      {
        title: 'Mídia em tempo real (VoIP, videoconferência, jogos)',
        body: 'Latência importa mais que integridade. Um pacote de áudio que chega 300ms atrasado já não tem onde ser tocado. Melhor descartá-lo e interpolar do que travar o fluxo esperando a retransmissão — que é exatamente o que o TCP faria.',
      },
      {
        title: 'Descoberta de serviços e transações curtas (DNS, DHCP, NTP, mDNS)',
        body: 'Uma consulta DNS cabe num datagrama e a resposta em outro. Um handshake TCP de três vias custaria mais round-trips do que a própria transação. Se a resposta não vier, repetir a pergunta é trivial e barato.',
      },
      {
        title: 'Telemetria de alto volume e baixo valor unitário',
        body: 'Métricas de infraestrutura (o protocolo do StatsD é UDP) e leituras de sensores densas no tempo. Perder uma amostra entre milhares não altera a média, e o custo de garantir cada uma delas não se paga.',
      },
      {
        title: 'Base para protocolos que constroem a própria semântica',
        body: 'QUIC, CoAP, WireGuard e QUIC-sobre-HTTP/3 rodam sobre UDP. Todos reimplementam confiabilidade — mas cada um do seu jeito, adaptado ao seu problema. Este é o uso mais importante do UDP hoje.',
      },
    ],
    bestPractices: [
      {
        title: 'Mantenha o payload abaixo de 1472 bytes',
        body: '1500 (MTU Ethernet) − 20 (cabeçalho IP) − 8 (cabeçalho UDP) = 1472. Acima disso o IP fragmenta, e perder um único fragmento descarta o datagrama inteiro. Com 4 fragmentos e 1% de perda por pacote, a perda efetiva do datagrama sobe para ~4%. Para atravessar túneis e VPNs com segurança, muitos protocolos usam 1200 bytes.',
      },
      {
        title: 'Numere suas mensagens',
        body: 'Sem número de sequência você não consegue distinguir perda de reordenação, nem detectar duplicatas. O botão de rajada desta aba só consegue mostrar o que aconteceu porque cada leitura carrega um contador. Em TCP essa contabilidade existe no kernel e você nunca a vê; em UDP, se quiser, precisa construir.',
      },
      {
        title: 'Torne o consumidor idempotente',
        body: 'UDP permite entrega duplicada. Processar a mesma leitura duas vezes precisa ser inofensivo. Se não for, você precisa de deduplicação por id — e aí vale perguntar se o protocolo certo não seria outro.',
      },
      {
        title: 'Reutilize um único socket',
        body: 'Criar um `UdpClient` por mensagem consome descritores e custa um bind a cada envio. É o mesmo erro de instanciar `HttpClient` em laço. Um socket por processo, vivo enquanto o processo viver.',
      },
      {
        title: 'No Windows, desative SIO_UDP_CONNRESET',
        body: 'Um ICMP "port unreachable" recebido em resposta a um envio anterior faz o próximo `ReceiveAsync` lançar `SocketException`. Um servidor UDP não pode morrer porque um cliente sumiu. Veja `UdpTelemetryServer.DisableConnectionResetOnWindows`.',
      },
    ],
    antipatterns: [
      {
        title: 'Reimplementar TCP por cima de UDP',
        body: 'Se você acabou adicionando ACKs, retransmissão, janela deslizante e controle de congestionamento, você escreveu um TCP pior — com menos anos de tuning e sem offload de hardware. Use TCP, ou use QUIC, que já fez esse trabalho e fez melhor.',
      },
      {
        title: 'Assumir que `SendAsync` completar significa entrega',
        body: 'Ele significa apenas que o kernel aceitou os bytes. O datagrama pode ser descartado no primeiro roteador. Nenhuma exceção será lançada. Este é o mal-entendido mais comum sobre UDP, e a aba demonstra: pacotes descartados na simulação de perda não geram erro algum no cliente.',
      },
      {
        title: 'Expor um serviço UDP sem validação de origem nem rate limit',
        body: 'UDP não valida o endereço de origem: qualquer um pode forjá-lo. Se uma requisição pequena gera uma resposta grande, seu serviço vira amplificador de DDoS — a vítima recebe o tráfego, você paga a banda. Foi assim com DNS, NTP e memcached. Se a resposta for maior que a pergunta, exija prova de posse do endereço antes de respondê-la.',
      },
      {
        title: 'Desserializar direto o que chegou no fio',
        body: 'Qualquer host pode enviar qualquer coisa para uma porta UDP aberta. Valide tamanho e formato antes de passar o buffer ao desserializador.',
      },
    ],
    avoidWhen:
      'Quando a perda de uma única mensagem tem custo real e não há como reconstruí-la: transações financeiras, comandos de escrita, transferência de arquivos. Se você se pegar pensando "mas eu preciso garantir que chegue", o protocolo já respondeu que não é UDP.',
  },

  Quic: {
    name: 'QUIC',
    tagline: 'Transporte com TLS embutido e streams independentes',
    rfc: 'RFC 9000 / 9001 / 9002 (2021)',
    transport: 'UDP, com conexão',
    delivery: 'Confiável e ordenada por stream',
    ordering: 'Ordenada dentro do stream; independente entre streams',
    overhead: '~25–50 bytes por pacote; TLS 1.3 obrigatório',
    topology: 'Ponto-a-ponto',
    essence:
      'QUIC resolve um problema que o TCP não tem como resolver: em TCP, os bytes de uma conexão formam um único fluxo ordenado, então um pacote perdido bloqueia a entrega de tudo o que veio depois — mesmo de requisições HTTP completamente independentes multiplexadas na mesma conexão. Isso é o head-of-line blocking, e foi o que impediu o HTTP/2 de cumprir sua promessa em redes com perda. QUIC move o transporte para o espaço de usuário, sobre UDP, e dá a cada stream seu próprio controle de fluxo e sua própria ordenação. Perder um pacote afeta um stream, não a conexão. De quebra, funde o handshake de transporte com o de TLS 1.3 (uma viagem de ida e volta em vez de duas), e identifica a conexão por um Connection ID em vez da quádrupla IP/porta — de modo que trocar de Wi-Fi para 5G não derruba o download.',
    useCases: [
      {
        title: 'HTTP/3',
        body: 'É o motivo pelo qual o QUIC existe. Todo navegador moderno o usa, e é o que o Kestrel expõe na porta 5443 deste laboratório quando há certificado disponível.',
      },
      {
        title: 'Clientes móveis que trocam de rede',
        body: 'A migração de conexão mantém a sessão viva ao sair do Wi-Fi para a rede celular. Em TCP, a mudança de IP mata a conexão e força handshake e TLS do zero.',
      },
      {
        title: 'Transferência de múltiplos recursos concorrentes',
        body: 'Vários downloads independentes numa conexão só. Um deles ficar lento ou perder pacotes não segura os outros. É o experimento do botão desta aba.',
      },
      {
        title: 'gRPC sobre HTTP/3, RPC de baixa latência',
        body: 'Handshake mais curto e ausência de head-of-line blocking beneficiam diretamente cargas RPC com muitas chamadas concorrentes e mensagens pequenas.',
      },
    ],
    bestPractices: [
      {
        title: 'Verifique `QuicListener.IsSupported` antes de usar',
        body: 'QUIC depende do msquic: embutido no runtime .NET no Windows 11 e Server 2022+, mas exige o pacote `libmsquic` no Linux. Sem essa verificação, sua aplicação estoura no startup em metade dos ambientes.',
      },
      {
        title: 'Um stream por unidade lógica de trabalho',
        body: 'Streams são baratíssimos: abrir um não custa round-trip algum, é só um id novo dentro de uma conexão existente. Multiplexar duas requisições no mesmo stream reintroduz manualmente o head-of-line blocking que você veio evitar.',
      },
      {
        title: 'Processe streams em paralelo no servidor',
        body: 'Um laço `AcceptInboundStreamAsync` que processa um stream por vez transforma o servidor no gargalo — o transporte é paralelo, o seu código não. Veja `QuicEchoServer.HandleConnectionAsync`.',
      },
      {
        title: 'Encerre a escrita com `CompleteWrites()`',
        body: 'É o equivalente ao meio-fechamento do TCP: sinaliza fim de mensagem sem fechar o stream para leitura. Sem isso, o par fica bloqueado esperando bytes que nunca virão.',
      },
      {
        title: 'ALPN é obrigatório e precisa bater exatamente',
        body: 'Não existe conexão QUIC sem negociar um protocolo de aplicação. Se cliente e servidor anunciarem strings diferentes, o handshake falha com um erro que não menciona ALPN.',
      },
    ],
    antipatterns: [
      {
        title: 'Aceitar qualquer certificado para "só testar"',
        body: 'Este projeto faz exatamente isso — `RemoteCertificateValidationCallback = (_,_,_,_) => true` em `QuicEchoClient` — porque o servidor usa um self-signed gerado em memória. Em produção essa linha anula todo o TLS: um atacante em posição de man-in-the-middle apresenta o certificado dele e você o aceita alegremente. Linhas assim têm o hábito de sobreviver ao merge.',
      },
      {
        title: 'Tratar QUIC como "UDP rápido"',
        body: 'QUIC tem conexão, handshake, controle de congestionamento e criptografia obrigatória. Ele é mais próximo de TCP+TLS do que de UDP. Se você queria fire-and-forget, o QUIC vai cobrar por garantias que você não pediu.',
      },
      {
        title: 'Abrir uma conexão nova por requisição',
        body: 'O handshake, mesmo sendo de um round-trip, é caro comparado a abrir um stream, que custa zero. Reúse a conexão e abra streams. É a mesma lógica de reusar `HttpClient`.',
      },
      {
        title: 'Esperar que firewalls corporativos deixem passar',
        body: 'Muita rede corporativa bloqueia UDP na porta 443 por padrão, e QUIC é indistinguível de outro tráfego UDP para um middlebox. Tenha sempre um caminho de fallback para TCP.',
      },
    ],
    avoidWhen:
      'Em comunicação interna entre serviços na mesma rede confiável e de baixa perda, o ganho sobre HTTP/2 é marginal e o custo operacional (msquic, certificados, observabilidade imatura) é real. QUIC brilha onde há perda, latência e mobilidade — na borda, não no data center.',
  },

  Mqtt: {
    name: 'MQTT',
    tagline: 'Pub/sub leve, desenhado para redes ruins',
    rfc: 'OASIS MQTT 5.0 (2019); ISO/IEC 20922',
    transport: 'TCP (ou WebSocket)',
    delivery: 'QoS 0, 1 ou 2 — escolhido por mensagem',
    ordering: 'Por tópico, dentro da sessão',
    overhead: '2 bytes de cabeçalho fixo',
    topology: 'Pub/sub mediado por broker',
    essence:
      'MQTT nasceu em 1999 para monitorar oleodutos por satélite, e cada decisão de projeto vem dessa origem: cabeçalho de 2 bytes, keep-alive configurável, e um testamento que o broker publica quando o dispositivo morre sem se despedir. O modelo é pub/sub puro: publicador e assinante nunca se conhecem, e o único acoplamento entre eles é a string do tópico. Isso torna trivial acrescentar um consumidor novo — e torna a hierarquia de tópicos um contrato tão rígido quanto uma assinatura de método, só que sem compilador para protegê-lo. O que MQTT oferece e quase nenhum outro protocolo oferece é a escolha de garantia por mensagem: a mesma conexão carrega telemetria descartável em QoS 0 e comandos críticos em QoS 2.',
    useCases: [
      {
        title: 'IoT sobre redes intermitentes ou caras',
        body: 'Celular, satélite, NB-IoT. Sessões persistentes fazem o broker guardar mensagens QoS 1/2 enquanto o dispositivo está fora do ar, e reentregá-las na reconexão. O dispositivo não perde comandos por ter ficado sem sinal.',
      },
      {
        title: 'Telemetria de muitos-para-um e comandos de um-para-muitos',
        body: 'Milhares de sensores publicam em `lab/telemetry/<device>`; um serviço assina `lab/telemetry/+` e recebe todos. No sentido inverso, uma publicação em `commands/all` alcança a frota inteira.',
      },
      {
        title: 'Estado corrente com mensagens retidas',
        body: 'O broker guarda a última mensagem retida de cada tópico e a entrega imediatamente a qualquer novo assinante. Um painel que acaba de abrir vê o estado atual de cada sensor sem esperar a próxima leitura.',
      },
      {
        title: 'Presença de dispositivos com Last Will and Testament',
        body: 'O dispositivo registra, no CONNECT, a mensagem que o broker deve publicar se a conexão cair sem DISCONNECT limpo. É a forma de um dispositivo anunciar a própria morte — algo que HTTP simplesmente não tem.',
      },
    ],
    bestPractices: [
      {
        title: 'Trate a hierarquia de tópicos como API pública',
        body: 'Vá do geral ao específico: `empresa/site/setor/dispositivo/medida`. Nunca coloque dados variáveis onde deveria haver estrutura. Renomear um tópico depois que a frota está em campo é uma migração, não um refactor.',
      },
      {
        title: 'Escolha o QoS por mensagem, não por aplicação',
        body: 'QoS 0 não tem confirmação. QoS 1 custa um round-trip e pode duplicar. QoS 2 custa dois round-trips e não duplica. Compare os tempos no painel de resultados desta aba: a diferença é medível. Use QoS 2 só quando duplicata for inaceitável e o consumidor não puder deduplicar sozinho.',
      },
      {
        title: 'Confirme depois de processar, não ao receber',
        body: 'Com `AutoAcknowledge = false`, o PUBACK sai só quando o processamento terminou. Confirmar cedo transforma QoS 1 em QoS 0 disfarçado: se o processo morrer entre o ack e o processamento, a mensagem se perde e ninguém percebe. Veja `MqttDemoService.OnMessageReceivedAsync`.',
      },
      {
        title: 'Sempre registre um Last Will, e anuncie "online" ao conectar',
        body: 'Um DISCONNECT limpo faz o broker descartar o testamento — então quem quer anunciar a saída precisa publicá-la explicitamente antes de sair. As duas metades são necessárias.',
      },
      {
        title: 'Client id único por instância',
        body: 'O client id identifica a sessão, não a conexão. Duas instâncias com o mesmo id se expulsam mutuamente num laço infinito de reconexão.',
      },
    ],
    antipatterns: [
      {
        title: 'Assinar `#` na raiz',
        body: 'Você acaba de pedir ao broker cada mensagem de cada dispositivo. Em produção isso derruba o seu consumidor, e às vezes o broker. Assine o ramo mais estreito que resolva o seu problema.',
      },
      {
        title: 'Usar MQTT como fila de trabalho',
        body: 'MQTT distribui cópias a todos os assinantes de um tópico; ele não distribui trabalho entre eles. Não há ack por consumidor, dead-letter, nem redistribuição em caso de falha. Isso é AMQP. (Shared subscriptions do MQTT 5 amenizam, mas não substituem.)',
      },
      {
        title: 'QoS 2 em tudo, por precaução',
        body: 'Dobra os round-trips e o estado que o broker precisa manter por mensagem, para resolver um problema — duplicatas — que um consumidor idempotente resolve de graça. Quase sempre a resposta certa é QoS 1 mais idempotência.',
      },
      {
        title: 'Esquecer que mensagens retidas não expiram',
        body: 'Uma leitura retida errada fica sendo entregue a todo novo assinante para sempre. A única forma de apagá-la é publicar payload vazio com retain ligado — não existe DELETE em MQTT. O botão "limpar retained" desta aba faz exatamente isso.',
      },
      {
        title: 'Broker sem autenticação exposto à rede',
        body: 'O `mosquitto.conf` deste laboratório usa `allow_anonymous true` porque está preso a loopback. Há scanners públicos indexando brokers MQTT abertos na internet, com telemetria industrial legível. Em produção: TLS na 8883, autenticação, e uma ACL que prenda cada dispositivo ao seu próprio ramo.',
      },
    ],
    avoidWhen:
      'Quando você precisa de roteamento decidido pelo consumidor, de distribuição de trabalho entre workers, ou de dead-letter queue. E quando não há dispositivos restritos envolvidos: entre dois serviços no mesmo data center, MQTT resolve um problema que você não tem.',
  },

  Amqp: {
    name: 'AMQP 0-9-1',
    tagline: 'Roteamento decidido pelo consumidor, entrega negociada',
    rfc: 'AMQP 0-9-1 (RabbitMQ)',
    transport: 'TCP',
    delivery: 'At-least-once com ack manual; exactly-once só com idempotência',
    ordering: 'Por fila, com um único consumidor',
    overhead: '~8 bytes por frame, mais propriedades',
    topology: 'Broker com exchanges, bindings e filas',
    essence:
      'A ideia central do AMQP 0-9-1, e o que o separa do MQTT, é que o publicador não escolhe o destino. Ele publica num exchange com uma routing key; o exchange consulta seus bindings e decide quais filas recebem cópias. Quem define o roteamento é o consumidor, ao declarar seu binding. Acrescentar um serviço de auditoria que escuta tudo não exige tocar em uma linha do publicador. O outro eixo é a entrega negociada: ack manual, publisher confirms, mensagens persistentes, prefetch e dead-lettering formam um vocabulário para dizer o que fazer quando o processamento falha — algo que o QoS do MQTT nem tenta cobrir. Cuidado com o nome: AMQP 1.0 é um padrão OASIS/ISO completamente diferente, com outro wire format, usado por Azure Service Bus e ActiveMQ Artemis. Mesma sigla, outro protocolo.',
    useCases: [
      {
        title: 'Filas de trabalho com concorrência entre workers',
        body: 'N consumidores na mesma fila dividem a carga. O prefetch controla quanto cada um recebe antes de confirmar, e é isso que faz o balanceamento funcionar.',
      },
      {
        title: 'Roteamento por tópico e fan-out',
        body: 'Um exchange `topic` roteia `telemetry.sensor-01` para quem assinou `telemetry.#`. Um `fanout` copia para todas as filas ligadas. O publicador não sabe quantas existem.',
      },
      {
        title: 'Desacoplamento temporal entre serviços',
        body: 'O consumidor pode estar em deploy, o produtor continua publicando. A fila absorve o pico. É a forma mais barata de tornar dois serviços independentes na disponibilidade.',
      },
      {
        title: 'Retentativa e quarentena de mensagens venenosas',
        body: 'Uma mensagem que sempre falha vai para a dead-letter queue e sai do caminho quente, ficando disponível para inspeção e reprocessamento. Nenhum outro protocolo aqui tem esse conceito nativo.',
      },
    ],
    bestPractices: [
      {
        title: 'Sempre configure prefetch',
        body: 'É o parâmetro mais importante e mais ignorado do AMQP. Sem `basic.qos`, o broker despeja a fila inteira no primeiro consumidor que conectar: a memória do processo explode e os outros consumidores ficam ociosos. Prefetch é o que faz balanceamento de carga existir.',
      },
      {
        title: 'Ack manual, depois do processamento',
        body: '`autoAck: true` dá a mensagem como entregue no instante em que ela sai do broker. Uma queda do consumidor durante o processamento a perde definitivamente.',
      },
      {
        title: 'Publisher confirms, sempre',
        body: 'Sem confirms, `BasicPublishAsync` retorna quando os bytes saem do socket. Uma queda do broker perde a mensagem em silêncio. Com confirms, o await só completa quando o broker garantiu a gravação.',
      },
      {
        title: 'Fila durável e mensagem persistente andam juntas',
        body: 'Fila durável sobrevive ao restart do broker; mensagem transiente, não. Declarar uma sem a outra dá a falsa sensação de durabilidade — e uma fila durável vazia depois do restart.',
      },
      {
        title: 'Um canal por thread; nunca compartilhe `IChannel`',
        body: '`IChannel` não é thread-safe. Compartilhá-lo entre o consumidor e requisições HTTP concorrentes causa erros de framing intermitentes, os piores de diagnosticar. Este projeto usa canais separados para publicar e consumir.',
      },
      {
        title: 'Publique com `mandatory: true`',
        body: 'Se nenhuma fila casar com a routing key, o broker devolve a mensagem via `basic.return` em vez de descartá-la em silêncio. Sem isso, um binding errado só é descoberto quando alguém pergunta por que os dados sumiram.',
      },
    ],
    antipatterns: [
      {
        title: 'Rejeitar com `requeue: true` numa mensagem que sempre falha',
        body: 'A mensagem volta ao início da fila, é entregue de novo, falha de novo. Um laço infinito que consome 100% de CPU e bloqueia as mensagens boas atrás dela. Sempre tenha uma DLQ e rejeite com `requeue: false`. O botão "publicar mensagem venenosa" desta aba mostra o caminho correto.',
      },
      {
        title: 'Confundir AMQP 0-9-1 com AMQP 1.0',
        body: 'São protocolos distintos que compartilham o nome. Um cliente 0-9-1 não fala com um broker que só entende 1.0. Se o destino é Azure Service Bus, você quer 1.0 — e uma biblioteca diferente.',
      },
      {
        title: 'Usar a fila como banco de dados',
        body: 'Filas são para trabalho em trânsito. Uma fila com milhões de mensagens paradas degrada a performance do broker inteiro, atrapalha o flow control e transforma um restart em incidente.',
      },
      {
        title: 'Abrir conexão por mensagem',
        body: 'Uma conexão AMQP é uma conexão TCP com handshake e autenticação. Ela deve durar a vida do processo. Canais são a unidade barata e descartável — e mesmo eles não devem ser criados em laço.',
      },
      {
        title: 'Esperar exactly-once do protocolo',
        body: 'AMQP entrega at-least-once. Redelivery acontece após reconexão, e a flag `redelivered` avisa. Exactly-once é propriedade do seu consumidor ser idempotente, não do broker.',
      },
    ],
    avoidWhen:
      'Em dispositivos restritos ou links caros: o handshake e o overhead por frame não se pagam num sensor a bateria. E quando você precisa de replay do histórico ou de múltiplos consumidores lendo o mesmo fluxo em ritmos diferentes — isso é log distribuído (Kafka), não fila.',
  },

  Coap: {
    name: 'CoAP',
    tagline: 'REST para dispositivos que contam bytes',
    rfc: 'RFC 7252 (2014); Observe: RFC 7641',
    transport: 'UDP (DTLS para coaps://)',
    delivery: 'CON com retransmissão, ou NON descartável',
    ordering: 'Nenhuma; Observe usa número de sequência',
    overhead: '4 bytes de cabeçalho',
    topology: 'Cliente/servidor, com pub/sub opcional via Observe',
    essence:
      'CoAP é o modelo mental do HTTP — métodos, códigos de resposta, URIs, content negotiation — comprimido num cabeçalho binário de 4 bytes e colocado sobre UDP. A troca não é gratuita, e é aí que está a elegância: como UDP não garante nada, o CoAP reimplementa apenas o que precisa, e deixa a escolha para a aplicação. Mensagens confirmáveis (CON) são retransmitidas com backoff exponencial até receberem ACK; não-confirmáveis (NON) somem sem aviso. A resposta comum vem dentro do próprio ACK (piggyback), então um request/response confiável custa dois pacotes. Para eventos, o padrão Observe transforma um GET numa assinatura: o servidor empurra atualizações direto ao cliente, sem broker no meio. É a resposta do CoAP ao MQTT — e a diferença é que aqui não há infraestrutura para operar.',
    useCases: [
      {
        title: 'Sensores e atuadores restritos',
        body: 'Microcontroladores com poucos KB de RAM, alimentados a bateria, em 6LoWPAN ou LoRa. Um cabeçalho de 4 bytes contra as centenas de bytes de texto do HTTP significa menos tempo de rádio ligado — e rádio é a maior fonte de consumo do dispositivo.',
      },
      {
        title: 'Descoberta de recursos',
        body: 'Um GET em `/.well-known/core` devolve os recursos disponíveis em `application/link-format`, marcando quais são observáveis. Tente pelo botão de discovery: é como um dispositivo CoAP se apresenta a um gateway que nunca o viu.',
      },
      {
        title: 'Observação de recursos sem broker',
        body: 'O cliente faz um GET com a opção Observe; o servidor guarda o token e passa a enviar notificações. Pub/sub direto entre as duas pontas, sem servidor intermediário para provisionar, escalar e pagar.',
      },
      {
        title: 'LwM2M, Thread e gestão de dispositivos',
        body: 'O OMA LwM2M, padrão de gerenciamento de dispositivos IoT, é construído sobre CoAP. Onde há CoAP, normalmente há uma pilha inteira em cima dele.',
      },
    ],
    bestPractices: [
      {
        title: 'Escolha CON ou NON conscientemente',
        body: 'CON para comandos e leituras que importam; NON para telemetria de alta frequência. CON custa retransmissões e estado; NON custa uma amostra perdida de vez em quando. Compare os dois botões desta aba com perda simulada ligada.',
      },
      {
        title: 'Use tokens imprevisíveis',
        body: 'UDP não protege contra spoofing de origem. Um atacante que adivinha o token consegue forjar respostas. Tokens aleatórios de 4 a 8 bytes, gerados por RNG criptográfico em produção.',
      },
      {
        title: 'Não use JSON no fio',
        body: 'Uma leitura em JSON ocupa ~130 bytes; em CBOR (RFC 8949), cerca de 40. Num MTU útil de 60–80 bytes do 6LoWPAN, isso é a diferença entre um pacote e uma transferência em blocos. Este laboratório usa JSON só para você conseguir ler o payload no navegador.',
      },
      {
        title: 'Respeite ACK_TIMEOUT e MAX_RETRANSMIT',
        body: '2 segundos iniciais, dobrando a cada tentativa, até 4 retransmissões. Um sensor a bateria não pode gastar rádio indefinidamente com um servidor que não responde: a desistência é uma feature.',
      },
      {
        title: 'Blockwise transfer para payloads grandes',
        body: 'Acima do MTU, o RFC 7959 fragmenta na camada CoAP em vez de deixar o IP fragmentar — assim, perder um bloco só custa aquele bloco. Não implementado aqui, mas obrigatório em produção.',
      },
    ],
    antipatterns: [
      {
        title: 'Tratar CoAP como "HTTP pela porta 5683"',
        body: 'Não há conexão, não há ordenação, e o Message ID não é o Token. O Message ID casa um ACK com o CON que o originou (camada de mensagens); o Token casa uma resposta com a requisição (camada de request/response). Numa resposta separada, os dois divergem — e quem correlaciona pelo Message ID quebra.',
      },
      {
        title: 'CON para tudo',
        body: 'Retransmissão custa energia e tempo de rádio, os dois recursos mais escassos do dispositivo. Telemetria periódica não precisa de garantia: a próxima amostra chega em segundos e substitui a perdida.',
      },
      {
        title: 'Ignorar deduplicação por Message ID no servidor',
        body: 'Um CON retransmitido chega duas vezes ao servidor quando o ACK original se perdeu. Se o GET tinha efeito colateral, ele acontece duas vezes. Servidores CoAP devem manter uma janela de Message IDs vistos — algo que este laboratório deliberadamente não faz, e que o comentário no código admite.',
      },
      {
        title: 'coap:// em produção',
        body: 'Sem DTLS, tudo trafega em claro e qualquer um pode forjar a origem. Use `coaps://` na 5684. A falta de DTLS aqui é uma escolha de laboratório, não um exemplo a copiar.',
      },
      {
        title: 'Deixar observadores acumularem',
        body: 'O servidor guarda estado por observador. Clientes que reiniciam sem cancelar deixam registros órfãos, e o servidor passa a enviar notificações a ninguém. Trate o RST de volta como cancelamento — é para isso que ele existe.',
      },
    ],
    avoidWhen:
      'Fora do mundo restrito. Entre serviços num data center, HTTP/2 tem melhor ferramental, observabilidade e bibliotecas maduras, e o ganho de bytes é irrelevante. CoAP se paga quando o byte custa energia.',
  },
};

/** Comparação lado a lado, exibida na aba de visão geral. */
export const comparisonRows = [
  { label: 'RFC / padrão', get: (d: ProtocolDoc) => d.rfc },
  { label: 'Transporte', get: (d: ProtocolDoc) => d.transport },
  { label: 'Topologia', get: (d: ProtocolDoc) => d.topology },
  { label: 'Garantia de entrega', get: (d: ProtocolDoc) => d.delivery },
  { label: 'Ordenação', get: (d: ProtocolDoc) => d.ordering },
  { label: 'Overhead', get: (d: ProtocolDoc) => d.overhead },
];
