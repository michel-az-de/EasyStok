using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Domain.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe;

/// <summary>
/// Serviço que decifra o A1 ativo da empresa pra uso pelo gateway fiscal.
/// AES-256-GCM com KEK gerenciada via configuração (mesmo padrão do
/// IntegrationCredentialResolver). Decifra apenas no escopo do request —
/// nunca cacheia o material decifrado.
/// </summary>
public sealed class NotaFiscalCertificadoA1Service(
    ICertificadoA1Repository repo,
    IUnitOfWork uow,
    IConfiguration config,
    ILogger<NotaFiscalCertificadoA1Service> log) : INotaFiscalCertificadoA1Service
{
    private const int IvSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int KeySizeBytes = 32;

    public async Task<CertificadoA1Decifrado> ResolverAtivoAsync(Guid empresaId, CancellationToken ct)
    {
        var cert = await repo.ObterAtivoAsync(empresaId, ct)
            ?? throw new CertificadoA1IndisponivelException(empresaId, "Nenhum certificado ativo cadastrado.");

        if (cert.Expirado(DateTime.UtcNow))
            throw new CertificadoA1IndisponivelException(empresaId, $"Certificado expirado em {cert.ValidoAte:O}.");

        var kek = ResolverKek(cert.KekId);
        var pfx = DecifrarBytes(cert.PfxCifrado, cert.Iv, cert.Tag, kek);
        var senha = Encoding.UTF8.GetString(DecifrarBytes(cert.SenhaCifrada, cert.Iv, cert.Tag, kek));

        return new CertificadoA1Decifrado(
            Pfx: pfx,
            Senha: senha,
            NomeTitular: cert.NomeTitular,
            DocumentoTitular: cert.DocumentoTitular,
            ValidoAte: cert.ValidoAte);
    }

    public async Task RegistrarUploadAsync(
        Guid empresaId, byte[] pfxBytes, string senha, Guid usuarioId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pfxBytes);
        if (pfxBytes.Length == 0)
            throw new ArgumentException("PFX vazio.", nameof(pfxBytes));
        if (string.IsNullOrEmpty(senha))
            throw new ArgumentException("Senha vazia.", nameof(senha));

        // Lê metadados do certificado para validação + persistencia.
        using var x509 = X509CertificateLoader.LoadPkcs12(pfxBytes, senha,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);

        var nome = ExtrairNomeTitular(x509);
        var documento = ExtrairDocumento(x509);
        var validoDe = x509.NotBefore.ToUniversalTime();
        var validoAte = x509.NotAfter.ToUniversalTime();

        var (kekId, kek) = ObterKekCorrente();

        // Mesmo IV/Tag pra pfx e senha porque cifragem usa AES-GCM com nonce
        // único por par (key, plaintext). Geramos IV separado pra cada blob.
        var ivPfx = RandomNumberGenerator.GetBytes(IvSizeBytes);
        var (pfxCifrado, tagPfx) = CifrarBytes(pfxBytes, ivPfx, kek);

        var ivSenha = RandomNumberGenerator.GetBytes(IvSizeBytes);
        var (senhaCifrada, tagSenha) = CifrarBytes(Encoding.UTF8.GetBytes(senha), ivSenha, kek);

        // Modelagem simplificada: armazenamos o IV/Tag do pfx; senha tem
        // próprios metadados embutidos no início do array (concatenados).
        // Decisão pragmática para o schema atual — pode evoluir em F8+.
        var senhaCombinada = ConcatBytes(ivSenha, tagSenha, senhaCifrada);

        // Desativa A1 anterior (apenas um ativo por empresa).
        var existentes = await repo.ListarPorEmpresaAsync(empresaId, ct);
        foreach (var antigo in existentes.Where(c => c.Ativo))
        {
            antigo.Desativar();
            await repo.AtualizarAsync(antigo, ct);
        }

        var novo = NotaFiscalCertificadoA1.Criar(
            empresaId, pfxCifrado, senhaCombinada, ivPfx, tagPfx, kekId,
            nome, documento, validoDe, validoAte, usuarioId);

        await repo.AdicionarAsync(novo, ct);
        await uow.CommitAsync();

        log.LogInformation("Certificado A1 registrado para empresa {EmpresaId} valido ate {ValidoAte:O}",
            empresaId, validoAte);
    }

    private byte[] ResolverKek(string kekId)
    {
        var b64 = config[$"Crypto:Keks:{kekId}"];
        if (string.IsNullOrWhiteSpace(b64))
            throw new CryptographicException($"KEK '{kekId}' não encontrada na configuração.");
        var bytes = Convert.FromBase64String(b64);
        if (bytes.Length != KeySizeBytes)
            throw new CryptographicException($"KEK '{kekId}' tem tamanho inválido ({bytes.Length} bytes; esperado {KeySizeBytes}).");
        return bytes;
    }

    private (string KekId, byte[] Kek) ObterKekCorrente()
    {
        var kekId = config["Crypto:CurrentKekId"]
            ?? throw new CryptographicException("Crypto:CurrentKekId não configurado.");
        return (kekId, ResolverKek(kekId));
    }

    private static (byte[] CipherText, byte[] Tag) CifrarBytes(byte[] plain, byte[] iv, byte[] kek)
    {
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSizeBytes];
        using var aes = new AesGcm(kek, TagSizeBytes);
        aes.Encrypt(iv, plain, cipher, tag);
        return (cipher, tag);
    }

    private static byte[] DecifrarBytes(byte[] cipher, byte[] iv, byte[] tag, byte[] kek)
    {
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(kek, TagSizeBytes);
        aes.Decrypt(iv, cipher, tag, plain);
        return plain;
    }

    private static byte[] ConcatBytes(params byte[][] arrays)
    {
        var total = arrays.Sum(a => a.Length);
        var result = new byte[total];
        var offset = 0;
        foreach (var a in arrays)
        {
            Buffer.BlockCopy(a, 0, result, offset, a.Length);
            offset += a.Length;
        }
        return result;
    }

    private static string ExtrairNomeTitular(X509Certificate2 x)
    {
        var subj = x.Subject;
        var cn = subj.Split(',').FirstOrDefault(p => p.Trim().StartsWith("CN=", StringComparison.OrdinalIgnoreCase));
        if (cn is not null) return cn.Trim()[3..].Trim();
        return subj;
    }

    private static string ExtrairDocumento(X509Certificate2 x)
    {
        // ICP-Brasil grava CNPJ no CN como ":CNPJ" sufixo.
        var subj = x.Subject;
        var idx = subj.IndexOf(':');
        if (idx > 0 && idx + 1 < subj.Length)
        {
            var resto = subj[(idx + 1)..];
            var digitos = new string(resto.Where(char.IsDigit).Take(14).ToArray());
            if (digitos.Length is 11 or 14) return digitos;
        }
        return new string(subj.Where(char.IsDigit).Take(14).ToArray());
    }
}
