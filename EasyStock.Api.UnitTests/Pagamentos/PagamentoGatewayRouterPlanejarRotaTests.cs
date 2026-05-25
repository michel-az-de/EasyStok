using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Infra.Async.Pagamentos;
using FluentAssertions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Pagamentos;

/// <summary>
/// Cobertura do <c>PlanejarRotaAsync</c> — algoritmo de smart routing P0:
/// regras de tenant tem precedencia sobre globais; faixa de valor filtra;
/// cross-check com gateways registrados elimina provedores ausentes;
/// health store suspenso pula gateway.
/// </summary>
public class PagamentoGatewayRouterPlanejarRotaTests
{
    private static IPagamentoGateway StubGateway(string provedor, params string[] metodos)
    {
        var g = Substitute.For<IPagamentoGateway>();
        g.Provedor.Returns(provedor);
        g.SuportaMetodo(Arg.Any<string>())
            .Returns(call => metodos.Contains((string)call[0], StringComparer.OrdinalIgnoreCase));
        return g;
    }

    private static IGatewayHealthStore HealthSempreOk()
    {
        var h = Substitute.For<IGatewayHealthStore>();
        h.PodeUsar(Arg.Any<string>()).Returns(true);
        return h;
    }

    private static IGatewayRoutingRuleRepository ComRegras(params GatewayRoutingRule[] regras)
    {
        var r = Substitute.For<IGatewayRoutingRuleRepository>();
        r.ObterRegrasAplicaveisAsync(Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(regras);
        return r;
    }

    [Fact]
    public async Task SemRegrasAplicaveis_RetornaPlanoVazio()
    {
        var router = new PagamentoGatewayRouter(
            new[] { StubGateway("EfiPix", "pix") },
            ComRegras(),
            HealthSempreOk());

        var plan = await router.PlanejarRotaAsync(
            new RoutingContext(Guid.NewGuid(), "pix", 100m));

        plan.ProvedoresOrdenados.Should().BeEmpty();
        plan.Motivo.Should().Be("sem-regra-aplicavel");
    }

    [Fact]
    public async Task RegraGlobal_RetornaProvedor_GlobalPriority()
    {
        var router = new PagamentoGatewayRouter(
            new[] { StubGateway("EfiPix", "pix") },
            ComRegras(GatewayRoutingRule.Criar("pix", "EfiPix", prioridade: 1, empresaId: null)),
            HealthSempreOk());

        var plan = await router.PlanejarRotaAsync(
            new RoutingContext(Guid.NewGuid(), "pix", 100m));

        plan.ProvedoresOrdenados.Should().ContainSingle().Which.Should().Be("EfiPix");
        plan.Motivo.Should().Be("global-priority");
    }

    [Fact]
    public async Task TenantTemPrecedenciaSobreGlobal()
    {
        var empresaId = Guid.NewGuid();
        // Global = EfiPix; Tenant override = "Manual" prio 1 (manual nao "pix" obviously,
        // entao usamos um exemplo onde tenant override aponta pra outro provedor).
        // Ambas regras precisam suportar metodo "pix" via stub.
        var router = new PagamentoGatewayRouter(
            new[] {
                StubGateway("EfiPix", "pix"),
                StubGateway("CustomGateway", "pix")
            },
            ComRegras(
                GatewayRoutingRule.Criar("pix", "EfiPix", prioridade: 1, empresaId: null),
                GatewayRoutingRule.Criar("pix", "CustomGateway", prioridade: 1, empresaId: empresaId)),
            HealthSempreOk());

        var plan = await router.PlanejarRotaAsync(
            new RoutingContext(empresaId, "pix", 100m));

        plan.ProvedoresOrdenados.Should().ContainSingle().Which.Should().Be("CustomGateway");
        plan.Motivo.Should().Be("tenant-override");
    }

    [Fact]
    public async Task ValorAbaixoDaFaixaMinima_DescartaRegra()
    {
        var router = new PagamentoGatewayRouter(
            new[] { StubGateway("EfiPix", "pix") },
            ComRegras(GatewayRoutingRule.Criar(
                "pix", "EfiPix", prioridade: 1, empresaId: null,
                valorMinimoCentavos: 10_000)), // R$ 100,00
            HealthSempreOk());

        var plan = await router.PlanejarRotaAsync(
            new RoutingContext(Guid.NewGuid(), "pix", 50m)); // R$ 50

        plan.ProvedoresOrdenados.Should().BeEmpty();
        plan.Motivo.Should().Be("sem-regra-na-faixa-de-valor");
    }

    [Fact]
    public async Task ProvedorJaTentado_PuladoNaProximaIteracao()
    {
        var router = new PagamentoGatewayRouter(
            new[] {
                StubGateway("EfiPix", "pix"),
                StubGateway("Stripe", "pix")
            },
            ComRegras(
                GatewayRoutingRule.Criar("pix", "EfiPix", prioridade: 1, empresaId: null),
                GatewayRoutingRule.Criar("pix", "Stripe", prioridade: 2, empresaId: null)),
            HealthSempreOk());

        var plan = await router.PlanejarRotaAsync(
            new RoutingContext(Guid.NewGuid(), "pix", 100m,
                ProvedoresJaTentados: new[] { "EfiPix" }));

        plan.ProvedoresOrdenados.Should().ContainSingle().Which.Should().Be("Stripe");
    }

    [Fact]
    public async Task GatewayNaoRegistradoNoDI_FiltradoMesmoComRegra()
    {
        // Regra existe mas nao ha gateway "Stripe" registrado no DI — descarta.
        var router = new PagamentoGatewayRouter(
            new[] { StubGateway("EfiPix", "pix") },
            ComRegras(
                GatewayRoutingRule.Criar("pix", "Stripe", prioridade: 1, empresaId: null),
                GatewayRoutingRule.Criar("pix", "EfiPix", prioridade: 2, empresaId: null)),
            HealthSempreOk());

        var plan = await router.PlanejarRotaAsync(
            new RoutingContext(Guid.NewGuid(), "pix", 100m));

        plan.ProvedoresOrdenados.Should().ContainSingle().Which.Should().Be("EfiPix");
    }

    [Fact]
    public async Task HealthStoreSuspende_PulaGateway_E_AnotaMotivo()
    {
        var health = Substitute.For<IGatewayHealthStore>();
        health.PodeUsar("Stripe").Returns(false);
        health.PodeUsar("EfiPix").Returns(true);

        var router = new PagamentoGatewayRouter(
            new[] {
                StubGateway("Stripe", "pix"),
                StubGateway("EfiPix", "pix")
            },
            ComRegras(
                GatewayRoutingRule.Criar("pix", "Stripe", prioridade: 1, empresaId: null),
                GatewayRoutingRule.Criar("pix", "EfiPix", prioridade: 2, empresaId: null)),
            health);

        var plan = await router.PlanejarRotaAsync(
            new RoutingContext(Guid.NewGuid(), "pix", 100m));

        plan.ProvedoresOrdenados.Should().ContainSingle().Which.Should().Be("EfiPix");
        plan.Motivo.Should().Contain("health-skip");
    }

    [Fact]
    public async Task MetodoVazio_RetornaPlanoVazio()
    {
        var router = new PagamentoGatewayRouter(
            new[] { StubGateway("EfiPix", "pix") },
            ComRegras(),
            HealthSempreOk());

        var plan = await router.PlanejarRotaAsync(
            new RoutingContext(Guid.NewGuid(), "", 100m));

        plan.ProvedoresOrdenados.Should().BeEmpty();
        plan.Motivo.Should().Be("metodo-vazio");
    }
}
