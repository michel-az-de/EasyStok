using System.Security.Cryptography;
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
/// por 5 minutos. A chave do cache carrega uma geração global: salvar, desativar e
/// rotacionar a KEK sobem a geração, e tudo o que estava em cache deixa de ser achado
/// (F16, #1246). <see cref="IMemoryCache"/> não tem <c>Clear</c>; salvar é raro.
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
    private const string ChaveGeracao = "credencial:geracao";

    /// <summary>Contador da geração do cache, compartilhado pelo singleton do <see cref="IMemoryCache"/>.</summary>
    private sealed class GeracaoCache
    {
        public long Valor;
    }

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
        string? mascara = null,
        CancellationToken ct = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(payload);

        var currentKekId = config["Crypto:CurrentKekId"];
        if (string.IsNullOrWhiteSpace(currentKekId))
            throw new ChaveMestraAusenteException(
                "Chave-mestra não configurada: defina EZ_CRYPTO_KEK_ID e EZ_CRYPTO_KEK (Crypto:CurrentKekId e Crypto:Keks).");
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
            validoAte: validoAte,
            mascara: mascara);

        await repo.AddAsync(nova, ct);
        await uow.CommitAsync();

        // A chave anterior pode estar em cache em qualquer tipo T: sobe a geração.
        InvalidarCache();
    }

    public async Task<int> DesativarAsync(Guid empresaId, string providerKey, CancellationToken ct = default)
    {
        var ativas = await repo.ListarAtivasDoProviderAsync(empresaId, providerKey, ct);
        foreach (var credencial in ativas)
        {
            credencial.Desativar();
            await repo.UpdateAsync(credencial, ct);
        }

        if (ativas.Count > 0)
            await uow.CommitAsync();

        InvalidarCache();
        return ativas.Count;
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

            // Payload decifrado em cache continua válido, mas a linha mudou: re-lê.
            InvalidarCache();
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

    private string BuildCacheKey<T>(Guid empresaId, string providerKey, AmbienteIntegracao ambiente)
    {
        var key = (providerKey ?? string.Empty).Trim().ToLowerInvariant();
        var geracao = Interlocked.Read(ref Geracao().Valor);
        return $"credencial:{geracao}:{empresaId:N}:{key}:{(int)ambiente}:{typeof(T).FullName}";
    }

    private GeracaoCache Geracao() => cache.GetOrCreate(ChaveGeracao, entrada =>
    {
        entrada.Priority = CacheItemPriority.NeverRemove;
        return new GeracaoCache();
    })!;

    private void InvalidarCache() => Interlocked.Increment(ref Geracao().Valor);

    /// <summary>
    /// Resolve a KEK pelo id. Lança em ausência ou tamanho inválido.
    /// Retorno: array de bytes (32 bytes) que o caller deve zerar após uso.
    /// </summary>
    private byte[] ResolveKek(string kekId)
    {
        var kekBase64 = config[$"Crypto:Keks:{kekId}"];
        if (string.IsNullOrWhiteSpace(kekBase64))
        {
            throw new ChaveMestraAusenteException(
                $"KEK '{kekId}' não configurada em Crypto:Keks. " +
                "Defina EZ_CRYPTO_KEK (ou EZ_CRYPTO_KEK_ANTERIOR, se for a KEK antiga) no .env do ambiente.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(kekBase64);
        }
        catch (FormatException ex)
        {
            throw new ChaveMestraAusenteException($"KEK '{kekId}' não é Base64 válido.", ex);
        }

        if (key.Length != KeySizeBytes)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new ChaveMestraAusenteException(
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
