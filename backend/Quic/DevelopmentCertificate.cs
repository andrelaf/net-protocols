using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ProtocolLab.Quic;

/// <summary>
/// Gera um certificado self-signed em memória para o listener QUIC.
///
/// <para>
/// <b>Por que isso existe:</b> QUIC embute TLS 1.3 no próprio handshake. Não há QUIC em
/// texto claro — nem para <c>localhost</c>, nem em desenvolvimento. Enquanto um servidor
/// TCP se levanta com um <c>bind()</c>, um servidor QUIC exige um certificado antes de
/// aceitar o primeiro pacote.
/// </para>
///
/// <para>
/// <b>Isto é código de laboratório.</b> Em produção o certificado vem de uma CA e o cliente
/// valida a cadeia. Aqui o cliente aceita qualquer certificado
/// (ver <c>RemoteCertificateValidationCallback</c> em <see cref="QuicEchoClient"/>), o que é
/// exatamente o antipattern que documentamos na aba QUIC do frontend: desabilitar validação
/// de certificado "só para testar" e esquecer a linha lá.
/// </para>
/// </summary>
internal static class DevelopmentCertificate
{
    public static X509Certificate2 CreateSelfSigned(string commonName = "protocol-lab-quic")
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, critical: true));

        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            critical: true));

        // 1.3.6.1.5.5.7.3.1 = serverAuth. Sem esta EKU, o TLS 1.3 do msquic recusa o certificado.
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")],
            critical: true));

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());

        using var ephemeral = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        // Exporta e reimporta como PKCS#12. No Windows, a chave privada de um certificado
        // recém-criado vive apenas em memória gerenciada, e o Schannel/msquic não a enxerga:
        // a ida e volta pelo PFX associa a chave a um provedor CNG que o TLS consegue usar.
        return X509CertificateLoader.LoadPkcs12(
            ephemeral.Export(X509ContentType.Pfx),
            password: null,
            keyStorageFlags: X509KeyStorageFlags.Exportable);
    }
}
