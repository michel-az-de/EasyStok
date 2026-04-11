using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.ReporEstoque;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
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
            Status = StatusItemEstoque.Ok,
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
        await movimentacaoRepository.DidNotReceive().InsertAsync(Arg.Any<MovimentacaoEstoque>());
        await unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Deve_registrar_movimentacao_e_commit_ao_repor_item_ativo()
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
            QuantidadeAtual = Quantidade.From(10),
            QuantidadeInicial = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(150m),
            Status = StatusItemEstoque.Ok,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        itemRepository.GetByIdAsync(item.Id).Returns(item);
        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);

        var useCase = new ReporEstoqueUseCase(produtoRepository, itemRepository, movimentacaoRepository, unitOfWork);

        var result = await useCase.ExecuteAsync(new ReporEstoqueCommand(
            empresaId,
            item.Id,
            5,
            180m,
            239.90m,
            new DateTime(2026, 4, 3, 10, 0, 0, DateTimeKind.Utc),
            "Preto / M",
            "Preto",
            "M",
            "Reposicao teste",
            "DOC-REP-01",
            new DimensoesInput(0.4m, 10m, 5m, 12m),
            new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc)));

        result.QuantidadeAnterior.Should().Be(10);
        result.QuantidadeAtual.Should().Be(15);

        await itemRepository.Received(1).UpdateAsync(Arg.Is<ItemEstoque>(i =>
            i.Id == item.Id &&
            i.QuantidadeAtual.Value == 15 &&
            i.CustoUnitario.Valor == 180m));

        await movimentacaoRepository.Received(1).InsertAsync(Arg.Is<MovimentacaoEstoque>(m =>
            m.ItemEstoqueId == item.Id &&
            m.Natureza == NaturezaMovimentacaoEstoque.Reposicao &&
            m.Tipo == TipoMovimentacaoEstoque.Entrada));

        await unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Deve_falhar_quando_produto_esta_inativo()
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
            Status = StatusProduto.Inativo
        };

        var item = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(10),
            QuantidadeInicial = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(150m),
            Status = StatusItemEstoque.Ok,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        itemRepository.GetByIdAsync(item.Id).Returns(item);
        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);

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

        await act.Should().ThrowAsync<ProdutoInativoException>();
        await itemRepository.DidNotReceive().UpdateAsync(Arg.Any<ItemEstoque>());
        await movimentacaoRepository.DidNotReceive().InsertAsync(Arg.Any<MovimentacaoEstoque>());
        await unitOfWork.DidNotReceive().CommitAsync();
    }
}
