using EasyStock.Api.Services;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Analytics.Reposicao;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Reposicao;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Services;

public class GeradorNotificacoesAutomaticasTests
{
    private static async IAsyncEnumerable<Empresa> Stream(params Empresa[] empresas)
    {
        foreach (var e in empresas) yield return e;
        await Task.CompletedTask;
    }

    // ObterReposicaoUseCase e concreto (sem interface): monta-se um real com portas mockadas,
    // como em ObterReposicaoUseCaseTests. O snapshot da IAnalyticsRepository dirige o resultado.
    private static ObterReposicaoUseCase CriarReposicaoUseCase(
        IAnalyticsRepository analytics, IConfiguracaoLojaRepository config) =>
        new(analytics, config, Substitute.For<ILogger<ObterReposicaoUseCase>>());

    [Fact]
    public async Task Deve_gerar_notificacao_de_estoque_critico_por_produto_sem_duplicar_no_dia()
    {
        var empresaRepository = Substitute.For<IEmpresaRepository>();
        var lojaRepository = Substitute.For<ILojaRepository>();
        var configuracaoLojaRepository = Substitute.For<IConfiguracaoLojaRepository>();
        var estoqueRepository = Substitute.For<IItemEstoqueRepository>();
        var analytics = Substitute.For<IAnalyticsRepository>();
        var notificacaoRepository = Substitute.For<INotificacaoRepository>();
        var pedidoRepository = Substitute.For<IPedidoFornecedorRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<GeradorNotificacoesAutomaticas>>();

        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();

        empresaRepository.StreamAllAsync(Arg.Any<CancellationToken>())
            .Returns(Stream(new Empresa { Id = empresaId, Nome = "Empresa" }));
        lojaRepository.GetByEmpresaAsync(empresaId)
            .Returns(new[] { new Loja { Id = lojaId, EmpresaId = empresaId, Nome = "Loja", Ativa = true } });
        configuracaoLojaRepository.GetOrDefaultAsync(lojaId).Returns(ConfiguracaoLoja.CriarPadrao(lojaId));

        // Fonte unica: produto com vigente 1, minimo 5, critico 2 -> Estado CRITICO.
        analytics.GetSnapshotReposicaoAsync(empresaId, lojaId,
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ProdutoReposicaoSnapshot>
            {
                new(produtoId, null, "Cafe", 1m, 5, 2, null, null, null, null, 0m, 0, 7, 1, null, null)
            });

        // As trilhas de validade/parado continuam via estoqueRepository (nao migradas).
        estoqueRepository.GetProximoVencimentoAsync(empresaId, Arg.Any<int>(), 1, 100, lojaId).Returns((Array.Empty<ItemEstoque>(), 0));
        estoqueRepository.GetItensParadosAsync(empresaId, Arg.Any<int>(), 1, 100, lojaId).Returns((Array.Empty<ItemEstoque>(), 0));
        pedidoRepository.GetPedidosAtrasadosAsync(empresaId, Arg.Any<DateTime>()).Returns(Array.Empty<PedidoFornecedor>());
        pedidoRepository.GetPedidosRecebidosNoPeriodoAsync(empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(Array.Empty<PedidoFornecedor>());
        notificacaoRepository.ExisteNotificacaoDoDiaAsync(empresaId, Arg.Any<TipoAlertaEstoque>(), Arg.Any<Guid?>(), Arg.Any<DateTime>()).Returns(false);

        var service = new GeradorNotificacoesAutomaticas(
            empresaRepository,
            lojaRepository,
            configuracaoLojaRepository,
            estoqueRepository,
            CriarReposicaoUseCase(analytics, configuracaoLojaRepository),
            notificacaoRepository,
            pedidoRepository,
            unitOfWork,
            logger);

        await service.ExecutarAsync();

        // EstoqueCritico agora referencia o PRODUTO (nao o lote).
        await notificacaoRepository.Received(1).AddAsync(Arg.Is<Notificacao>(n =>
            n.EmpresaId == empresaId &&
            n.TipoAlerta == TipoAlertaEstoque.EstoqueCritico &&
            n.ReferenciaId == produtoId));
        // Produto critico tambem gera ReposicaoSugerida (mesma fonte, superset).
        await notificacaoRepository.Received(1).AddAsync(Arg.Is<Notificacao>(n =>
            n.TipoAlerta == TipoAlertaEstoque.ReposicaoSugerida &&
            n.ReferenciaId == produtoId));
        await unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Deve_gerar_alertas_de_pedido_atrasado_e_pedido_recebido()
    {
        var empresaRepository = Substitute.For<IEmpresaRepository>();
        var lojaRepository = Substitute.For<ILojaRepository>();
        var configuracaoLojaRepository = Substitute.For<IConfiguracaoLojaRepository>();
        var estoqueRepository = Substitute.For<IItemEstoqueRepository>();
        var analytics = Substitute.For<IAnalyticsRepository>();
        var notificacaoRepository = Substitute.For<INotificacaoRepository>();
        var pedidoRepository = Substitute.For<IPedidoFornecedorRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<GeradorNotificacoesAutomaticas>>();

        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var pedidoAtrasadoId = Guid.NewGuid();
        var pedidoRecebidoId = Guid.NewGuid();

        empresaRepository.StreamAllAsync(Arg.Any<CancellationToken>())
            .Returns(Stream(new Empresa { Id = empresaId, Nome = "Empresa" }));
        lojaRepository.GetByEmpresaAsync(empresaId)
            .Returns(new[] { new Loja { Id = lojaId, EmpresaId = empresaId, Nome = "Loja", Ativa = true } });
        configuracaoLojaRepository.GetOrDefaultAsync(lojaId).Returns(ConfiguracaoLoja.CriarPadrao(lojaId));
        analytics.GetSnapshotReposicaoAsync(empresaId, lojaId,
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ProdutoReposicaoSnapshot>());
        estoqueRepository.GetProximoVencimentoAsync(empresaId, Arg.Any<int>(), 1, 100, lojaId).Returns((Array.Empty<ItemEstoque>(), 0));
        estoqueRepository.GetItensParadosAsync(empresaId, Arg.Any<int>(), 1, 100, lojaId).Returns((Array.Empty<ItemEstoque>(), 0));
        pedidoRepository.GetPedidosAtrasadosAsync(empresaId, Arg.Any<DateTime>()).Returns(new[]
        {
            new PedidoFornecedor { Id = pedidoAtrasadoId, EmpresaId = empresaId, PrevisaoEntrega = DateTime.UtcNow.AddDays(-2), Status = StatusPedidoFornecedor.EmTransito }
        });
        pedidoRepository.GetPedidosRecebidosNoPeriodoAsync(empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(new[]
        {
            new PedidoFornecedor { Id = pedidoRecebidoId, EmpresaId = empresaId, DataRecebimento = DateTime.UtcNow, Status = StatusPedidoFornecedor.Recebido }
        });
        notificacaoRepository.ExisteNotificacaoDoDiaAsync(empresaId, Arg.Any<TipoAlertaEstoque>(), Arg.Any<Guid?>(), Arg.Any<DateTime>()).Returns(false);

        var service = new GeradorNotificacoesAutomaticas(
            empresaRepository,
            lojaRepository,
            configuracaoLojaRepository,
            estoqueRepository,
            CriarReposicaoUseCase(analytics, configuracaoLojaRepository),
            notificacaoRepository,
            pedidoRepository,
            unitOfWork,
            logger);

        await service.ExecutarAsync();

        await notificacaoRepository.Received(1).AddAsync(Arg.Is<Notificacao>(n => n.TipoAlerta == TipoAlertaEstoque.PedidoAtrasado && n.ReferenciaId == pedidoAtrasadoId));
        await notificacaoRepository.Received(1).AddAsync(Arg.Is<Notificacao>(n => n.TipoAlerta == TipoAlertaEstoque.PedidoRecebido && n.ReferenciaId == pedidoRecebidoId));
    }
}
