using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Integration;

/// <summary>
/// Chave-mestra (KEK) das credenciais de integração, para a API e para o Worker (I4.1, #1674; porta da
/// PR #1304). No <c>.env</c> de cada ambiente ela vem em variáveis de nome fixo, e aqui vira a
/// configuração que o <see cref="IntegrationCredentialResolver"/> lê (<c>Crypto:CurrentKekId</c> e
/// <c>Crypto:Keks:{id}</c>):
/// <list type="bullet">
///   <item><c>EZ_CRYPTO_KEK_ID</c> + <c>EZ_CRYPTO_KEK</c>: a KEK atual (Base64 de 32 bytes).</item>
///   <item><c>EZ_CRYPTO_KEK_ANTERIOR_ID</c> + <c>EZ_CRYPTO_KEK_ANTERIOR</c>: a antiga, durante a rotação.</item>
/// </list>
/// Sem as variáveis, nada é mapeado e vale o que estiver em <c>Crypto:*</c>. Nesta etapa a falta da chave
/// só gera aviso na subida; o fail-fast é da I4.2. O valor da chave nunca é logado.
/// </summary>
public static class ChaveMestraConfiguracao
{
    public const string VarKekId = "EZ_CRYPTO_KEK_ID";
    public const string VarKek = "EZ_CRYPTO_KEK";
    public const string VarKekAnteriorId = "EZ_CRYPTO_KEK_ANTERIOR_ID";
    public const string VarKekAnterior = "EZ_CRYPTO_KEK_ANTERIOR";

    private const int TamanhoDaChaveEmBytes = 32; // AES-256

    /// <summary>Núcleo puro: lê as variáveis por <paramref name="ambiente"/> e devolve as chaves de configuração.</summary>
    public static IDictionary<string, string?> MapearVariaveisEz(Func<string, string?> ambiente)
    {
        var config = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        var atualId = ambiente(VarKekId)?.Trim();
        if (!string.IsNullOrEmpty(atualId))
        {
            config["Crypto:CurrentKekId"] = atualId;
            if (ambiente(VarKek)?.Trim() is { Length: > 0 } atual)
                config[$"Crypto:Keks:{atualId}"] = atual;
        }

        var anteriorId = ambiente(VarKekAnteriorId)?.Trim();
        if (!string.IsNullOrEmpty(anteriorId) && ambiente(VarKekAnterior)?.Trim() is { Length: > 0 } anterior)
            config[$"Crypto:Keks:{anteriorId}"] = anterior;

        return config;
    }

    /// <summary>Acrescenta as variáveis <c>EZ_CRYPTO_*</c> do processo por cima da configuração.</summary>
    public static IConfigurationBuilder AdicionarChaveMestraDoAmbiente(this IConfigurationBuilder configuracao)
    {
        var mapeado = MapearVariaveisEz(Environment.GetEnvironmentVariable);
        return mapeado.Count == 0 ? configuracao : configuracao.AddInMemoryCollection(mapeado);
    }

    /// <summary>
    /// O que falta para a chave-mestra servir, em texto para o log, ou <see langword="null"/> se está
    /// tudo certo. Nunca inclui o valor da chave.
    /// </summary>
    public static string? Diagnosticar(IConfiguration configuration)
    {
        var id = configuration["Crypto:CurrentKekId"]?.Trim();
        if (string.IsNullOrEmpty(id))
            return $"chave-mestra ausente: defina {VarKekId} e {VarKek}. Sem ela, credencial de integração não é gravada nem lida.";

        var valor = configuration[$"Crypto:Keks:{id}"]?.Trim();
        if (string.IsNullOrEmpty(valor))
            return $"chave-mestra '{id}' sem valor: defina {VarKek}.";

        var bytes = new byte[valor.Length]; // Base64 nunca decodifica para mais bytes do que caracteres
        var valida = Convert.TryFromBase64String(valor, bytes, out var lidos) && lidos == TamanhoDaChaveEmBytes;
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes);
        return valida ? null : $"chave-mestra '{id}' inválida: {VarKek} precisa ser Base64 de 32 bytes.";
    }

    /// <summary>
    /// Registra um aviso se a chave-mestra faltar ou for inválida. Não lança: o host sobe mesmo assim.
    /// Devolve se avisou.
    /// </summary>
    public static bool AvisarSeFaltar(IConfiguration configuration, ILogger logger, string host)
    {
        if (Diagnosticar(configuration) is not { } motivo) return false;
        logger.LogWarning("{Host}: {Motivo}", host, motivo);
        return true;
    }
}