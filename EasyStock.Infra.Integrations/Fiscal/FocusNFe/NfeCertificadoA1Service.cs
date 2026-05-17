using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Fiscal;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe;

/// <summary>
/// Implementacao de <see cref="INfeCertificadoA1Service"/> usando
/// <see cref="IDataProtectionProvider"/> do ASP.NET (Azure Key Vault binding ja
/// configurado em master). KEK rotacionavel via purposes do data protection —
/// <c>kekId</c> do payload mapeia para purpose distinto, permitindo
/// re-cifragem sem perda de acesso ao payload antigo.
///
/// <para>
/// <b>Seguranca:</b> bytes do .pfx + senha NUNCA logados. <see cref="CertificadoA1Decifrado"/>
/// retornado deve ser usado pelo caller no escopo imediato e descartado (records
/// em .NET sao stack-friendly mas o byte[] interno e heap — caller deve evitar
/// passar adiante).
/// </para>
///
/// <para>
/// <b>Algoritmo:</b> AES-256-GCM via <see cref="IDataProtector"/>. Iv/Tag/payload
/// armazenados em <see cref="CertificadoA1CredencialDto"/>; .Unprotect concatena
/// para validar.
/// </para>
/// </summary>
public sealed class NfeCertificadoA1Service(
    IDataProtectionProvider dataProtection,
    ILogger<NfeCertificadoA1Service> logger) : INfeCertificadoA1Service
{
    private const string PurposePrefix = "EasyStock.Fiscal.NfeCertificadoA1.v1";

    public Task<CertificadoA1Decifrado> DecifrarAsync(CertificadoA1CredencialDto credencial, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(credencial);
        if (credencial.PayloadCifrado is null || credencial.PayloadCifrado.Length == 0)
            throw new InvalidOperationException("PayloadCifrado vazio.");

        var protector = dataProtection.CreateProtector($"{PurposePrefix}.{credencial.KekId}");

        // O ASP.NET Data Protection ja gerencia IV+Tag internos no payload (formato proprio).
        // Para compatibilidade, persistimos somente o payload completo retornado por Protect()
        // — os campos Iv/Tag do dto sao reservados para implementacoes futuras com AES-GCM puro.
        byte[] payloadClaro;
        try
        {
            payloadClaro = protector.Unprotect(credencial.PayloadCifrado);
        }
        catch (CryptographicException ex)
        {
            logger.LogError(ex,
                "Falha decifrando cert A1 credencial={CredencialId} tenant={Empresa}. KEK rotacionada? Payload corrompido?",
                credencial.CredencialId, credencial.EmpresaId);
            throw new InvalidOperationException("Cert A1 nao pode ser decifrado.", ex);
        }

        var envelope = JsonSerializer.Deserialize<CertificadoA1Envelope>(payloadClaro)
            ?? throw new InvalidOperationException("Envelope cert A1 invalido.");

        return Task.FromResult(new CertificadoA1Decifrado(
            PfxBytes: envelope.PfxBytes,
            Senha: envelope.Senha,
            ValidoAte: envelope.ValidoAte));
    }

    public DateTime ValidarUpload(byte[] pfxBytes, string senha)
    {
        ArgumentNullException.ThrowIfNull(pfxBytes);
        if (pfxBytes.Length == 0) throw new InvalidOperationException("PfxBytes vazio.");
        if (string.IsNullOrEmpty(senha)) throw new InvalidOperationException("Senha vazia.");

        try
        {
            using var cert = X509CertificateLoader.LoadPkcs12(pfxBytes, senha);
            // X509Certificate2.NotAfter retorna DateTime com Kind=Local — comparar com
            // DateTime.UtcNow daria erro de fuso (3h em SP). Usar DateTime.Now (mesmo Kind).
            if (cert.NotAfter <= DateTime.Now)
                throw new InvalidOperationException("Certificado expirado.");
            return cert.NotAfter;
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("Cert A1 invalido ou senha incorreta.", ex);
        }
    }

    public Task<byte[]> CifrarParaArmazenamentoAsync(byte[] pfxBytes, string senha, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pfxBytes);
        var validoAte = ValidarUpload(pfxBytes, senha);

        var envelope = new CertificadoA1Envelope
        {
            PfxBytes = pfxBytes,
            Senha = senha,
            ValidoAte = validoAte,
        };

        var payloadClaro = JsonSerializer.SerializeToUtf8Bytes(envelope);
        var kekIdAtual = "v1"; // versao corrente; quando rotar, mudar aqui + Unprotect mantem compatibilidade old
        var protector = dataProtection.CreateProtector($"{PurposePrefix}.{kekIdAtual}");

        return Task.FromResult(protector.Protect(payloadClaro));
    }

    private sealed class CertificadoA1Envelope
    {
        public byte[] PfxBytes { get; set; } = Array.Empty<byte>();
        public string Senha { get; set; } = string.Empty;
        public DateTime ValidoAte { get; set; }
    }
}
