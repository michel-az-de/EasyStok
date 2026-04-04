using EasyStock.Api.Configuration;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EasyStock.Api.UnitTests.Controllers;

public class InteligenciaControllerTests
{
    private readonly IItemEstoqueRepository _itemEstoqueRepository = Substitute.For<IItemEstoqueRepository>();
    private readonly IMovimentacaoEstoqueRepository _movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
    private readonly InteligenciaController _controller;

    public InteligenciaControllerTests()
    {
        var config = Options.Create(new EasyStockConfiguracoes
        {
            LimiteEstoqueBaixoDefault = 5,
            DiasAlertaVencimento = 30,
            DiasItemParado = 90
        });
        _controller = new InteligenciaController(_itemEstoqueRepository, _movimentacaoRepository, config);
    }

    [Fact]
    public async Task EstoqueBaixo_DeveRetornarOk_ComItensAbaixoDoLimite()
    {
        var empresaId = Guid.NewGuid();
        var item1 = new ItemEstoque { Id = Guid.NewGuid(), QuantidadeAtual = Quantidade.From(5) };
        var itens = new List<ItemEstoque> { item1 };
        _itemEstoqueRepository.GetEstoqueBaixoAsync(empresaId, 10, 1, 20).Returns((itens, 1));

        var result = await _controller.EstoqueBaixo(empresaId, 10);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        var returnedItens = ObterPropriedade<IEnumerable<ItemEstoque>>(okResult!.Value, "Items");
        returnedItens.Should().ContainSingle().Which.Should().Be(item1);
    }

    [Fact]
    public async Task ProximoVencimento_DeveRetornarOk_ComItensProximosAoVencimento()
    {
        var empresaId = Guid.NewGuid();
        var hoje = DateTime.UtcNow.Date;
        var item1 = new ItemEstoque { Id = Guid.NewGuid(), ValidadeEm = Validade.From(hoje.AddDays(25)) };
        var itens = new List<ItemEstoque> { item1 };
        _itemEstoqueRepository.GetProximoVencimentoAsync(empresaId, 30, 1, 20).Returns((itens, 1));

        var result = await _controller.ProximoVencimento(empresaId, 30);

        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        var returnedItens = ObterPropriedade<IEnumerable<ItemEstoque>>(okResult!.Value, "Items");
        returnedItens.Should().ContainSingle().Which.Should().Be(item1);
    }

    [Fact]
    public async Task ItensParados_DeveRetornarOk_ComItensSemMovimentacao()
    {
        var empresaId = Guid.NewGuid();
        var hoje = DateTime.UtcNow;
        var item1 = new ItemEstoque { Id = Guid.NewGuid(), UltimaMovimentacaoEm = hoje.AddDays(-100) };
        var itens = new List<ItemEstoque> { item1 };
        _itemEstoqueRepository.GetItensParadosAsync(empresaId, 90, 1, 20).Returns((itens, 1));

        var result = await _controller.ItensParados(empresaId, 90);

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
        var itens = new List<ItemEstoque> { item1 };
        _itemEstoqueRepository.GetSugestaoReposicaoAsync(empresaId, 5, 1, 20).Returns((itens, 1));

        var result = await _controller.SugestaoReposicao(empresaId);

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
