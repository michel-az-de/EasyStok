using System.Reflection;
using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Atendimento.Producao;
using EasyStock.Domain.Entities;
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
    public void ItensDoCardapio_OperadorConsultaGerenteAltera()
    {
        typeof(AtendimentoItensCardapioController).GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy).Should().ContainSingle().Which.Should().Be("Operador");
        foreach (var nome in new[] { "Fora", "Mover", "Incluir", "Editar", "Arquivar", "Validar", "Visivel" })
            typeof(AtendimentoItensCardapioController).GetMethod(nome)!.GetCustomAttributes<AuthorizeAttribute>()
                .Select(a => a.Policy).Should().Contain("Gerente", $"a ação {nome} continua reservada ao Gerente");
    }
}

/// <summary>M1.3 (#1483): as categorias do cardápio são do Gerente, como o resto da autoria.</summary>
public class AtendimentoSecoesCardapioControllerTests
{
    [Fact]
    public void CategoriasDoCardapio_OperadorConsultaGerenteAltera()
    {
        typeof(AtendimentoSecoesCardapioController).GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Select(a => a.Policy).Should().ContainSingle().Which.Should().Be("Operador");
        foreach (var nome in new[] { "Criar", "Renomear", "Visivel", "Mover", "Excluir", "MigrarCategorias" })
            typeof(AtendimentoSecoesCardapioController).GetMethod(nome)!.GetCustomAttributes<AuthorizeAttribute>()
                .Select(a => a.Policy).Should().Contain("Gerente", $"a ação {nome} continua reservada ao Gerente");
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
        var controller = new AtendimentoProducaoController(null!, null!, null!, null!, null!, null!, currentUser);

        (await controller.EstoqueDoDia(CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await controller.Insumos(CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await controller.Receitas(CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await controller.Sugestao(null, CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await controller.Planejar(new PlanejamentoRequest([]), CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await controller.Perdas(null, null, CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await controller.Vencidos(CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await controller.LancarPerda(new PerdaRequest(Guid.NewGuid(), null, 1, MotivoPerda.Vencido, null), CancellationToken.None))
            .Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task PerdaAcimaDoLimite_SemGerente_403ComAMensagem()
    {
        // M2.6 (#1511, D-M2-06): Operador com permissão de estoque, perda de R$ 100 > R$ 50.
        var empresaId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.TemPermissao(Permissao.GerenciarEstoque).Returns(true);
        currentUser.Nivel.Returns(NivelAcesso.Operador);
        currentUser.EmpresaId.Returns(empresaId);
        var produtoId = Guid.NewGuid();
        var itens = Substitute.For<IItemEstoqueRepository>();
        itens.GetLotesDisponiveisParaSaidaAsync(empresaId, produtoId, null, true, true).Returns([new ItemEstoque
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, ProdutoId = produtoId,
            QuantidadeAtual = EasyStock.Domain.ValueObjects.Quantidade.From(1), CustoUnitario = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(100m),
        }]);
        var perdas = new PerdasDaProducaoUseCase(itens, Substitute.For<IMovimentacaoEstoqueRepository>(), null!, TimeProvider.System);
        var controller = new AtendimentoProducaoController(null!, null!, null!, null!, null!, perdas, currentUser);

        var r = await controller.LancarPerda(new PerdaRequest(produtoId, null, 1, MotivoPerda.PerdaNoPreparo, null), CancellationToken.None);

        var objeto = r.Should().BeOfType<ObjectResult>().Subject;
        objeto.StatusCode.Should().Be(403);
        objeto.Value.Should().BeOfType<ApiErrorResponse>().Which.Error.Message.Should().Contain("só o Gerente lança");
    }

    [Theory]
    [InlineData(nameof(AtendimentoProducaoController.CriarInsumo))]
    [InlineData(nameof(AtendimentoProducaoController.AtualizarInsumo))]
    [InlineData(nameof(AtendimentoProducaoController.MarcarBaixaAutomatica))]
    public void CadastroDeInsumo_EDoGerente(string acao)
    {
        typeof(AtendimentoProducaoController).GetMethod(acao)!.GetCustomAttributes<AuthorizeAttribute>()
            .Select(a => a.Policy).Should().Contain("Gerente");
    }
}
