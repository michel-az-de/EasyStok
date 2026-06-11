using EasyStock.Web.Models.Api;
using EasyStock.Web.Navigation;
using EasyStock.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace EasyStock.Web.UnitTests.Services;

/// <summary>
/// Agregação + cache dos badges do menu (ADR-0032, fatia 2). O HTTP fica no
/// <c>IMenuResumoSource</c> (fake aqui); o 401/redirect de sessão é do BaseController/esFetch.
/// </summary>
public class MenuResumoServiceTests
{
    private static IMemoryCache NewCache() => new MemoryCache(new MemoryCacheOptions());

    private static MenuResumoRaw Raw(int criticos, int vencidos, int pedidos, bool ok = true) =>
        new(
            ok ? new DashboardResumoApi { AlertasEstoqueBaixo = criticos, AlertasVencidos = vencidos } : null,
            new ResumoDiaApi { PedidosPendentes = pedidos },
            Ok: ok);

    [Fact]
    public async Task Agrega_dashboard_e_dia()
    {
        var source = Substitute.For<IMenuResumoSource>();
        source.FetchAsync().Returns(Raw(criticos: 2, vencidos: 5, pedidos: 3));
        var svc = new MenuResumoService(source, NewCache());

        var (badges, ok) = await svc.ObterAsync("e1", "l1");

        ok.Should().BeTrue();
        badges.ProdutosCriticos.Should().Be(2);
        badges.LotesVencidos.Should().Be(5);
        badges.PedidosAbertos.Should().Be(3);
        badges.DashboardTotal.Should().Be(7); // criticos + vencidos (pedidos nao entra)
    }

    [Fact]
    public async Task Cacheia_e_nao_refaz_fetch_na_mesma_loja()
    {
        var source = Substitute.For<IMenuResumoSource>();
        source.FetchAsync().Returns(Raw(1, 1, 1));
        var svc = new MenuResumoService(source, NewCache());

        await svc.ObterAsync("e1", "l1");
        await svc.ObterAsync("e1", "l1");

        await source.Received(1).FetchAsync();
    }

    [Fact]
    public async Task Isola_por_loja()
    {
        var source = Substitute.For<IMenuResumoSource>();
        source.FetchAsync().Returns(Raw(1, 1, 1));
        var svc = new MenuResumoService(source, NewCache());

        await svc.ObterAsync("e1", "lojaA");
        await svc.ObterAsync("e1", "lojaB");

        await source.Received(2).FetchAsync(); // chaves de cache distintas por loja
    }

    [Fact]
    public async Task Nao_cacheia_falha()
    {
        var source = Substitute.For<IMenuResumoSource>();
        source.FetchAsync().Returns(Raw(0, 0, 0, ok: false));
        var svc = new MenuResumoService(source, NewCache());

        var (badges, ok) = await svc.ObterAsync("e1", "l1");
        await svc.ObterAsync("e1", "l1");

        ok.Should().BeFalse();
        badges.Should().Be(MenuBadges.Zero);
        await source.Received(2).FetchAsync(); // falha nao foi cacheada -> refez
    }

    [Fact]
    public async Task Dia_ausente_nao_invalida_dashboard()
    {
        var source = Substitute.For<IMenuResumoSource>();
        source.FetchAsync().Returns(new MenuResumoRaw(
            new DashboardResumoApi { AlertasEstoqueBaixo = 4, AlertasVencidos = 1 },
            Dia: null, Ok: true));
        var svc = new MenuResumoService(source, NewCache());

        var (badges, ok) = await svc.ObterAsync("e1", "l1");

        ok.Should().BeTrue();
        badges.PedidosAbertos.Should().Be(0);
        badges.DashboardTotal.Should().Be(5);
    }
}
