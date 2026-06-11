using EasyStock.Web.Models.Api;
using EasyStock.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace EasyStock.Web.UnitTests.Services;

/// <summary>Cache + invalidação + degradação do BFF de favoritos (ADR-0032, fatia 4b).</summary>
public class PreferenciaMenuServiceTests
{
    private static IMemoryCache NewCache() => new MemoryCache(new MemoryCacheOptions());

    [Fact]
    public async Task Obter_cacheia_e_nao_refaz_fetch()
    {
        var fonte = Substitute.For<IPreferenciaMenuFonte>();
        fonte.ObterAsync("l1").Returns((new FavoritosMenuApi { Favoritos = new() { "pedidos" }, KdsHabilitado = true }, true));
        var svc = new PreferenciaMenuService(fonte, NewCache());

        var a = await svc.ObterAsync("u1", "l1");
        var b = await svc.ObterAsync("u1", "l1");

        a.Favoritos.Should().Equal("pedidos");
        a.KdsHabilitado.Should().BeTrue();
        b.Should().BeSameAs(a);
        await fonte.Received(1).ObterAsync("l1");
    }

    [Fact]
    public async Task Obter_falha_degrada_e_nao_cacheia()
    {
        var fonte = Substitute.For<IPreferenciaMenuFonte>();
        fonte.ObterAsync("l1").Returns(((FavoritosMenuApi?)null, false));
        var svc = new PreferenciaMenuService(fonte, NewCache());

        var r = await svc.ObterAsync("u1", "l1");
        await svc.ObterAsync("u1", "l1");

        r.Favoritos.Should().BeNull();
        r.KdsHabilitado.Should().BeFalse();
        await fonte.Received(2).ObterAsync("l1"); // falha nao foi cacheada
    }

    [Fact]
    public async Task Salvar_invalida_o_cache()
    {
        var fonte = Substitute.For<IPreferenciaMenuFonte>();
        fonte.ObterAsync("l1").Returns((new FavoritosMenuApi { Favoritos = new() { "pedidos" }, KdsHabilitado = false }, true));
        fonte.SalvarAsync("l1", Arg.Any<IReadOnlyList<string>>()).Returns((new List<string> { "caixa" } as IReadOnlyList<string>, true));
        var svc = new PreferenciaMenuService(fonte, NewCache());

        await svc.ObterAsync("u1", "l1");                 // popula cache
        var norm = await svc.SalvarAsync("u1", "l1", new[] { "caixa" }); // invalida
        await svc.ObterAsync("u1", "l1");                 // refaz fetch

        norm.Should().Equal("caixa");
        await fonte.Received(2).ObterAsync("l1");
    }

    [Fact]
    public async Task Salvar_falha_devolve_null()
    {
        var fonte = Substitute.For<IPreferenciaMenuFonte>();
        fonte.SalvarAsync("l1", Arg.Any<IReadOnlyList<string>>()).Returns(((IReadOnlyList<string>?)null, false));
        var svc = new PreferenciaMenuService(fonte, NewCache());

        var norm = await svc.SalvarAsync("u1", "l1", new[] { "caixa" });

        norm.Should().BeNull();
    }
}
