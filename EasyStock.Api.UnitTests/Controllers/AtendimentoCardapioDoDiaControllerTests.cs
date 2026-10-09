using System.Reflection;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// #1241 (decisão do Felipe, 08/10/2026): disponibilidade e saldo do item são do Operador; ajustar
/// saldo ainda exige a permissão de estoque, como a rota de desacertos.
/// </summary>
public class AtendimentoCardapioDoDiaControllerTests
{
    [Fact]
    public void CardapioDoDia_EDoOperador()
    {
        typeof(AtendimentoCardapioDoDiaController).GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy).Should().ContainSingle().Which.Should().Be("Operador");
    }

    [Fact]
    public async Task AjustarSaldo_SemPermissaoDeEstoque_403()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.EmpresaId.Returns(Guid.NewGuid());
        currentUser.TemPermissao(Permissao.GerenciarEstoque).Returns(false);
        // O use case não é alcançado: a permissão barra antes.
        var controller = new AtendimentoCardapioDoDiaController(null!, currentUser);

        var r = await controller.Saldo(Guid.NewGuid(), new AjustarSaldoItemRequest(3m, "contei"), CancellationToken.None);

        r.Should().BeOfType<ForbidResult>();
    }
}

/// <summary>#1241 (decisão do Felipe, 08/10/2026): editar o item do cardápio é do Gerente.</summary>
public class AtendimentoItensCardapioControllerTests
{
    [Fact]
    public void ItensDoCardapio_SaoDoGerente()
    {
        typeof(AtendimentoItensCardapioController).GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy).Should().ContainSingle().Which.Should().Be("Gerente");
    }
}

/// <summary>M1.3 (#1483): as categorias do cardápio são do Gerente, como o resto da autoria.</summary>
public class AtendimentoSecoesCardapioControllerTests
{
    [Fact]
    public void CategoriasDoCardapio_SaoDoGerente()
    {
        typeof(AtendimentoSecoesCardapioController).GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy).Should().ContainSingle().Which.Should().Be("Gerente");
    }
}

/// <summary>M2.1 (#1490): estoque do dia é do Operador, mas ver o estoque exige a permissão de estoque.</summary>
public class AtendimentoProducaoControllerTests
{
    [Fact]
    public void Producao_EDoOperador()
    {
        typeof(AtendimentoProducaoController).GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy).Should().ContainSingle().Which.Should().Be("Operador");
    }

    [Fact]
    public async Task EstoqueDoDia_SemPermissaoDeEstoque_403()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.TemPermissao(Permissao.GerenciarEstoque).Returns(false);
        var controller = new AtendimentoProducaoController(null!, null!, null!, null!, currentUser);

        (await controller.EstoqueDoDia(CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await controller.Insumos(CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await controller.Receitas(CancellationToken.None)).Should().BeOfType<ForbidResult>();
    }

    [Theory]
    [InlineData(nameof(AtendimentoProducaoController.CriarInsumo))]
    [InlineData(nameof(AtendimentoProducaoController.AtualizarInsumo))]
    public void CadastroDeInsumo_EDoGerente(string acao)
    {
        typeof(AtendimentoProducaoController).GetMethod(acao)!.GetCustomAttributes<AuthorizeAttribute>()
            .Select(a => a.Policy).Should().Contain("Gerente");
    }
}
