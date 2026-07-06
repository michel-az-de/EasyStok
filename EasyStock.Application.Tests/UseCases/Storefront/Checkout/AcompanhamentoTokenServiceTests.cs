using EasyStock.Application.UseCases.Storefront.Avaliacao;
using EasyStock.Application.UseCases.Storefront.Checkout;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Tests.UseCases.Storefront.Checkout;

/// <summary>
/// Testes do <see cref="AcompanhamentoTokenService"/> (issues #680/#684 — link
/// guest "Acompanhe seu pedido"). Espelha <see cref="Avaliacao.AvaliacaoTokenService"/>
/// e cobre tambem a segregacao de scope entre os dois servicos (issue #854).
/// </summary>
public sealed class AcompanhamentoTokenServiceTests
{
    private const string Secret = "test-secret-key-com-pelo-menos-32-chars!!";

    private static AcompanhamentoTokenService CriarService(
        string? secret = null, TimeProvider? timeProvider = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Acompanhamento:JwtSecret"] = secret ?? Secret,
            })
            .Build();
        return new AcompanhamentoTokenService(config, timeProvider ?? TimeProvider.System);
    }

    [Fact]
    public void Gerar_EValidadoParaMesmoPedido()
    {
        var svc = CriarService();
        var pedidoId = Guid.NewGuid();

        var token = svc.Gerar(pedidoId);
        var act = () => svc.Validar(token, pedidoId);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validar_TokenDePedidoDiferente_LancaExcecao()
    {
        var svc = CriarService();
        var token = svc.Gerar(Guid.NewGuid());

        var act = () => svc.Validar(token, Guid.NewGuid());

        act.Should().Throw<AcompanhamentoTokenInvalidoException>();
    }

    [Fact]
    public void Validar_TokenAdulterado_LancaExcecao()
    {
        var svc = CriarService();
        var pedidoId = Guid.NewGuid();
        var token = svc.Gerar(pedidoId);
        var adulterado = token[..^5] + "XXXXX";

        var act = () => svc.Validar(adulterado, pedidoId);

        act.Should().Throw<AcompanhamentoTokenInvalidoException>();
    }

    [Fact]
    public void Validar_TokenMalformado_LancaExcecao()
    {
        var svc = CriarService();

        var act = () => svc.Validar("nao.e.jwt.valido.demais", Guid.NewGuid());

        act.Should().Throw<AcompanhamentoTokenInvalidoException>();
    }

    [Fact]
    public void Validar_SecretoDiferente_LancaExcecao()
    {
        var svc1 = CriarService("secret-A-com-pelo-menos-32-caracteres!");
        var svc2 = CriarService("secret-B-com-pelo-menos-32-caracteres!");
        var pedidoId = Guid.NewGuid();

        var token = svc1.Gerar(pedidoId);
        var act = () => svc2.Validar(token, pedidoId);

        act.Should().Throw<AcompanhamentoTokenInvalidoException>();
    }

    [Fact]
    public void Validar_TokenExpirado_LancaExcecao()
    {
        var pedidoId = Guid.NewGuid();
        // Gera com relogio 31 dias no passado (TTL do token e 30 dias).
        var svcPassado = CriarService(
            timeProvider: new FixedTimeProvider(DateTimeOffset.UtcNow.AddDays(-31)));
        var token = svcPassado.Gerar(pedidoId);

        var svcAgora = CriarService();
        var act = () => svcAgora.Validar(token, pedidoId);

        act.Should().Throw<AcompanhamentoTokenInvalidoException>();
    }

    [Fact]
    public void Validar_TokenDeAvaliacao_NaoAbreAcompanhamento()
    {
        // Segregacao de scope ("aval" vs "acomp") — a razao de existirem dois
        // servicos: token de avaliacao nao pode abrir a pagina de status.
        // MESMO secret nos dois pra provar que a rejeicao e pelo scope,
        // nao pela assinatura.
        var pedidoId = Guid.NewGuid();
        var configAval = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Avaliacao:JwtSecret"] = Secret,
            })
            .Build();
        var tokenAval = new AvaliacaoTokenService(configAval, TimeProvider.System)
            .Gerar(pedidoId);

        var svcAcomp = CriarService();
        var act = () => svcAcomp.Validar(tokenAval, pedidoId);

        act.Should().Throw<AcompanhamentoTokenInvalidoException>();
    }
}

/// <summary>TimeProvider fixo para testes determinísticos.</summary>
file sealed class FixedTimeProvider(DateTimeOffset fixedTime) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => fixedTime;
}
