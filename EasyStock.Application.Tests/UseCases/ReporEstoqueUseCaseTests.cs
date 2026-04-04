using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.ReporEstoque;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

public class ReporEstoqueUseCaseTests
{
    [Fact]
    public async Task Deve_repor_sem_desbloquear_item_bloqueado()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var empresaId = Guid.NewGuid();
        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Galaxy Buds FE",
            Status = StatusProduto.Ativo
        };

        var item = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(1),
            QuantidadeInicial = Quantidade.From(1),
            CustoUnitario = Dinheiro.FromDecimal(250m),
            Status = StatusItemEstoque.Bloqueado,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        itemRepository.GetByIdAsync(item.Id).Returns(item);
        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);

        var useCase = new ReporEstoqueUseCase(produtoRepository, itemRepository, movimentacaoRepository, unitOfWork);

        var result = await useCase.ExecuteAsync(new ReporEstoqueCommand(
            empresaId,
            item.Id,
            5,
            null,
            null,
            new DateTime(2026, 4, 3, 10, 0, 0, DateTimeKind.Utc),
            null,
            null,
            null,
            null,
            null,
            null,
            null));

        result.QuantidadeAtual.Should().Be(6);

        await itemRepository.Received(1).UpdateAsync(Arg.Is<ItemEstoque>(i =>
            i.Status == StatusItemEstoque.Bloqueado &&
            i.QuantidadeAtual.Value == 6));
    }

    [Fact]
    public async Task Deve_falhar_quando_item_nao_pertence_a_empresa()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var empresaId = Guid.NewGuid();
        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Galaxy Buds FE",
            Status = StatusProduto.Ativo
        };

        var item = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = Guid.NewGuid(),
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(1),
            QuantidadeInicial = Quantidade.From(1),
            CustoUnitario = Dinheiro.FromDecimal(250m),
            Status = StatusItemEstoque.Ativo,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        itemRepository.GetByIdAsync(item.Id).Returns(item);

        var useCase = new ReporEstoqueUseCase(produtoRepository, itemRepository, movimentacaoRepository, unitOfWork);

        var act = () => useCase.ExecuteAsync(new ReporEstoqueCommand(
            empresaId,
            item.Id,
            5,
            null,
            null,
            new DateTime(2026, 4, 3, 10, 0, 0, DateTimeKind.Utc),
            null,
            null,
            null,
            null,
            null,
            null,
            null));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*nao pertence a empresa*");

        await itemRepository.DidNotReceive().UpdateAsync(Arg.Any<ItemEstoque>());
        await movimentacaoRepository.DidNotReceive().AddAsync(Arg.Any<MovimentacaoEstoque>());
        await unitOfWork.DidNotReceive().CommitAsync();
    }
}
