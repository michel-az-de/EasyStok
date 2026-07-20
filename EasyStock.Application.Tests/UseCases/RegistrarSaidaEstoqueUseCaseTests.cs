using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Domain.Events;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

public class RegistrarSaidaEstoqueUseCaseTests
{
    [Fact]
    public async Task Deve_consumir_lotes_em_fifo_automaticamente()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SetupExecuteInTransactionSemRetryForward<RegistrarSaidaEstoqueResult>();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = Guid.NewGuid(),
            Nome = "Galaxy Buds FE",
            Status = StatusProduto.Ativo
        };

        var loteAntigo = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = produto.EmpresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(10),
            QuantidadeInicial = Quantidade.From(10),
            QuantidadeMinima = 5,
            CustoUnitario = Dinheiro.FromDecimal(250m),
            Status = StatusItemEstoque.Ok,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var loteNovo = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = produto.EmpresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(5),
            QuantidadeInicial = Quantidade.From(5),
            QuantidadeMinima = 5,
            CustoUnitario = Dinheiro.FromDecimal(250m),
            Status = StatusItemEstoque.Ok,
            EntradaEm = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc)
        };

        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);
        itemRepository.GetLotesDisponiveisParaSaidaAsync(produto.EmpresaId, produto.Id, null, Arg.Any<bool>())
            .Returns([loteAntigo, loteNovo]);
        movimentacaoRepository.GetTaxaSaidaDiariaAsync(produto.EmpresaId, produto.Id, Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(0m);

        var logger = Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>();
        unitOfWork.SetupExecuteInTransactionSemRetry<RegistrarSaidaEstoqueResult>();
        var useCase = new RegistrarSaidaEstoqueUseCase(
            produtoRepository,
            itemRepository,
            vendaRepository,
            itemVendaRepository,
            movimentacaoRepository,
            unitOfWork,
            logger,
            publicadorEventos: Substitute.For<IPublicadorEventos>()); // #306

        var result = await useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            produto.EmpresaId,
            [new RegistrarSaidaEstoqueItemCommand(produto.Id, null, 12, 399.90m, "Venda FIFO")],
            new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 3, 12, 5, 0, DateTimeKind.Utc),
            null,
            "NF-123",
            NaturezaMovimentacaoEstoque.Venda,
            CanalVenda.MercadoLivre,
            "FIFO"));

        result.Itens.Should().HaveCount(2);
        result.Itens.Select(i => i.QuantidadeSaida).Should().BeEquivalentTo([10, 2]);

        await itemRepository.Received(1).UpdateRangeAsync(Arg.Is<IEnumerable<ItemEstoque>>(lotes =>
            lotes.Any(l => l.Id == loteAntigo.Id && l.QuantidadeAtual.Value == 0) &&
            lotes.Any(l => l.Id == loteNovo.Id && l.QuantidadeAtual.Value == 3)));

        await itemVendaRepository.Received(1).InsertRangeAsync(Arg.Is<IEnumerable<ItemVenda>>(x => x.Count() == 2));
        await movimentacaoRepository.Received(1).InsertRangeAsync(Arg.Is<IEnumerable<MovimentacaoEstoque>>(x => x.Count() == 2));
        await unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Saida_acima_do_saldo_com_PermitirDescoberto_nao_trava_e_registra_descoberto()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SetupExecuteInTransactionSemRetryForward<RegistrarSaidaEstoqueResult>();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = Guid.NewGuid(),
            Nome = "Vassoura",
            Status = StatusProduto.Ativo
        };

        var lote = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = produto.EmpresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(3),
            QuantidadeInicial = Quantidade.From(3),
            QuantidadeMinima = 5,
            CustoUnitario = Dinheiro.FromDecimal(10m),
            Status = StatusItemEstoque.Ok,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);
        itemRepository.GetByIdComLockAsync(produto.EmpresaId, lote.Id).Returns(lote);
        movimentacaoRepository.GetTaxaSaidaDiariaAsync(produto.EmpresaId, produto.Id, Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(0m);

        var logger = Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>();
        unitOfWork.SetupExecuteInTransactionSemRetry<RegistrarSaidaEstoqueResult>();
        var useCase = new RegistrarSaidaEstoqueUseCase(
            produtoRepository,
            itemRepository,
            vendaRepository,
            itemVendaRepository,
            movimentacaoRepository,
            unitOfWork,
            logger,
            publicadorEventos: Substitute.For<IPublicadorEventos>());

        var result = await useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            produto.EmpresaId,
            [new RegistrarSaidaEstoqueItemCommand(lote.Id, 999, 20m, "Venda a descoberto")],
            new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 3, 12, 5, 0, DateTimeKind.Utc),
            null,
            null,
            NaturezaMovimentacaoEstoque.Venda,
            CanalVenda.MercadoLivre,
            "QuickSaida",
            PermitirDescoberto: true));

        // A venda sai inteira (999 x R$20 = R$19.980, receita real)...
        result.Itens.Should().HaveCount(1);
        result.Itens.Single().QuantidadeSaida.Should().Be(999);
        result.ValorTotal.Should().Be(999 * 20m);

        // ...e a falta vira descoberto auditavel no lote, sem fabricar estoque.
        lote.QuantidadeAtual.Value.Should().Be(0);
        lote.QuantidadeDescoberta.Value.Should().Be(996);
        await unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Deve_registrar_saida_multi_item_e_baixar_quantidade_do_estoque()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SetupExecuteInTransactionSemRetryForward<RegistrarSaidaEstoqueResult>();

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
            Status = StatusItemEstoque.Ok,
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
            Status = StatusItemEstoque.Ok,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);
        itemRepository.GetByIdComLockAsync(Arg.Any<Guid>(), item1.Id).Returns(item1);
        itemRepository.GetByIdComLockAsync(Arg.Any<Guid>(), item2.Id).Returns(item2);

        var logger = Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>();
        unitOfWork.SetupExecuteInTransactionSemRetry<RegistrarSaidaEstoqueResult>();
        var useCase = new RegistrarSaidaEstoqueUseCase(
            produtoRepository,
            itemRepository,
            vendaRepository,
            itemVendaRepository,
            movimentacaoRepository,
            unitOfWork,
            logger,
            publicadorEventos: Substitute.For<IPublicadorEventos>()); // #306

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

        await itemRepository.Received(1).UpdateRangeAsync(Arg.Is<IEnumerable<ItemEstoque>>(lotes =>
            lotes.Any(l => l.Id == item1.Id && l.QuantidadeAtual.Value == 7)));

        await itemRepository.Received(1).UpdateRangeAsync(Arg.Is<IEnumerable<ItemEstoque>>(lotes =>
            lotes.Any(l => l.Id == item2.Id && l.QuantidadeAtual.Value == 3)));

        await vendaRepository.Received(1).InsertAsync(Arg.Is<Venda>(v =>
            v.Id == result.VendaId &&
            v.Natureza == NaturezaMovimentacaoEstoque.Venda));

        await itemVendaRepository.Received(1).InsertRangeAsync(Arg.Is<IEnumerable<ItemVenda>>(x => x.Count() == 2));

        await movimentacaoRepository.Received(1).InsertRangeAsync(Arg.Is<IEnumerable<MovimentacaoEstoque>>(x =>
            x.All(m => m.Tipo == TipoMovimentacaoEstoque.Saida) && x.Count() == 2));

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
        unitOfWork.SetupExecuteInTransactionSemRetryForward<RegistrarSaidaEstoqueResult>();

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
        itemRepository.GetByIdComLockAsync(Arg.Any<Guid>(), item.Id).Returns(item);

        var logger = Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>();
        unitOfWork.SetupExecuteInTransactionSemRetry<RegistrarSaidaEstoqueResult>();
        var useCase = new RegistrarSaidaEstoqueUseCase(
            produtoRepository,
            itemRepository,
            vendaRepository,
            itemVendaRepository,
            movimentacaoRepository,
            unitOfWork,
            logger);

        var act =() => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
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
        unitOfWork.SetupExecuteInTransactionSemRetryForward<RegistrarSaidaEstoqueResult>();

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
            Status = StatusItemEstoque.Ok,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);
        itemRepository.GetByIdComLockAsync(Arg.Any<Guid>(), item.Id).Returns(item);

        var logger = Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>();
        unitOfWork.SetupExecuteInTransactionSemRetry<RegistrarSaidaEstoqueResult>();
        var useCase = new RegistrarSaidaEstoqueUseCase(
            produtoRepository,
            itemRepository,
            vendaRepository,
            itemVendaRepository,
            movimentacaoRepository,
            unitOfWork,
            logger);

        var act =() => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
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
    public async Task Deve_falhar_quando_soma_dos_lotes_em_fifo_e_insuficiente()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SetupExecuteInTransactionSemRetryForward<RegistrarSaidaEstoqueResult>();

        var empresaId = Guid.NewGuid();
        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Galaxy Buds FE",
            Status = StatusProduto.Ativo
        };

        itemRepository.GetLotesDisponiveisParaSaidaAsync(empresaId, produto.Id, null, Arg.Any<bool>())
            .Returns([
                new ItemEstoque
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = empresaId,
                    ProdutoId = produto.Id,
                    QuantidadeAtual = Quantidade.From(2),
                    QuantidadeInicial = Quantidade.From(2),
                    CustoUnitario = Dinheiro.FromDecimal(250m),
                    Status = StatusItemEstoque.Ok,
                    EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new ItemEstoque
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = empresaId,
                    ProdutoId = produto.Id,
                    QuantidadeAtual = Quantidade.From(1),
                    QuantidadeInicial = Quantidade.From(1),
                    CustoUnitario = Dinheiro.FromDecimal(250m),
                    Status = StatusItemEstoque.Ok,
                    EntradaEm = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc)
                }
            ]);
        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);

        var logger = Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>();
        unitOfWork.SetupExecuteInTransactionSemRetry<RegistrarSaidaEstoqueResult>();
        var useCase = new RegistrarSaidaEstoqueUseCase(
            produtoRepository,
            itemRepository,
            vendaRepository,
            itemVendaRepository,
            movimentacaoRepository,
            unitOfWork,
            logger);

        var act = () => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            empresaId,
            [new RegistrarSaidaEstoqueItemCommand(produto.Id, null, 5, 399.90m, "Sem saldo FIFO")],
            new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 3, 12, 5, 0, DateTimeKind.Utc),
            null,
            null,
            NaturezaMovimentacaoEstoque.Venda,
            CanalVenda.MercadoLivre,
            null));

        await act.Should().ThrowAsync<EstoqueInsuficienteException>();
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
        unitOfWork.SetupExecuteInTransactionSemRetryForward<RegistrarSaidaEstoqueResult>();

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
            Status = StatusItemEstoque.Ok,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        itemRepository.GetByIdComLockAsync(Arg.Any<Guid>(), item.Id).Returns(item);

        var logger = Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>();
        unitOfWork.SetupExecuteInTransactionSemRetry<RegistrarSaidaEstoqueResult>();
        var useCase = new RegistrarSaidaEstoqueUseCase(
            produtoRepository,
            itemRepository,
            vendaRepository,
            itemVendaRepository,
            movimentacaoRepository,
            unitOfWork,
            logger);

        var act =() => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
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
        unitOfWork.SetupExecuteInTransactionSemRetryForward<RegistrarSaidaEstoqueResult>();
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
            Status = StatusItemEstoque.Ok,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);
        itemRepository.GetByIdComLockAsync(Arg.Any<Guid>(), item.Id).Returns(item);

        var logger = Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>();
        unitOfWork.SetupExecuteInTransactionSemRetry<RegistrarSaidaEstoqueResult>();
        var useCase = new RegistrarSaidaEstoqueUseCase(
            produtoRepository,
            itemRepository,
            vendaRepository,
            itemVendaRepository,
            movimentacaoRepository,
            unitOfWork,
            logger);

        var act =() => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
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
        unitOfWork.SetupExecuteInTransactionSemRetryForward<RegistrarSaidaEstoqueResult>();
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
            Status = StatusItemEstoque.Ok,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            ValidadeEm = Validade.From(new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc))
        };

        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);
        itemRepository.GetByIdComLockAsync(Arg.Any<Guid>(), item.Id).Returns(item);

        var logger = Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>();
        unitOfWork.SetupExecuteInTransactionSemRetry<RegistrarSaidaEstoqueResult>();
        var useCase = new RegistrarSaidaEstoqueUseCase(
            produtoRepository,
            itemRepository,
            vendaRepository,
            itemVendaRepository,
            movimentacaoRepository,
            unitOfWork,
            logger);

        var act =() => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
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

    // #983 (D1): pelo FEFO/ProdutoId (sem ItemEstoqueId direto), o repositorio corrigido
    // ja exclui lotes vencidos do resultado quando incluirVencidos=false — o mock aqui
    // modela esse contrato pos-fix. Antes do fix, um lote vencido na frente da lista
    // FEFO derrubava a venda inteira com ItemEstoqueVencidoException mesmo havendo saldo
    // valido suficiente; agora a venda consome so o lote valido.
    [Fact]
    public async Task Venda_por_produto_consome_so_o_lote_valido_quando_ha_lote_vencido_no_mesmo_produto()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SetupExecuteInTransactionForward<RegistrarSaidaEstoqueResult>();
        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Galaxy Buds FE",
            Status = StatusProduto.Ativo
        };

        var loteValido = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(10),
            QuantidadeInicial = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(250m),
            Status = StatusItemEstoque.Ok,
            EntradaEm = new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
            ValidadeEm = Validade.From(new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc))
        };

        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);
        // Repositorio pos-fix: incluirVencidos=false (Venda nao permite baixa de vencido)
        // ja retorna so o lote valido — o lote vencido nem chega no use case.
        itemRepository.GetLotesDisponiveisParaSaidaAsync(empresaId, produto.Id, null, Arg.Any<bool>(), incluirVencidos: false)
            .Returns([loteValido]);
        movimentacaoRepository.GetTaxaSaidaDiariaAsync(empresaId, produto.Id, Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(0m);

        var logger = Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>();
        unitOfWork.SetupExecuteInTransaction<RegistrarSaidaEstoqueResult>();
        var useCase = new RegistrarSaidaEstoqueUseCase(
            produtoRepository,
            itemRepository,
            vendaRepository,
            itemVendaRepository,
            movimentacaoRepository,
            unitOfWork,
            logger,
            publicadorEventos: Substitute.For<IPublicadorEventos>());

        var result = await useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            empresaId,
            [new RegistrarSaidaEstoqueItemCommand(produto.Id, null, 4, 399.90m, "Venda com lote vencido no meio")],
            new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 3, 12, 5, 0, DateTimeKind.Utc),
            null,
            null,
            NaturezaMovimentacaoEstoque.Venda,
            CanalVenda.MercadoLivre,
            null));

        result.Itens.Should().ContainSingle().Which.ItemEstoqueId.Should().Be(loteValido.Id);
        await itemRepository.Received(1).GetLotesDisponiveisParaSaidaAsync(empresaId, produto.Id, null, Arg.Any<bool>(), incluirVencidos: false);
        await unitOfWork.Received(1).CommitAsync();
    }

    // #983 (D2): natureza Perda fura o guard de vencido — bloqueio absoluto derrubava
    // qualquer tentativa de dar baixa/descarte num lote 100% vencido.
    [Fact]
    public async Task Perda_sobre_lote_100_por_cento_vencido_tem_sucesso()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SetupExecuteInTransactionForward<RegistrarSaidaEstoqueResult>();
        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Galaxy Buds FE",
            Status = StatusProduto.Ativo
        };

        var loteVencido = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(3),
            QuantidadeInicial = Quantidade.From(3),
            CustoUnitario = Dinheiro.FromDecimal(250m),
            Status = StatusItemEstoque.Ok,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            ValidadeEm = Validade.From(new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc))
        };

        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);
        // Perda pede incluirVencidos=true — repositorio devolve o lote vencido.
        itemRepository.GetLotesDisponiveisParaSaidaAsync(empresaId, produto.Id, null, Arg.Any<bool>(), incluirVencidos: true)
            .Returns([loteVencido]);
        movimentacaoRepository.GetTaxaSaidaDiariaAsync(empresaId, produto.Id, Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(0m);

        var logger = Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>();
        unitOfWork.SetupExecuteInTransaction<RegistrarSaidaEstoqueResult>();
        var useCase = new RegistrarSaidaEstoqueUseCase(
            produtoRepository,
            itemRepository,
            vendaRepository,
            itemVendaRepository,
            movimentacaoRepository,
            unitOfWork,
            logger,
            publicadorEventos: Substitute.For<IPublicadorEventos>());

        var result = await useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            empresaId,
            [new RegistrarSaidaEstoqueItemCommand(produto.Id, null, 3, 0m, "Descarte de lote vencido")],
            new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 3, 12, 5, 0, DateTimeKind.Utc),
            null,
            null,
            NaturezaMovimentacaoEstoque.Perda,
            CanalVenda.LojaPropria,
            null));

        result.Itens.Should().ContainSingle().Which.QuantidadeRestante.Should().Be(0);
        await itemRepository.Received(1).GetLotesDisponiveisParaSaidaAsync(empresaId, produto.Id, null, Arg.Any<bool>(), incluirVencidos: true);
        await unitOfWork.Received(1).CommitAsync();
    }

    // #983 (D3): quando so resta lote vencido e a natureza NAO permite baixa de vencido,
    // "disponivel" deve excluir esse lote (repositorio pos-fix ja devolve lista vazia) —
    // erro correto e EstoqueInsuficienteException, nao mais ItemEstoqueVencidoException
    // depois de somar erroneamente a quantidade de um lote que nunca seria vendido.
    [Fact]
    public async Task Venda_sem_lote_valido_quando_so_resta_vencido_falha_por_estoque_insuficiente()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var vendaRepository = Substitute.For<IVendaRepository>();
        var itemVendaRepository = Substitute.For<IItemVendaRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SetupExecuteInTransactionForward<RegistrarSaidaEstoqueResult>();
        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Galaxy Buds FE",
            Status = StatusProduto.Ativo
        };

        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);
        // Repositorio pos-fix: so havia lote vencido, incluirVencidos=false filtra tudo.
        itemRepository.GetLotesDisponiveisParaSaidaAsync(empresaId, produto.Id, null, Arg.Any<bool>(), incluirVencidos: false)
            .Returns(Array.Empty<ItemEstoque>());

        var logger = Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>();
        unitOfWork.SetupExecuteInTransaction<RegistrarSaidaEstoqueResult>();
        var useCase = new RegistrarSaidaEstoqueUseCase(
            produtoRepository,
            itemRepository,
            vendaRepository,
            itemVendaRepository,
            movimentacaoRepository,
            unitOfWork,
            logger,
            publicadorEventos: Substitute.For<IPublicadorEventos>());

        var act = () => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
            empresaId,
            [new RegistrarSaidaEstoqueItemCommand(produto.Id, null, 1, 399.90m, "Venda sem saldo valido")],
            new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 3, 12, 5, 0, DateTimeKind.Utc),
            null,
            null,
            NaturezaMovimentacaoEstoque.Venda,
            CanalVenda.MercadoLivre,
            null));

        await act.Should().ThrowAsync<EstoqueInsuficienteException>();
        await vendaRepository.DidNotReceive().InsertAsync(Arg.Any<Venda>());
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
        unitOfWork.SetupExecuteInTransactionSemRetryForward<RegistrarSaidaEstoqueResult>();
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
            Status = StatusItemEstoque.Ok,
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
            Status = StatusItemEstoque.Ok,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);
        itemRepository.GetByIdComLockAsync(Arg.Any<Guid>(), itemValido.Id).Returns(itemValido);
        itemRepository.GetByIdComLockAsync(Arg.Any<Guid>(), itemSemSaldo.Id).Returns(itemSemSaldo);

        var logger = Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>();
        unitOfWork.SetupExecuteInTransactionSemRetry<RegistrarSaidaEstoqueResult>();
        var useCase = new RegistrarSaidaEstoqueUseCase(
            produtoRepository,
            itemRepository,
            vendaRepository,
            itemVendaRepository,
            movimentacaoRepository,
            unitOfWork,
            logger);

        var act =() => useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
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
        unitOfWork.SetupExecuteInTransactionSemRetryForward<RegistrarSaidaEstoqueResult>();
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
            Status = StatusItemEstoque.Ok,
            EntradaEm = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);
        itemRepository.GetByIdComLockAsync(Arg.Any<Guid>(), item.Id).Returns(item);

        var logger = Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>();
        unitOfWork.SetupExecuteInTransactionSemRetry<RegistrarSaidaEstoqueResult>();
        var useCase = new RegistrarSaidaEstoqueUseCase(
            produtoRepository,
            itemRepository,
            vendaRepository,
            itemVendaRepository,
            movimentacaoRepository,
            unitOfWork,
            logger,
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
