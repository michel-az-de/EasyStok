namespace EasyStock.Api.Startup;

/// <summary>
/// Chave-mestra (KEK) das credenciais de integração (F16, #1246). No <c>.env</c> de cada ambiente
/// ela vem em variáveis de nome fixo, e aqui vira a configuração que o
/// <c>IntegrationCredentialResolver</c> lê (<c>Crypto:CurrentKekId</c> e <c>Crypto:Keks:{id}</c>):
/// <list type="bullet">
///   <item><c>EZ_CRYPTO_KEK_ID</c> + <c>EZ_CRYPTO_KEK</c>: a KEK atual (Base64 de 32 bytes).</item>
///   <item><c>EZ_CRYPTO_KEK_ANTERIOR_ID</c> + <c>EZ_CRYPTO_KEK_ANTERIOR</c>: a antiga, durante a rotação.</item>
/// </list>
/// Sem as variáveis, nada é mapeado e vale o que estiver em <c>Crypto:*</c>. O valor nunca é logado.
/// </summary>
public static class ChaveMestraConfiguracao
{
    public const string VarKekId = "EZ_CRYPTO_KEK_ID";
    public const string VarKek = "EZ_CRYPTO_KEK";
    public const string VarKekAnteriorId = "EZ_CRYPTO_KEK_ANTERIOR_ID";
    public const string VarKekAnterior = "EZ_CRYPTO_KEK_ANTERIOR";

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
}
