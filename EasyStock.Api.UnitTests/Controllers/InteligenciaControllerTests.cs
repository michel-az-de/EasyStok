using EasyStock.Api.Configuration;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public class InteligenciaControllerTests
{
    private readonly IItemEstoqueRepository _itemEstoqueRepository = Substitute.For<IItemEstoqueRepository>();
    private readonly IMovimentacaoEstoqueRepository _movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
    private readonly IConfiguracaoLojaRepository _configuracaoLojaRepository = Substitute.For<IConfiguracaoLojaRepository>();
    private readonly InteligenciaController _controller;

    public InteligenciaControllerTests()
    {
        var config = Options.Create(new EasyStockConfiguracoes
        {
            LimiteEstoqueBaixoDefault = 5,
            DiasAlertaVencimento = 30,
            DiasItemParado = 90
        });
        _controller = new InteligenciaController(_itemEstoqueRepository, _movimentacaoRepository, _configuracaoLojaRepository, config);
    }

    [Fact]
    public async Task EstoqueBaixo_DeveRetornarOk_ComItensAbaixoDoLimite()
    {
        var empresaId = Guid.NewGuid();
        var item1 = new ItemEstoque { Id = Guid.NewGuid(), QuantidadeAtual = Quantidade.From(5) };
        var itens = new List<ItemEstoque> { item1 };
        _itemEstoqueRepository.GetEstoqueBaixoAsync(empresaId, 10, 1, 20, null).Returns((itens, 1));

        var result = await _controller.EstoqueBaixo(empresaId, null, 10);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        var returnedItens = ObterPropriedade<IEnumerable<ItemEstoque>>(okResult!.Value, "Items");
        returnedItens.Should().ContainSingle().Which.Should().Be(item1);
    }

    [Fact]
    public async Task ProximoVencimento_DeveUsarConfiguracaoDaLoja_QuandoDiasNaoInformado()
    {
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var hoje = DateTime.UtcNow.Date;
        var item1 = new ItemEstoque { Id = Guid.NewGuid(), ValidadeEm = Validade.From(hoje.AddDays(25)) };
        var config = ConfiguracaoLoja.CriarPadrao(lojaId);
        config!.DiasAlertaValidade = 15;
        _configuracaoLojaRepository.GetByLojaIdAsync(lojaId).Returns(config);
        _itemEstoqueRepository.GetProximoVencimentoAsync(empresaId, 15, 1, 20, lojaId).Returns((new[] { item1 }, 1));

        var result = await _controller.ProximoVencimento(empresaId, lojaId, null);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task ItensParados_DeveRetornarOk_ComItensSemMovimentacao()
    {
        var empresaId = Guid.NewGuid();
        var hoje = DateTime.UtcNow;
        var item1 = new ItemEstoque { Id = Guid.NewGuid(), UltimaMovimentacaoEm = hoje.AddDays(-100) };
        _itemEstoqueRepository.GetItensParadosAsync(empresaId, 90, 1, 20, null).Returns((new[] { item1 }, 1));

        var result = await _controller.ItensParados(empresaId, null, 90);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        var returnedItens = ObterPropriedade<IEnumerable<ItemEstoque>>(okResult!.Value, "Items");
        returnedItens.Should().ContainSingle().Which.Should().Be(item1);
    }

    [Fact]
    public async Task SugestaoReposicao_DeveRetornarOk_ComItensComEstoqueBaixo()
    {
        var empresaId = Guid.NewGuid();
        var item1 = new ItemEstoque { Id = Guid.NewGuid(), QuantidadeAtual = Quantidade.From(3) };
        _itemEstoqueRepository.GetSugestaoReposicaoAsync(empresaId, 5, 1, 20, null).Returns((new[] { item1 }, 1));

        var result = await _controller.SugestaoReposicao(empresaId, null);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        var returnedItens = ObterPropriedade<IEnumerable<ItemEstoque>>(okResult!.Value, "Items");
        returnedItens.Should().ContainSingle().Which.Should().Be(item1);
    }

    private static T ObterPropriedade<T>(object? source, string nome)
    {
        source.Should().NotBeNull();
        var propriedade = source!.GetType().GetProperty(nome);
        propriedade.Should().NotBeNull();
        return (T)propriedade!.GetValue(source)!;
    }
}
