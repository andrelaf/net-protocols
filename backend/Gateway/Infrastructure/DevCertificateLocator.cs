using System.Security.Cryptography.X509Certificates;

namespace ProtocolLab.Gateway.Infrastructure;

/// <summary>
/// Localiza o certificado de desenvolvimento do ASP.NET Core (<c>dotnet dev-certs https</c>).
///
/// <para>
/// Existe porque o endpoint HTTP/3 <b>exige</b> TLS: HTTP/3 roda sobre QUIC, e QUIC embute
/// TLS 1.3 obrigatoriamente. Se pedíssemos um listener HTTPS sem certificado, o Kestrel
/// lançaria durante o <c>Run()</c> — depois de todo o resto já ter subido. Preferimos
/// detectar antes, expor a aba de QUIC em modo degradado e dizer ao usuário o que rodar.
/// </para>
/// </summary>
internal static class DevCertificateLocator
{
    /// <summary>OID que a Microsoft usa para marcar o certificado de desenvolvimento.</summary>
    private const string AspNetCoreHttpsOid = "1.3.6.1.4.1.311.84.1.1";

    public static X509Certificate2? TryFind()
    {
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);

            var now = DateTime.Now;

            return store.Certificates
                .Where(certificate => certificate.HasPrivateKey)
                .Where(certificate => certificate.NotBefore <= now && certificate.NotAfter >= now)
                .Where(certificate => certificate.Extensions
                    .Any(extension => string.Equals(extension.Oid?.Value, AspNetCoreHttpsOid, StringComparison.Ordinal)))
                .OrderByDescending(certificate => certificate.NotAfter)
                .FirstOrDefault();
        }
        catch (Exception)
        {
            // Store indisponível (containers Linux mínimos, por exemplo). Seguimos só com HTTP.
            return null;
        }
    }
}
