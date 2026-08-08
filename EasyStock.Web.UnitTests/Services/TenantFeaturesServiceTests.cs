using EasyStock.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;

namespace EasyStock.Web.UnitTests.Services;

/// <summary>
/// BFF das feature flags por tenant (ADR-0048): cache por empresa, falha nunca cacheada e
/// decisão fail-closed. O que estes testes protegem é a diferença entre "esta empresa não
/// tem o módulo" e "não conseguimos perguntar" — as duas escondem, mas só a segunda pode
/// ser corrigida no próximo request.
/// </summary>
public class TenantFeaturesServiceTests
{
    private static IMemoryCache NewCache() => new MemoryCache(new MemoryCacheOptions());

    private static ITenantFeaturesFonte Fonte(IReadOnlyList<string>? features, bool ok = true)
    {
        var f = Substitute.For<ITenantFeaturesFonte>();
        f.FetchAsync().Returns((features, ok));
        return f;
    }

    [Fact]
    public async Task Traz_as_features_ativas_da_empresa()
    {
        var svc = new TenantFeaturesService(Fonte(["modulo.comercial"]), NewCache());

        var r = await svc.ObterAsync("empresa-1");

        r.Ok.Should().BeTrue();
        r.Ativas.Should().Contain("modulo.comercial");
    }

    [Fact]
    public async Task Cacheia_e_nao_refaz_o_fetch()
    {
        var fonte = Fonte(["modulo.crm"]);
        var svc = new TenantFeaturesService(fonte, NewCache());

        await svc.ObterAsync("empresa-1");
        await svc.ObterAsync("empresa-1");

        await fonte.Received(1).FetchAsync();
    }

    [Fact]
    public async Task Isola_por_empresa()
    {
        // A chave inclui a empresa: sem isso a Casa da Babá herdaria as flags da FMA.
        var fonte = Fonte(["modulo.crm"]);
        var svc = new TenantFeaturesService(fonte, NewCache());

        await svc.ObterAsync("empresa-1");
        await svc.ObterAsync("empresa-2");

        await fonte.Received(2).FetchAsync();
    }

    [Fact]
    public async Task Nao_cacheia_falha()
    {
        // Desligar um módulo por engano não pode ficar valendo 5 minutos sem chance de
        // correção — e um módulo escondido por timeout deve voltar assim que a Api voltar.
        var fonte = Fonte(null, ok: false);
        var svc = new TenantFeaturesService(fonte, NewCache());

        var r1 = await svc.ObterAsync("empresa-1");
        var r2 = await svc.ObterAsync("empresa-1");

        r1.Ok.Should().BeFalse();
        r2.Ok.Should().BeFalse();
        await fonte.Received(2).FetchAsync();
    }

    [Fact]
    public async Task Empresa_sem_flag_nenhuma_e_sucesso_com_lista_vazia()
    {
        var svc = new TenantFeaturesService(Fonte([]), NewCache());

        var r = await svc.ObterAsync("empresa-1");

        r.Ok.Should().BeTrue(because: "a Api respondeu; a empresa é que não tem flag");
        r.Ativas.Should().BeEmpty();
    }

    [Fact]
    public async Task Comparacao_de_feature_ignora_caixa()
    {
        var svc = new TenantFeaturesService(Fonte(["Modulo.Comercial"]), NewCache());

        var r = await svc.ObterAsync("empresa-1");

        r.Permite("modulo.comercial").Should().BeTrue();
    }

    // ── decisão de visibilidade ──────────────────────────────────────

    [Fact]
    public void Item_sem_feature_exigida_aparece_sempre()
    {
        TenantFeaturesBff.Indisponivel.Permite(null).Should().BeTrue();
        TenantFeaturesBff.Indisponivel.Permite("").Should().BeTrue();
    }

    [Fact]
    public async Task Item_gated_some_quando_a_flag_esta_off()
    {
        var svc = new TenantFeaturesService(Fonte([]), NewCache());

        var r = await svc.ObterAsync("empresa-1");

        r.Permite("modulo.comercial").Should().BeFalse();
    }

    [Fact]
    public void Api_fora_do_ar_esconde_o_item_gated()
    {
        // Fail-closed: um módulo B2B aparecendo por engano numa cozinha é pior que um
        // módulo faltando para quem sabe pedir.
        TenantFeaturesBff.Indisponivel.Permite("modulo.comercial").Should().BeFalse();
    }
}
