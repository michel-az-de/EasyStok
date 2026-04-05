using EasyStock.Api.Configuration;
using EasyStock.Api.Services;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Services;

public class GeradorNotificacoesAutomaticasTests
{
    [Fact]
    public async Task Deve_gerar_notificacoes_sem_duplicar_no_mesmo_dia()
    {
        var empresaRepository = Substitute.For<IEmpresaRepository>();
        var estoqueRepository = Substitute.For<IItemEstoqueRepository>();
        var notificacaoRepository = Substitute.For<INotificacaoRepository>();
        var pedidoRepository = Substitute.For<IPedidoFornecedorRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<GeradorNotificacoesAutomaticas>>();
        var config = Options.Create(new EasyStockConfiguracoes
        {
            LimiteEstoqueBaixoDefault = 5,
            DiasAlertaVencimento = 30,
            DiasItemParado = 30
        });

        var empresaId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        empresaRepository.GetAllAsync().Returns(new[] { new Empresa { Id = empresaId, Nome = "Empresa" } });
        estoqueRepository.GetEstoqueBaixoAsync(empresaId, Arg.Any<int>(), 1, 100)
            .Returns((new[]
            {
                new ItemEstoque
                {
                    Id = itemId,
                    EmpresaId = empresaId,
                    QuantidadeAtual = Quantidade.From(1),
                    QuantidadeMinima = 5,
                    CodigoInterno = "CAP3426"
                }
            }, 1));
        estoqueRepository.GetEstoqueBaixoAsync(empresaId, Arg.Any<int>(), 2, 100).Returns((Array.Empty<ItemEstoque>(), 1));
        estoqueRepository.GetProximoVencimentoAsync(empresaId, Arg.Any<int>(), 1, 100).Returns((Array.Empty<ItemEstoque>(), 0));
        estoqueRepository.GetItensParadosAsync(empresaId, Arg.Any<int>(), 1, 100).Returns((Array.Empty<ItemEstoque>(), 0));
        estoqueRepository.GetSugestaoReposicaoAsync(empresaId, Arg.Any<int>(), 1, 100).Returns((Array.Empty<ItemEstoque>(), 0));
        pedidoRepository.GetPedidosAtrasadosAsync(empresaId, Arg.Any<DateTime>()).Returns(Array.Empty<PedidoFornecedor>());
        pedidoRepository.GetPedidosRecebidosNoPeriodoAsync(empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(Array.Empty<PedidoFornecedor>());
        notificacaoRepository.ExisteNotificacaoDoDiaAsync(empresaId, TipoAlertaEstoque.EstoqueCritico, itemId, Arg.Any<DateTime>()).Returns(false);

        var service = new GeradorNotificacoesAutomaticas(
            empresaRepository,
            estoqueRepository,
            notificacaoRepository,
            pedidoRepository,
            unitOfWork,
            config,
            logger);

        await service.ExecutarAsync();

        await notificacaoRepository.Received(1).AddAsync(Arg.Is<Notificacao>(n =>
            n.EmpresaId == empresaId &&
            n.TipoAlerta == TipoAlertaEstoque.EstoqueCritico &&
            n.ReferenciaId == itemId));
        await unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Deve_gerar_alertas_de_pedido_atrasado_e_pedido_recebido()
    {
        var empresaRepository = Substitute.For<IEmpresaRepository>();
        var estoqueRepository = Substitute.For<IItemEstoqueRepository>();
        var notificacaoRepository = Substitute.For<INotificacaoRepository>();
        var pedidoRepository = Substitute.For<IPedidoFornecedorRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<GeradorNotificacoesAutomaticas>>();
        var config = Options.Create(new EasyStockConfiguracoes());

        var empresaId = Guid.NewGuid();
        var pedidoAtrasadoId = Guid.NewGuid();
        var pedidoRecebidoId = Guid.NewGuid();
        empresaRepository.GetAllAsync().Returns(new[] { new Empresa { Id = empresaId, Nome = "Empresa" } });
        estoqueRepository.GetEstoqueBaixoAsync(empresaId, Arg.Any<int>(), 1, 100).Returns((Array.Empty<ItemEstoque>(), 0));
        estoqueRepository.GetProximoVencimentoAsync(empresaId, Arg.Any<int>(), 1, 100).Returns((Array.Empty<ItemEstoque>(), 0));
        estoqueRepository.GetItensParadosAsync(empresaId, Arg.Any<int>(), 1, 100).Returns((Array.Empty<ItemEstoque>(), 0));
        estoqueRepository.GetSugestaoReposicaoAsync(empresaId, Arg.Any<int>(), 1, 100).Returns((Array.Empty<ItemEstoque>(), 0));
        pedidoRepository.GetPedidosAtrasadosAsync(empresaId, Arg.Any<DateTime>()).Returns(new[]
        {
            new PedidoFornecedor { Id = pedidoAtrasadoId, EmpresaId = empresaId, PrevisaoEntrega = DateTime.UtcNow.AddDays(-2), Status = StatusPedidoFornecedor.EmTransito }
        });
        pedidoRepository.GetPedidosRecebidosNoPeriodoAsync(empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(new[]
        {
            new PedidoFornecedor { Id = pedidoRecebidoId, EmpresaId = empresaId, DataRecebimento = DateTime.UtcNow, Status = StatusPedidoFornecedor.Recebido }
        });
        notificacaoRepository.ExisteNotificacaoDoDiaAsync(empresaId, TipoAlertaEstoque.PedidoAtrasado, pedidoAtrasadoId, Arg.Any<DateTime>()).Returns(false);
        notificacaoRepository.ExisteNotificacaoDoDiaAsync(empresaId, TipoAlertaEstoque.PedidoRecebido, pedidoRecebidoId, Arg.Any<DateTime>()).Returns(false);

        var service = new GeradorNotificacoesAutomaticas(
            empresaRepository,
            estoqueRepository,
            notificacaoRepository,
            pedidoRepository,
            unitOfWork,
            config,
            logger);

        await service.ExecutarAsync();

        await notificacaoRepository.Received(1).AddAsync(Arg.Is<Notificacao>(n => n.TipoAlerta == TipoAlertaEstoque.PedidoAtrasado && n.ReferenciaId == pedidoAtrasadoId));
        await notificacaoRepository.Received(1).AddAsync(Arg.Is<Notificacao>(n => n.TipoAlerta == TipoAlertaEstoque.PedidoRecebido && n.ReferenciaId == pedidoRecebidoId));
    }
}
