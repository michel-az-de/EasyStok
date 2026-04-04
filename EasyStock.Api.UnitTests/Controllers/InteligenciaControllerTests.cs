using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace EasyStock.Api.UnitTests.Controllers;

public class InteligenciaControllerTests
{
    private readonly IItemEstoqueRepository _itemEstoqueRepository = Substitute.For<IItemEstoqueRepository>();
    private readonly InteligenciaController _controller;

    public InteligenciaControllerTests()
    {
        _controller = new InteligenciaController(_itemEstoqueRepository);
    }

    [Fact]
    public async Task EstoqueBaixo_DeveRetornarOk_ComItensAbaixoDoLimite()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var item1 = new ItemEstoque { Id = Guid.NewGuid(), QuantidadeAtual = Quantidade.From(5) };
        var item2 = new ItemEstoque { Id = Guid.NewGuid(), QuantidadeAtual = Quantidade.From(15) };
        var itens = new List<ItemEstoque> { item1 };
        _itemEstoqueRepository.GetEstoqueBaixoAsync(empresaId, 10, 1, 20).Returns((itens, 1));

        // Act
        var result = await _controller.EstoqueBaixo(empresaId, 10);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        var returnedItens = ObterPropriedade<IEnumerable<ItemEstoque>>(okResult!.Value, "Items");
        returnedItens.Should().ContainSingle().Which.Should().Be(item1);
    }

    [Fact]
    public async Task ProximoVencimento_DeveRetornarOk_ComItensProximosAoVencimento()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var hoje = DateTime.UtcNow.Date;
        var item1 = new ItemEstoque { Id = Guid.NewGuid(), ValidadeEm = Validade.From(hoje.AddDays(25)) };
        var itens = new List<ItemEstoque> { item1 };
        _itemEstoqueRepository.GetProximoVencimentoAsync(empresaId, 30, 1, 20).Returns((itens, 1));

        // Act
        var result = await _controller.ProximoVencimento(empresaId, 30);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        var returnedItens = ObterPropriedade<IEnumerable<ItemEstoque>>(okResult!.Value, "Items");
        returnedItens.Should().ContainSingle().Which.Should().Be(item1);
    }

    [Fact]
    public async Task ItensParados_DeveRetornarOk_ComItensSemMovimentacao()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var hoje = DateTime.UtcNow;
        var item1 = new ItemEstoque { Id = Guid.NewGuid(), UltimaMovimentacaoEm = hoje.AddDays(-100) };
        var itens = new List<ItemEstoque> { item1 };
        _itemEstoqueRepository.GetItensParadosAsync(empresaId, 90, 1, 20).Returns((itens, 1));

        // Act
        var result = await _controller.ItensParados(empresaId, 90);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = result as OkObjectResult;
        var returnedItens = ObterPropriedade<IEnumerable<ItemEstoque>>(okResult!.Value, "Items");
        returnedItens.Should().ContainSingle().Which.Should().Be(item1);
    }

    [Fact]
    public async Task SugestaoReposicao_DeveRetornarOk_ComItensComEstoqueBaixo()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var item1 = new ItemEstoque { Id = Guid.NewGuid(), QuantidadeAtual = Quantidade.From(3) };
        var itens = new List<ItemEstoque> { item1 };
        _itemEstoqueRepository.GetSugestaoReposicaoAsync(empresaId, 1, 20).Returns((itens, 1));

        // Act
        var result = await _controller.SugestaoReposicao(empresaId);

        // Assert
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
