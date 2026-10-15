using Microsoft.Extensions.Caching.Memory;

namespace EasyStock.Web.Services;

/// <summary>
/// Features ativas do tenant (ADR-0048), já resolvidas para o Web. <c>Ok</c> distingue
/// "a empresa não tem feature nenhuma" de "não conseguimos perguntar" — e as duas coisas
/// escondem módulo, mas por motivos diferentes que o chamador pode querer logar.
/// </summary>
public sealed record TenantFeaturesBff(IReadOnlySet<string> Ativas, bool Ok)
{
    public static readonly TenantFeaturesBff Indisponivel =
        new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), Ok: false);

    /// <summary>
    /// Módulo sem feature exigida é sempre visível; com feature exigida, só aparece se a
    /// flag estiver ativa. <b>Fail-closed</b>: Api fora do ar esconde o módulo em vez de
    /// mostrar. Um módulo B2B aparecendo por engano na cozinha é pior que um módulo faltando
    /// para quem sabe pedir — e o oposto do fail-open que o menu usa para rota sem dono
    /// (ADR-0046), porque lá é navegação e aqui é visibilidade de produto.
    /// </summary>
    public bool Permite(string? featureExigida) =>
        string.IsNullOrEmpty(featureExigida) || Ativas.Contains(featureExigida);
}

/// <summary>Seam testável: busca as features da Api. Implementação real usa ApiClient.</summary>
public interface ITenantFeaturesFonte
{
    Task<(IReadOnlyList<string>? Features, bool Ok)> FetchAsync();
}

/// <summary>
/// Busca <c>GET feature-flags</c>. Claims-only: a Api resolve a empresa pelo JWT, então não
/// há id de tenant trafegando na URL.
/// </summary>
public sealed class TenantFeaturesFonte(ApiClient api) : ITenantFeaturesFonte
{
    public async Task<(IReadOnlyList<string>?, bool)> FetchAsync()
    {
        var r = await api.GetAsync<List<string>>("feature-flags");
        return (r.Success ? r.Data : null, r.Success);
    }
}

/// <summary>
/// BFF das feature flags: cache de 5 minutos por EMPRESA. A chave não inclui loja nem
/// usuário porque a flag não varia por eles — incluir só multiplicaria as entradas e as
/// idas à Api. Falha nunca é cacheada: o próximo request tenta de novo, senão desligar um
/// módulo por engano ficaria valendo por 5 minutos sem chance de correção.
/// </summary>
public sealed class TenantFeaturesService(ITenantFeaturesFonte fonte, IMemoryCache cache)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public async Task<TenantFeaturesBff> ObterAsync(string? empresaId)
    {
        var key = $"tenant-features:{empresaId}";
        if (cache.TryGetValue(key, out TenantFeaturesBff? cached) && cached is not null)
            return cached;

        var (features, ok) = await fonte.FetchAsync();
        if (!ok || features is null)
            return TenantFeaturesBff.Indisponivel; // não cacheia falha

        var result = new TenantFeaturesBff(
            new HashSet<string>(features, StringComparer.OrdinalIgnoreCase), Ok: true);

        cache.Set(key, result, Ttl);
        return result;
    }
}
