using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Events;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

public class RegistrarSaidaEstoqueUseCaseTests
{
    [Fact]
    public async Task Deve_registrar_saida_multi_item_e_baixar_quantidade_do_estoque()
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
            EmpresaId = Guid.NewGuid(),
            Nome = "Galaxy Buds FE",
            Status = StatusProduto.Ativo
        };

        var item1 = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = produto.EmpresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(10),
            QuantidadeInicial = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(250m),
            Status = StatusItemEstoque.Ativo,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var item2 = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = produto.EmpresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(5),
            QuantidadeInicial = Quantidade.From(5),
            CustoUnitario = Dinheiro.FromDecimal(250m),
            Status = StatusItemEstoque.Ativo,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);
        itemRepository.GetByIdAsync(item1.Id).Returns(item1);
        itemRepository.GetByIdAsync(item2.Id).Returns(item2);

        var useCase = new RegistrarSaidaEstoqueUseCase(
            produtoRepository,
            itemRepository,
            vendaRepository,
            itemVendaRepository,
            movimentacaoRepository,
            unitOfWork);

        var result = await useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            produto.EmpresaId,
            [
                new RegistrarSaidaEstoqueItemCommand(item1.Id, 3, 399.90m, "Venda Mercado Livre"),
                new RegistrarSaidaEstoqueItemCommand(item2.Id, 2, 399.90m, "Venda Mercado Livre")
            ],
            new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 3, 12, 5, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 4, 12, 0, 0, DateTimeKind.Utc),
            "NF-123",
            NaturezaMovimentacaoEstoque.Venda,
            CanalVenda.MercadoLivre,
            "Observacao"));

        result.Itens.Should().HaveCount(2);
        result.ValorTotal.Should().Be(1999.50m);

        await itemRepository.Received(1).UpdateAsync(Arg.Is<ItemEstoque>(i =>
            i.Id == item1.Id &&
            i.QuantidadeAtual.Value == 7));

        await itemRepository.Received(1).UpdateAsync(Arg.Is<ItemEstoque>(i =>
            i.Id == item2.Id &&
            i.QuantidadeAtual.Value == 3));

        await vendaRepository.Received(1).InsertAsync(Arg.Is<Venda>(v =>
            v.Id == result.VendaId &&
            v.Natureza == NaturezaMovimentacaoEstoque.Venda));

        await itemVendaRepository.Received(2).InsertAsync(Arg.Any<ItemVenda>());

        await movimentacaoRepository.Received(2).InsertAsync(Arg.Is<MovimentacaoEstoque>(m =>
            m.Tipo == TipoMovimentacaoEstoque.Saida));

        await unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Deve_falhar_quando_item_esta_bloqueado()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
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
            CustoUnitario = Dinheiro.FromDecimal(250m),
            Status = StatusItemEstoque.Bloqueado,
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

        var act = () => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            empresaId,
            [new RegistrarSaidaEstoqueItemCommand(item.Id, 1, 399.90m, "Venda bloqueada")],
            new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 3, 12, 5, 0, DateTimeKind.Utc),
            null,
            null,
            NaturezaMovimentacaoEstoque.Venda,
            CanalVenda.MercadoLivre,
            null));

        await act.Should().ThrowAsync<ItemEstoqueBloqueadoException>();

        await vendaRepository.DidNotReceive().InsertAsync(Arg.Any<Venda>());
        await itemVendaRepository.DidNotReceive().InsertAsync(Arg.Any<ItemVenda>());
        await movimentacaoRepository.DidNotReceive().InsertAsync(Arg.Any<MovimentacaoEstoque>());
        await unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Deve_falhar_quando_estoque_e_insuficiente()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
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
            QuantidadeAtual = Quantidade.From(2),
            QuantidadeInicial = Quantidade.From(2),
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

        var act = () => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            empresaId,
            [new RegistrarSaidaEstoqueItemCommand(item.Id, 3, 399.90m, "Venda sem saldo")],
            new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 3, 12, 5, 0, DateTimeKind.Utc),
            null,
            null,
            NaturezaMovimentacaoEstoque.Venda,
            CanalVenda.MercadoLivre,
            null));

        await act.Should().ThrowAsync<EstoqueInsuficienteException>();

        await vendaRepository.DidNotReceive().InsertAsync(Arg.Any<Venda>());
        await itemVendaRepository.DidNotReceive().InsertAsync(Arg.Any<ItemVenda>());
        await movimentacaoRepository.DidNotReceive().InsertAsync(Arg.Any<MovimentacaoEstoque>());
        await unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Deve_falhar_quando_item_nao_pertence_a_empresa()
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
            EmpresaId = Guid.NewGuid(),
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

        itemRepository.GetByIdAsync(item.Id).Returns(item);

        var useCase = new RegistrarSaidaEstoqueUseCase(
            produtoRepository,
            itemRepository,
            vendaRepository,
            itemVendaRepository,
            movimentacaoRepository,
            unitOfWork);

        var act = () => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            Guid.NewGuid(),
            [new RegistrarSaidaEstoqueItemCommand(item.Id, 1, 399.90m, "Venda invalida")],
            new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 3, 12, 5, 0, DateTimeKind.Utc),
            null,
            null,
            NaturezaMovimentacaoEstoque.Venda,
            CanalVenda.MercadoLivre,
            null));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*nao pertence a empresa*");

        await vendaRepository.DidNotReceive().InsertAsync(Arg.Any<Venda>());
        await itemVendaRepository.DidNotReceive().InsertAsync(Arg.Any<ItemVenda>());
        await movimentacaoRepository.DidNotReceive().InsertAsync(Arg.Any<MovimentacaoEstoque>());
        await unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Deve_falhar_quando_produto_esta_inativo()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
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

        var act = () => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            empresaId,
            [new RegistrarSaidaEstoqueItemCommand(item.Id, 1, 399.90m, "Venda invalida")],
            new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 3, 12, 5, 0, DateTimeKind.Utc),
            null,
            null,
            NaturezaMovimentacaoEstoque.Venda,
            CanalVenda.MercadoLivre,
            null));

        await act.Should().ThrowAsync<ProdutoInativoException>();
        await vendaRepository.DidNotReceive().InsertAsync(Arg.Any<Venda>());
        await itemVendaRepository.DidNotReceive().InsertAsync(Arg.Any<ItemVenda>());
        await movimentacaoRepository.DidNotReceive().InsertAsync(Arg.Any<MovimentacaoEstoque>());
        await unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Deve_falhar_quando_item_esta_vencido()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
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
            CustoUnitario = Dinheiro.FromDecimal(250m),
            Status = StatusItemEstoque.Ativo,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            ValidadeEm = Validade.From(new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc))
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

        var act = () => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            empresaId,
            [new RegistrarSaidaEstoqueItemCommand(item.Id, 1, 399.90m, "Venda vencida")],
            new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 3, 12, 5, 0, DateTimeKind.Utc),
            null,
            null,
            NaturezaMovimentacaoEstoque.Venda,
            CanalVenda.MercadoLivre,
            null));

        await act.Should().ThrowAsync<ItemEstoqueVencidoException>();
        await vendaRepository.DidNotReceive().InsertAsync(Arg.Any<Venda>());
        await itemVendaRepository.DidNotReceive().InsertAsync(Arg.Any<ItemVenda>());
        await movimentacaoRepository.DidNotReceive().InsertAsync(Arg.Any<MovimentacaoEstoque>());
        await unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Nao_deve_persistir_venda_parcial_quando_segundo_item_falha()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
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

        var itemValido = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(10),
            QuantidadeInicial = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(250m),
            Status = StatusItemEstoque.Ativo,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var itemSemSaldo = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(1),
            QuantidadeInicial = Quantidade.From(1),
            CustoUnitario = Dinheiro.FromDecimal(250m),
            Status = StatusItemEstoque.Ativo,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);
        itemRepository.GetByIdAsync(itemValido.Id).Returns(itemValido);
        itemRepository.GetByIdAsync(itemSemSaldo.Id).Returns(itemSemSaldo);

        var useCase = new RegistrarSaidaEstoqueUseCase(
            produtoRepository,
            itemRepository,
            vendaRepository,
            itemVendaRepository,
            movimentacaoRepository,
            unitOfWork);

        var act = () => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            empresaId,
            [
                new RegistrarSaidaEstoqueItemCommand(itemValido.Id, 2, 399.90m, "Item 1"),
                new RegistrarSaidaEstoqueItemCommand(itemSemSaldo.Id, 3, 399.90m, "Item 2")
            ],
            new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 3, 12, 5, 0, DateTimeKind.Utc),
            null,
            null,
            NaturezaMovimentacaoEstoque.Venda,
            CanalVenda.MercadoLivre,
            "Pedido com falha"));

        await act.Should().ThrowAsync<EstoqueInsuficienteException>();

        await vendaRepository.DidNotReceive().InsertAsync(Arg.Any<Venda>());
        await itemVendaRepository.DidNotReceive().InsertAsync(Arg.Any<ItemVenda>());
        await movimentacaoRepository.DidNotReceive().InsertAsync(Arg.Any<MovimentacaoEstoque>());
        await unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Deve_publicar_eventos_de_saida_com_payload_real()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var publicadorEventos = Substitute.For<IPublicadorEventos>();
        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Galaxy Buds FE",
            Status = StatusProduto.Ativo,
            DescricaoBase = "Fone bluetooth"
        };

        var item = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
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
            unitOfWork,
            publicadorEventos);

        var result = await useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            empresaId,
            [new RegistrarSaidaEstoqueItemCommand(item.Id, 3, 399.90m, "Venda Mercado Livre")],
            new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 3, 12, 5, 0, DateTimeKind.Utc),
            null,
            "NF-123",
            NaturezaMovimentacaoEstoque.Venda,
            CanalVenda.MercadoLivre,
            "Pedido importante"));

        await publicadorEventos.Received(1).PublicarAsync(Arg.Is<VendaRegistrada>(e =>
            e.VendaId == result.VendaId &&
            e.EmpresaId == empresaId &&
            e.ValorTotal == result.ValorTotal));

        await publicadorEventos.Received(1).PublicarAsync(Arg.Is<SaidaEstoqueRegistrada>(e =>
            e.ItemEstoqueId == item.Id &&
            e.ProdutoId == produto.Id &&
            e.EmpresaId == empresaId &&
            e.Quantidade == 3 &&
            e.Motivo == "Pedido importante"));
    }
}
