using EasyStock.Web.Models.Api;
using EasyStock.Web.Navigation;
using Microsoft.Extensions.Caching.Memory;

namespace EasyStock.Web.Services;

/// <summary>Dados crus do resumo do menu (dashboard + dia). <c>Ok</c> = o dashboard
/// (fonte de críticos/vencidos) respondeu; <c>Dia</c> pode faltar sem invalidar o resto.</summary>
public sealed record MenuResumoRaw(DashboardResumoApi? Dash, ResumoDiaApi? Dia, bool Ok);

/// <summary>Seam testável: busca os dois resumos da Api. Implementação real usa ApiClient.</summary>
public interface IMenuResumoSource
{
    Task<MenuResumoRaw> FetchAsync();
}

/// <summary>Busca <c>analytics/dashboard</c> + <c>analytics/dia</c> em paralelo (molde DashboardController).
/// Multi-tenant pelo claim do JWT (a Api resolve empresa do <c>sub</c>/<c>empresaId</c>).</summary>
public sealed class MenuResumoSource(ApiClient api) : IMenuResumoSource
{
    public async Task<MenuResumoRaw> FetchAsync()
    {
        var dashTask = api.GetAsync<DashboardResumoApi>("analytics/dashboard");
        var diaTask = api.GetAsync<ResumoDiaApi>("analytics/dia");
        await Task.WhenAll(dashTask, diaTask);

        var dash = await dashTask;
        var dia = await diaTask;

        return new MenuResumoRaw(
            dash.Success ? dash.Data : null,
            dia.Success ? dia.Data : null,
            Ok: dash.Success);
    }
}

/// <summary>
/// Agrega os badges do menu (ADR-0032, fatia 2) com cache curto por empresa+loja.
/// Cache key SEMPRE inclui a loja (sem isso vaza contagem entre lojas do mesmo tenant).
/// Falha NUNCA é cacheada — devolve zero+ok:false e tenta de novo na próxima chamada.
/// </summary>
public sealed class MenuResumoService(IMenuResumoSource source, IMemoryCache cache)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    public async Task<(MenuBadges Badges, bool Ok)> ObterAsync(string? empresaId, string? lojaId)
    {
        var key = $"menu-resumo:{empresaId}:{lojaId}";
        if (cache.TryGetValue(key, out MenuBadges? cached) && cached is not null)
            return (cached, true);

        var raw = await source.FetchAsync();
        if (!raw.Ok || raw.Dash is null)
            return (MenuBadges.Zero, false); // não cacheia falha

        var badges = new MenuBadges(
            PedidosAbertos: raw.Dia?.PedidosPendentes ?? 0,
            ProdutosCriticos: raw.Dash.AlertasEstoqueBaixo,
            LotesVencidos: raw.Dash.AlertasVencidos);

        cache.Set(key, badges, Ttl);
        return (badges, true);
    }
}
