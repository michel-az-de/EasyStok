using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Integration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Integration;

/// <summary>
/// Implementação Postgres do <see cref="IIntegrationCredentialResolver"/>.
/// Cifragem AES-256-GCM com KEKs identificadas por <c>kek_id</c> e
/// resolvidas via <see cref="IConfiguration"/> (section <c>Crypto:Keks</c>).
///
/// <para>
/// <b>Configuração esperada</b>:
/// <code>
/// "Crypto": {
///   "CurrentKekId": "kek-2026-01",
///   "Keks": {
///     "kek-2026-01": "BASE64_DE_32_BYTES",
///     "kek-2025-04": "BASE64_DE_32_BYTES"
///   }
/// }
/// </code>
/// Em produção, KEKs vêm de Secret Manager via env vars (override do
/// <c>appsettings.json</c>). Múltiplas KEKs permitem rotação sem perda
/// de acesso a credenciais cifradas com KEK antiga.
/// </para>
///
/// <para>
/// <b>Cache</b>: payload decifrado é cacheado em <see cref="IMemoryCache"/>
/// por 5 minutos. Caller deve invalidar (via <see cref="SalvarAsync"/>)
/// quando rotacionar credencial.
/// </para>
///
/// <para>
/// <b>Segurança</b>: chave-mestra (KEK) NUNCA aparece em logs. Erros de
/// decifragem (tag inválida, KEK não encontrada) lançam <see cref="CryptographicException"/>
/// genérica — o caller decide como reportar (sem expor detalhes ao cliente).
/// </para>
/// </summary>
public sealed class IntegrationCredentialResolver(
    ICredencialIntegracaoRepository repo,
    IUnitOfWork uow,
    IConfiguration config,
    IMemoryCache cache,
    ILogger<IntegrationCredentialResolver> logger) : IIntegrationCredentialResolver
{
    private const int IvSizeBytes = 12; // AES-GCM standard nonce length
    private const int TagSizeBytes = 16;
    private const int KeySizeBytes = 32; // AES-256
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public async Task<T?> ObterAsync<T>(
        Guid empresaId,
        string providerKey,
        AmbienteIntegracao ambiente,
        CancellationToken ct = default) where T : class
    {
        var cacheKey = BuildCacheKey<T>(empresaId, providerKey, ambiente);
        if (cache.TryGetValue(cacheKey, out T? cached) && cached is not null)
        {
            return cached;
        }

        var credencial = await repo.GetAtivaAsync(empresaId, providerKey, ambiente, ct);
        if (credencial is null || !credencial.EstaUtilizavel())
        {
            return null;
        }

        var key = ResolveKek(credencial.KekId);

        byte[] plaintext;
        try
        {
            plaintext = DecryptAesGcm(credencial.PayloadCifrado, credencial.Iv, credencial.Tag, key);
        }
        catch (CryptographicException ex)
        {
            logger.LogError(ex,
                "Falha de decifragem em CredencialIntegracao {Id} (kek={KekId}). " +
                "KEK pode estar corrompida ou payload adulterado.",
                credencial.Id, credencial.KekId);
            throw;
        }
        finally
        {
            // Limpa key local — não permanece em memória além do necessário.
            CryptographicOperations.ZeroMemory(key);
        }

        T? payload;
        try
        {
            payload = JsonSerializer.Deserialize<T>(plaintext, JsonOpts);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        if (payload is null) return null;

        // Telemetria de uso (não bloqueia retorno se falhar persistir).
        try
        {
            credencial.RegistrarUso();
            await repo.UpdateAsync(credencial, ct);
            await uow.CommitAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Falha ao registrar uso de CredencialIntegracao {Id} (não-bloqueante).",
                credencial.Id);
        }

        cache.Set(cacheKey, payload, CacheTtl);
        return payload;
    }

    public async Task SalvarAsync<T>(
        Guid empresaId,
        CategoriaIntegracao categoria,
        string providerKey,
        AmbienteIntegracao ambiente,
        T payload,
        Guid criadoPorUsuarioId,
        DateTime? validoAte = null,
        CancellationToken ct = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(payload);

        var currentKekId = config["Crypto:CurrentKekId"]
            ?? throw new InvalidOperationException("Crypto:CurrentKekId não configurado.");
        var key = ResolveKek(currentKekId);

        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);

        byte[] cipher;
        byte[] iv;
        byte[] tag;
        try
        {
            (cipher, iv, tag) = EncryptAesGcm(plaintext, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(key);
        }

        // Desativa credencial anterior ativa (se houver) antes de criar nova.
        var anterior = await repo.GetAtivaAsync(empresaId, providerKey, ambiente, ct);
        if (anterior is not null)
        {
            anterior.Desativar();
            await repo.UpdateAsync(anterior, ct);
        }

        var nova = CredencialIntegracao.Criar(
            empresaId: empresaId,
            categoria: categoria,
            providerKey: providerKey,
            ambiente: ambiente,
            payloadCifrado: cipher,
            kekId: currentKekId,
            iv: iv,
            tag: tag,
            criadoPorUsuarioId: criadoPorUsuarioId,
            validoAte: validoAte);

        await repo.AddAsync(nova, ct);
        await uow.CommitAsync();

        // Invalida cache da chave anterior (caller pode chamar Obter logo depois).
        cache.Remove(BuildCacheKey<T>(empresaId, providerKey, ambiente));
    }

    public async Task RotacionarKekAsync(string novoKekId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(novoKekId))
            throw new ArgumentException("novoKekId é obrigatório.", nameof(novoKekId));

        var novaKey = ResolveKek(novoKekId);

        try
        {
            // Lista todas as KEK IDs distintas em uso (exceto a nova).
            // Em produção isso seria batched + paginated; aqui simples.
            var todasAtivas = await repo.ListarPorKekAsync(novoKekId, ct); // já-cifradas com nova KEK
            var jaCifradas = new HashSet<Guid>(todasAtivas.Select(c => c.Id));

            // Pega tudo cifrado com KEK antiga — varremos credenciais ativas
            // por KEKs que aparecem em config (excluindo a nova).
            var keksKnown = config.GetSection("Crypto:Keks").GetChildren().Select(c => c.Key).ToList();
            var kekParaRotacionar = keksKnown.Where(k => !string.Equals(k, novoKekId, StringComparison.Ordinal)).ToList();

            int rotacionadas = 0;
            foreach (var kekAntiga in kekParaRotacionar)
            {
                var credenciais = await repo.ListarPorKekAsync(kekAntiga, ct);
                foreach (var c in credenciais)
                {
                    if (jaCifradas.Contains(c.Id)) continue;
                    var keyAntiga = ResolveKek(c.KekId);
                    byte[] plain;
                    try
                    {
                        plain = DecryptAesGcm(c.PayloadCifrado, c.Iv, c.Tag, keyAntiga);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(keyAntiga);
                    }

                    byte[] novoCipher;
                    byte[] novoIv;
                    byte[] novaTag;
                    try
                    {
                        (novoCipher, novoIv, novaTag) = EncryptAesGcm(plain, novaKey);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(plain);
                    }

                    c.RotacionarKek(novoCipher, novoKekId, novoIv, novaTag);
                    await repo.UpdateAsync(c, ct);
                    rotacionadas++;
                }
            }

            await uow.CommitAsync();

            // Invalida cache inteiro — payloads decifrados continuam válidos
            // mas re-fetch é seguro (próximo Obter recifrar com a nova KEK).
            // IMemoryCache não tem Clear nativo; melhor usar tokens de invalidação
            // em produção. Aqui logamos pra forçar restart do worker se necessário.
            logger.LogInformation(
                "Rotação de KEK concluída: {Rotacionadas} credenciais re-cifradas para {NovoKekId}.",
                rotacionadas, novoKekId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(novaKey);
        }
    }

    // ─── Helpers internos ────────────────────────────────────────────────

    private static string BuildCacheKey<T>(Guid empresaId, string providerKey, AmbienteIntegracao ambiente)
    {
        var key = (providerKey ?? string.Empty).Trim().ToLowerInvariant();
        return $"credencial:{empresaId:N}:{key}:{(int)ambiente}:{typeof(T).FullName}";
    }

    /// <summary>
    /// Resolve a KEK pelo id. Lança em ausência ou tamanho inválido.
    /// Retorno: array de bytes (32 bytes) que o caller deve zerar após uso.
    /// </summary>
    private byte[] ResolveKek(string kekId)
    {
        var kekBase64 = config[$"Crypto:Keks:{kekId}"];
        if (string.IsNullOrWhiteSpace(kekBase64))
        {
            throw new InvalidOperationException(
                $"KEK '{kekId}' não configurada em Crypto:Keks. " +
                "Em produção, garantir que env var ou Secret Manager esteja injetando.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(kekBase64);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"KEK '{kekId}' não é Base64 válido.", ex);
        }

        if (key.Length != KeySizeBytes)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidOperationException(
                $"KEK '{kekId}' tem {key.Length} bytes; esperado {KeySizeBytes} (AES-256).");
        }

        return key;
    }

    private static (byte[] cipher, byte[] iv, byte[] tag) EncryptAesGcm(byte[] plaintext, byte[] key)
    {
        var iv = RandomNumberGenerator.GetBytes(IvSizeBytes);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];

        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Encrypt(iv, plaintext, cipher, tag);

        return (cipher, iv, tag);
    }

    private static byte[] DecryptAesGcm(byte[] cipher, byte[] iv, byte[] tag, byte[] key)
    {
        var plaintext = new byte[cipher.Length];
        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Decrypt(iv, cipher, tag, plaintext);
        return plaintext;
    }
}
