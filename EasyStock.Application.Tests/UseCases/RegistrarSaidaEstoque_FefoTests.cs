using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

public class RegistrarSaidaEstoque_FefoTests
{
    private static (RegistrarSaidaEstoqueUseCase UseCase,
                    IItemEstoqueRepository ItemRepo,
                    IConfiguracaoLojaRepository ConfigRepo)
        Build(bool fifoAtivo, Guid empresaId, Guid produtoId, ItemEstoque lote)
    {
        var produtoRepo = Substitute.For<IProdutoRepository>();
        var itemRepo = Substitute.For<IItemEstoqueRepository>();
        var vendaRepo = Substitute.For<IVendaRepository>();
        var itemVendaRepo = Substitute.For<IItemVendaRepository>();
        var movRepo = Substitute.For<IMovimentacaoEstoqueRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SetupExecuteInTransactionForward<RegistrarSaidaEstoqueResult>();
        var configRepo = Substitute.For<IConfiguracaoLojaRepository>();
        var logger = Substitute.For<ILogger<RegistrarSaidaEstoqueUseCase>>();

        var produto = new Produto { Id = produtoId, EmpresaId = empresaId, Nome = "P", Status = StatusProduto.Ativo };
        produtoRepo.GetByIdAsync(produtoId).Returns(produto);
        movRepo.GetTaxaSaidaDiariaAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(0m);

        var config = ConfiguracaoLoja.CriarPadrao(empresaId);
        config.FifoAtivo = fifoAtivo;
        configRepo.GetByLojaIdAsync(empresaId).Returns(config);

        // stub only the call with matching fefo value
        itemRepo.GetLotesDisponiveisParaSaidaAsync(empresaId, produtoId, null, fifoAtivo).Returns([lote]);
        itemRepo.GetLotesDisponiveisParaSaidaAsync(empresaId, produtoId, null, !fifoAtivo).Returns([]);

        uow.SetupExecuteInTransaction<RegistrarSaidaEstoqueResult>();

        var useCase = new RegistrarSaidaEstoqueUseCase(
            produtoRepo, itemRepo, vendaRepo, itemVendaRepo, movRepo, uow, logger,
            configuracaoLojaRepository: configRepo);

        return (useCase, itemRepo, configRepo);
    }

    private static RegistrarSaidaEstoqueCommand Cmd(Guid empresaId, Guid produtoId, int qty = 1) =>
        new(empresaId,
            [new RegistrarSaidaEstoqueItemCommand(produtoId, null, qty, 10m, null)],
            DateTime.UtcNow, DateTime.UtcNow, null, null,
            NaturezaMovimentacaoEstoque.Venda, CanalVenda.LojaPropria, null);

    [Fact]
    public async Task FifoAtivo_true_chama_repositorio_com_fefo_true()
    {
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var lote = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            QuantidadeAtual = Quantidade.From(5),
            QuantidadeInicial = Quantidade.From(5),
            CustoUnitario = Dinheiro.FromDecimal(1m),
            Status = StatusItemEstoque.Ok,
            EntradaEm = DateTime.UtcNow.AddDays(-1),
            ValidadeEm = Validade.From(DateTime.UtcNow.AddDays(30))
        };

        var (useCase, itemRepo, _) = Build(fifoAtivo: true, empresaId, produtoId, lote);
        await useCase.ExecuteAsync(Cmd(empresaId, produtoId));

        await itemRepo.Received(1).GetLotesDisponiveisParaSaidaAsync(empresaId, produtoId, null, fefo: true);
        await itemRepo.DidNotReceive().GetLotesDisponiveisParaSaidaAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), fefo: false);
    }

    [Fact]
    public async Task FifoAtivo_false_chama_repositorio_com_fefo_false()
    {
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var lote = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            QuantidadeAtual = Quantidade.From(5),
            QuantidadeInicial = Quantidade.From(5),
            CustoUnitario = Dinheiro.FromDecimal(1m),
            Status = StatusItemEstoque.Ok,
            EntradaEm = DateTime.UtcNow.AddDays(-1)
        };

        var (useCase, itemRepo, _) = Build(fifoAtivo: false, empresaId, produtoId, lote);
        await useCase.ExecuteAsync(Cmd(empresaId, produtoId));

        await itemRepo.Received(1).GetLotesDisponiveisParaSaidaAsync(empresaId, produtoId, null, fefo: false);
        await itemRepo.DidNotReceive().GetLotesDisponiveisParaSaidaAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), fefo: true);
    }
}
