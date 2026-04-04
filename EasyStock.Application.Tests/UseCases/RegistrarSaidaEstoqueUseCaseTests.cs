using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

public class RegistrarSaidaEstoqueUseCaseTests
{
    [Fact]
    public async Task Deve_registrar_saida_e_baixar_quantidade_do_estoque()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            Nome = "Galaxy Buds FE",
            Status = StatusProduto.Ativo
        };

        var item = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = Guid.NewGuid(),
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(10),
            QuantidadeInicial = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(250m),
            Status = StatusItemEstoque.Ativo,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);
        itemRepository.GetByIdAsync(item.Id).Returns(item);

        var useCase = new RegistrarSaidaEstoqueUseCase(
            produtoRepository,
            itemRepository,
            vendaRepository,
            itemVendaRepository,
            movimentacaoRepository,
            unitOfWork);

        var result = await useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            item.Id,
            3,
            399.90m,
            "Venda Mercado Livre",
            new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 3, 12, 5, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 4, 12, 0, 0, DateTimeKind.Utc),
            "NF-123",
            NaturezaMovimentacaoEstoque.Venda,
            CanalVenda.MercadoLivre,
            "Observacao"));

        result.QuantidadeRestante.Should().Be(7);

        await itemRepository.Received(1).UpdateAsync(Arg.Is<ItemEstoque>(i =>
            i.Id == item.Id &&
            i.QuantidadeAtual.Value == 7));

        await vendaRepository.Received(1).AddAsync(Arg.Is<Venda>(v =>
            v.Id == result.VendaId &&
            v.Natureza == NaturezaMovimentacaoEstoque.Venda));

        await itemVendaRepository.Received(1).AddAsync(Arg.Is<ItemVenda>(iv =>
            iv.Id == result.ItemVendaId &&
            iv.Quantidade.Value == 3));

        await movimentacaoRepository.Received(1).AddAsync(Arg.Is<MovimentacaoEstoque>(m =>
            m.Id == result.MovimentacaoId &&
            m.Tipo == TipoMovimentacaoEstoque.Saida));

        await unitOfWork.Received(1).CommitAsync();
    }
}
