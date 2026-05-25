using EasyStock.Application.UseCases.Storefront.Avaliacao;
using EasyStock.Domain.Exceptions.Storefront;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EasyStock.Application.Tests.UseCases.Storefront.Avaliacao;

/// <summary>
/// Testes do <see cref="AvaliacaoTokenService"/> (TASK-EZ-AVAL-001).
/// </summary>
public sealed class AvaliacaoTokenServiceTests
{
    private static AvaliacaoTokenService CriarService(string? secret = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Avaliacao:JwtSecret"] = secret ?? "test-secret-key-com-pelo-menos-32-chars!!",
            })
            .Build();
        return new AvaliacaoTokenService(config, TimeProvider.System);
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

        act.Should().Throw<AvaliacaoTokenInvalidoException>();
    }

    [Fact]
    public void Validar_TokenAdulterado_LancaExcecao()
    {
        var svc = CriarService();
        var pedidoId = Guid.NewGuid();
        var token = svc.Gerar(pedidoId);
        var adulterado = token[..^5] + "XXXXX";

        var act = () => svc.Validar(adulterado, pedidoId);

        act.Should().Throw<AvaliacaoTokenInvalidoException>();
    }

    [Fact]
    public void Validar_TokenMalformado_LancaExcecao()
    {
        var svc = CriarService();
        var pedidoId = Guid.NewGuid();

        var act = () => svc.Validar("nao.e.jwt.valido.demais", pedidoId);

        act.Should().Throw<AvaliacaoTokenInvalidoException>();
    }

    [Fact]
    public void Validar_SecretoDiferente_LancaExcecao()
    {
        var svc1 = CriarService("secret-A-com-pelo-menos-32-caracteres!");
        var svc2 = CriarService("secret-B-com-pelo-menos-32-caracteres!");
        var pedidoId = Guid.NewGuid();

        var token = svc1.Gerar(pedidoId);
        var act = () => svc2.Validar(token, pedidoId);

        act.Should().Throw<AvaliacaoTokenInvalidoException>();
    }

    [Fact]
    public void Validar_TokenExpirado_LancaExcecao()
    {
        var pedidoId = Guid.NewGuid();
        // Gera com TimeProvider que retorna data no passado (31 dias atrás)
        var pastTime = new FixedTimeProvider(DateTimeOffset.UtcNow.AddDays(-31));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Avaliacao:JwtSecret"] = "test-secret-key-com-pelo-menos-32-chars!!",
            })
            .Build();
        var svcPast = new AvaliacaoTokenService(config, pastTime);
        var token = svcPast.Gerar(pedidoId);

        var svcNow = new AvaliacaoTokenService(config, TimeProvider.System);
        var act = () => svcNow.Validar(token, pedidoId);

        act.Should().Throw<AvaliacaoTokenInvalidoException>();
    }
}

/// <summary>TimeProvider fixo para testes determinísticos.</summary>
file sealed class FixedTimeProvider(DateTimeOffset fixedTime) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => fixedTime;
}
