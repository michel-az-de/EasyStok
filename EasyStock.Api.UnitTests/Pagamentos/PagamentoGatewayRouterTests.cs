using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Async.Pagamentos;
using FluentAssertions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Pagamentos;

/// <summary>
/// Cobertura para o roteador de gateways. Foco: resolucao por metodo,
/// resolucao por provedor, fallback null quando nao ha match.
/// </summary>
public class PagamentoGatewayRouterTests
{
    private static IPagamentoGateway StubGateway(string provedor, params string[] metodos)
    {
        var g = Substitute.For<IPagamentoGateway>();
        g.Provedor.Returns(provedor);
        g.SuportaMetodo(Arg.Any<string>())
            .Returns(call => metodos.Contains((string)call[0], StringComparer.OrdinalIgnoreCase));
        return g;
    }

    private static IGatewayRoutingRuleRepository NoOpRules()
    {
        var r = Substitute.For<IGatewayRoutingRuleRepository>();
        r.ObterRegrasAplicaveisAsync(Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<EasyStock.Domain.Entities.Pagamentos.GatewayRoutingRule>());
        return r;
    }

    private static IGatewayHealthStore NoOpHealth()
    {
        var h = Substitute.For<IGatewayHealthStore>();
        h.PodeUsar(Arg.Any<string>()).Returns(true);
        return h;
    }

    [Fact]
    public void Resolver_RetornaPrimeiroQueSuportaMetodo()
    {
        var pix = StubGateway("EfiPix", "pix");
        var manual = StubGateway("Manual", "manual", "dinheiro");
        var router = new PagamentoGatewayRouter(new[] { pix, manual }, NoOpRules(), NoOpHealth());

        router.Resolver(Guid.NewGuid(), "pix").Should().BeSameAs(pix);
        router.Resolver(Guid.NewGuid(), "dinheiro").Should().BeSameAs(manual);
        router.Resolver(Guid.NewGuid(), "MANUAL").Should().BeSameAs(manual); // case insensitive
    }

    [Fact]
    public void Resolver_RetornaNullQuandoNenhumGatewaySuporta()
    {
        var pix = StubGateway("EfiPix", "pix");
        var router = new PagamentoGatewayRouter(new[] { pix }, NoOpRules(), NoOpHealth());

        router.Resolver(Guid.NewGuid(), "cripto").Should().BeNull();
        router.Resolver(Guid.NewGuid(), "").Should().BeNull();
        router.Resolver(Guid.NewGuid(), null!).Should().BeNull();
    }

    [Fact]
    public void Resolver_SemGateways_RetornaNull()
    {
        var router = new PagamentoGatewayRouter(Array.Empty<IPagamentoGateway>(), NoOpRules(), NoOpHealth());
        router.Resolver(Guid.NewGuid(), "pix").Should().BeNull();
    }

    [Fact]
    public void ResolverPorProvedor_LookupCaseInsensitive()
    {
        var pix = StubGateway("EfiPix", "pix");
        var manual = StubGateway("Manual", "manual");
        var router = new PagamentoGatewayRouter(new[] { pix, manual }, NoOpRules(), NoOpHealth());

        router.ResolverPorProvedor("EfiPix").Should().BeSameAs(pix);
        router.ResolverPorProvedor("efipix").Should().BeSameAs(pix);
        router.ResolverPorProvedor("MANUAL").Should().BeSameAs(manual);
    }

    [Fact]
    public void ResolverPorProvedor_RetornaNullQuandoNaoExiste()
    {
        var pix = StubGateway("EfiPix", "pix");
        var router = new PagamentoGatewayRouter(new[] { pix }, NoOpRules(), NoOpHealth());

        router.ResolverPorProvedor("Stripe").Should().BeNull();
        router.ResolverPorProvedor("").Should().BeNull();
        router.ResolverPorProvedor(null!).Should().BeNull();
    }
}

/// <summary>Cobertura do <see cref="ManualGatewayAdapter"/>.</summary>
public class ManualGatewayAdapterTests
{
    [Fact]
    public void SuportaMetodo_AceitaMetodosManuaisCommonn()
    {
        var g = new ManualGatewayAdapter();

        g.SuportaMetodo("manual").Should().BeTrue();
        g.SuportaMetodo("dinheiro").Should().BeTrue();
        g.SuportaMetodo("transferencia").Should().BeTrue();
        g.SuportaMetodo("cheque").Should().BeTrue();
        g.SuportaMetodo("outro").Should().BeTrue();

        g.SuportaMetodo("pix").Should().BeFalse();
        g.SuportaMetodo("boleto").Should().BeFalse();
        g.SuportaMetodo("cartao").Should().BeFalse();
    }

    [Fact]
    public async Task CriarAsync_RetornaInstrucaoVaziaComProvedorManual()
    {
        var g = new ManualGatewayAdapter();
        var fatura = Fatura.Criar(
            Guid.NewGuid(), "2026-000001",
            new EasyStock.Domain.ValueObjects.DadosFaturado("Cliente"),
            new EasyStock.Domain.ValueObjects.DadosEmissor("Empresa"),
            EasyStock.Domain.Enums.OrigemFatura.Avulsa,
            DateTime.UtcNow, DateTime.UtcNow.AddDays(7));

        var instr = await g.CriarAsync(fatura, "dinheiro");

        instr.Provedor.Should().Be("Manual");
        instr.TransactionId.Should().Contain(fatura.Id.ToString("N"));
        instr.PixCopiaCola.Should().BeNull();
        instr.BoletoUrl.Should().BeNull();
    }

    [Fact]
    public async Task ConsultarAsync_SemprePendente()
    {
        var g = new ManualGatewayAdapter();
        var status = await g.ConsultarAsync("manual-xyz");
        status.Should().Be(StatusGateway.Pendente);
    }
}
