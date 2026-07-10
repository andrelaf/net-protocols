using System.Buffers.Binary;
using System.Text;

namespace ProtocolLab.Coap;

/// <summary>Tipo de mensagem CoAP (2 bits no cabeçalho).</summary>
public enum CoapMessageType : byte
{
    /// <summary>Confirmable: exige ACK. É assim que o CoAP recupera a confiabilidade que o UDP não tem.</summary>
    Confirmable = 0,

    /// <summary>Non-confirmable: dispare e esqueça. Para telemetria de alta frequência onde perder uma amostra não importa.</summary>
    NonConfirmable = 1,

    /// <summary>Acknowledgement. Pode carregar a resposta junto (piggyback) ou vir vazio.</summary>
    Acknowledgement = 2,

    /// <summary>Reset: "não entendi" ou "não quero mais". Cancela um observe.</summary>
    Reset = 3
}

/// <summary>
/// Códigos CoAP. Um byte: 3 bits de classe, 5 bits de detalhe, escritos como <c>c.dd</c>.
/// A classe espelha o HTTP — 2.xx sucesso, 4.xx erro do cliente, 5.xx erro do servidor.
/// </summary>
public static class CoapCode
{
    public const byte Empty = 0x00;

    // Requisições (classe 0)
    public const byte Get = 0x01;    // 0.01
    public const byte Post = 0x02;   // 0.02
    public const byte Put = 0x03;    // 0.03
    public const byte Delete = 0x04; // 0.04

    // Respostas
    public const byte Created = 0x41;             // 2.01
    public const byte Changed = 0x44;             // 2.04
    public const byte Content = 0x45;             // 2.05
    public const byte BadRequest = 0x80;          // 4.00
    public const byte NotFound = 0x84;            // 4.04
    public const byte MethodNotAllowed = 0x85;    // 4.05
    public const byte InternalServerError = 0xA0; // 5.00

    /// <summary>Formata 0x45 como "2.05".</summary>
    public static string Format(byte code) => $"{code >> 5}.{code & 0x1F:D2}";

    public static string Describe(byte code) => code switch
    {
        Get => "GET",
        Post => "POST",
        Put => "PUT",
        Delete => "DELETE",
        Content => "2.05 Content",
        Created => "2.01 Created",
        Changed => "2.04 Changed",
        BadRequest => "4.00 Bad Request",
        NotFound => "4.04 Not Found",
        MethodNotAllowed => "4.05 Method Not Allowed",
        InternalServerError => "5.00 Internal Server Error",
        Empty => "0.00 Empty",
        _ => Format(code)
    };
}

/// <summary>Números de opção usados por esta demonstração (RFC 7252 §12.2 e RFC 7641).</summary>
public static class CoapOptionNumber
{
    public const int Observe = 6;        // RFC 7641
    public const int UriPath = 11;       // repetível: um segmento por ocorrência
    public const int ContentFormat = 12;
    public const int UriQuery = 15;
}

/// <summary>Content-Format é um inteiro, não uma string MIME. Economia de bytes levada a sério.</summary>
public static class CoapContentFormat
{
    public const int TextPlain = 0;
    public const int LinkFormat = 40;  // application/link-format, usado em /.well-known/core
    public const int Json = 50;        // application/json
    public const int Cbor = 60;        // application/cbor — o que produção usaria de verdade
}

public sealed record CoapOption(int Number, byte[] Value)
{
    public string AsString() => Encoding.UTF8.GetString(Value);

    public uint AsUInt()
    {
        uint result = 0;
        foreach (var b in Value)
        {
            result = (result << 8) | b;
        }
        return result;
    }

    public static CoapOption FromUInt(int number, uint value)
    {
        // Inteiros CoAP usam o menor número de bytes possível; zero é o array vazio.
        if (value == 0)
        {
            return new CoapOption(number, []);
        }

        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);

        var start = 0;
        while (start < 3 && buffer[start] == 0)
        {
            start++;
        }

        return new CoapOption(number, buffer[start..].ToArray());
    }
}

/// <summary>
/// Uma mensagem CoAP.
///
/// <para>
/// O cabeçalho tem <b>4 bytes fixos</b>. Compare com HTTP/1.1, onde só a linha
/// <c>GET /telemetry HTTP/1.1\r\nHost: …\r\n</c> já passa de 40 bytes de texto. Num sensor
/// alimentado por bateria transmitindo por rádio, cada byte é energia: essa diferença é a
/// razão de ser do protocolo.
/// </para>
///
/// <code>
///  0                   1                   2                   3
///  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// |Ver| T |  TKL  |      Code     |          Message ID           |
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// |   Token (0 a 8 bytes) ...
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// |   Options ...
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// |1 1 1 1 1 1 1 1|    Payload ...
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// </code>
///
/// <para>
/// <b>Message ID vs Token — a confusão mais comum do CoAP.</b> O Message ID (16 bits) casa
/// um ACK com o CON que o originou: é a camada de <i>mensagens</i>. O Token casa uma resposta
/// com a requisição que a pediu: é a camada de <i>requisição/resposta</i>. Numa resposta
/// separada (o servidor demora e responde depois, num CON novo), o Message ID é diferente do
/// pedido original — só o Token permite correlacionar.
/// </para>
/// </summary>
public sealed record CoapMessage
{
    public const int Version = 1;
    public const byte PayloadMarker = 0xFF;
    public const int MaxTokenLength = 8;

    public required CoapMessageType Type { get; init; }
    public required byte Code { get; init; }
    public required ushort MessageId { get; init; }
    public byte[] Token { get; init; } = [];
    public IReadOnlyList<CoapOption> Options { get; init; } = [];
    public byte[] Payload { get; init; } = [];

    public string TokenHex => Convert.ToHexString(Token);

    public string UriPath => string.Join('/', Options
        .Where(o => o.Number == CoapOptionNumber.UriPath)
        .Select(o => o.AsString()));

    public CoapOption? GetOption(int number) => Options.FirstOrDefault(o => o.Number == number);

    public string PayloadAsString() => Encoding.UTF8.GetString(Payload);

    /// <summary>Serializa no formato do fio.</summary>
    public byte[] Encode()
    {
        if (Token.Length > MaxTokenLength)
        {
            throw new InvalidOperationException($"Token de {Token.Length} bytes excede o máximo de {MaxTokenLength}.");
        }

        var writer = new ArrayBufferWriterLite(64);

        writer.WriteByte((byte)((Version << 6) | ((byte)Type << 4) | Token.Length));
        writer.WriteByte(Code);
        writer.WriteUInt16BigEndian(MessageId);
        writer.Write(Token);

        // Opções vão em ordem crescente de número, e cada uma codifica apenas o *delta*
        // em relação à anterior. É por isso que a ordem importa e a lista precisa ser ordenada.
        var lastNumber = 0;
        foreach (var option in Options.OrderBy(o => o.Number))
        {
            var delta = option.Number - lastNumber;
            lastNumber = option.Number;

            var (deltaNibble, deltaExt) = EncodeNibble(delta);
            var (lengthNibble, lengthExt) = EncodeNibble(option.Value.Length);

            writer.WriteByte((byte)((deltaNibble << 4) | lengthNibble));
            writer.Write(deltaExt);
            writer.Write(lengthExt);
            writer.Write(option.Value);
        }

        if (Payload.Length > 0)
        {
            writer.WriteByte(PayloadMarker);
            writer.Write(Payload);
        }

        return writer.ToArray();
    }

    /// <summary>Desserializa do formato do fio. Lança <see cref="FormatException"/> em dados inválidos.</summary>
    public static CoapMessage Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            throw new FormatException("Mensagem CoAP menor que o cabeçalho de 4 bytes.");
        }

        var version = data[0] >> 6;
        if (version != Version)
        {
            throw new FormatException($"Versão CoAP não suportada: {version}.");
        }

        var type = (CoapMessageType)((data[0] >> 4) & 0b11);
        var tokenLength = data[0] & 0x0F;
        if (tokenLength > MaxTokenLength)
        {
            throw new FormatException($"TKL inválido: {tokenLength}.");
        }

        var code = data[1];
        var messageId = BinaryPrimitives.ReadUInt16BigEndian(data[2..4]);

        var offset = 4;
        if (data.Length < offset + tokenLength)
        {
            throw new FormatException("Mensagem truncada no token.");
        }

        var token = data.Slice(offset, tokenLength).ToArray();
        offset += tokenLength;

        var options = new List<CoapOption>();
        var currentNumber = 0;

        while (offset < data.Length && data[offset] != PayloadMarker)
        {
            var head = data[offset++];
            var deltaNibble = head >> 4;
            var lengthNibble = head & 0x0F;

            if (deltaNibble == 15 || lengthNibble == 15)
            {
                throw new FormatException("Nibble 15 é reservado fora do marcador de payload.");
            }

            var delta = DecodeNibble(deltaNibble, data, ref offset);
            var length = DecodeNibble(lengthNibble, data, ref offset);

            if (offset + length > data.Length)
            {
                throw new FormatException("Mensagem truncada no valor de uma opção.");
            }

            currentNumber += delta;
            options.Add(new CoapOption(currentNumber, data.Slice(offset, length).ToArray()));
            offset += length;
        }

        byte[] payload = [];
        if (offset < data.Length && data[offset] == PayloadMarker)
        {
            offset++;
            payload = data[offset..].ToArray();
        }

        return new CoapMessage
        {
            Type = type,
            Code = code,
            MessageId = messageId,
            Token = token,
            Options = options,
            Payload = payload
        };
    }

    /// <summary>
    /// Valores 0–12 cabem no próprio nibble. 13 significa "mais 1 byte, some 13";
    /// 14 significa "mais 2 bytes, some 269". 15 é reservado.
    /// </summary>
    private static (int Nibble, byte[] Extended) EncodeNibble(int value) => value switch
    {
        < 13 => (value, []),
        < 269 => (13, [(byte)(value - 13)]),
        _ => (14, BitConverter.GetBytes((ushort)(value - 269)).Reverse().ToArray())
    };

    private static int DecodeNibble(int nibble, ReadOnlySpan<byte> data, ref int offset) => nibble switch
    {
        13 when offset < data.Length => data[offset++] + 13,
        14 when offset + 1 < data.Length => ReadUInt16(data, ref offset) + 269,
        13 or 14 => throw new FormatException("Mensagem truncada no nibble estendido."),
        _ => nibble
    };

    private static int ReadUInt16(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
        offset += 2;
        return value;
    }
}

/// <summary>Buffer de escrita mínimo, para manter o codec livre de dependências.</summary>
internal sealed class ArrayBufferWriterLite(int capacity)
{
    private byte[] _buffer = new byte[capacity];
    private int _length;

    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_length++] = value;
    }

    public void WriteUInt16BigEndian(ushort value)
    {
        EnsureCapacity(2);
        BinaryPrimitives.WriteUInt16BigEndian(_buffer.AsSpan(_length), value);
        _length += 2;
    }

    public void Write(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
        {
            return;
        }

        EnsureCapacity(value.Length);
        value.CopyTo(_buffer.AsSpan(_length));
        _length += value.Length;
    }

    public byte[] ToArray() => _buffer.AsSpan(0, _length).ToArray();

    private void EnsureCapacity(int extra)
    {
        if (_length + extra <= _buffer.Length)
        {
            return;
        }

        Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _length + extra));
    }
}
