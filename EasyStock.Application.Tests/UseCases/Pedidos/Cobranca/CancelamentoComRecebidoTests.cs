using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Services;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.CancelarPedido;
using EasyStock.Application.UseCases.Pedidos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// #1717-5: o pedido pago e cancelado pelo console não registrava a devolução a fazer. O cancelamento grava na
/// trilha quanto há a devolver e a consulta de devoluções (Ficha › Devoluções) devolve o mesmo valor. Nada é
/// estornado sozinho: é o registro do que falta acertar com o cliente.
/// </summary>
public class CancelamentoComRecebidoTests
{
    [Fact]
    public async Task Cancelar_com_recebido_registra_a_devolucao_a_fazer_e_a_consulta_mostra_o_valor()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        f.AdicionarPagamento("pix-direto", 25m);

        await Cancelar(f).ExecuteAsync(new CancelarPedidoCommand(f.EmpresaId, f.Pedido.Id, Motivo: "Cliente desistiu",
            NivelSolicitante: NivelAcesso.Gerente));

        var evento = f.Eventos.Should().ContainSingle(e => e.Tipo == CancelarPedidoUseCase.EventoDevolucaoAFazer).Subject;
        evento.Detalhes.Should().Contain(25m.ToString("C", Cultura.PtBr));
        (await Consultar(f).ExecuteAsync(f.EmpresaId, f.Pedido.Id)).ADevolver.Should().Be(25m);
    }

    [Fact]
    public async Task Cancelar_desconta_o_que_ja_foi_devolvido()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        f.AdicionarPagamento("pix-direto", 25m);
        f.PedidoRepo.ListarEstornosManuaisAsync(f.EmpresaId, f.Pedido.Id, Arg.Any<CancellationToken>())
            .Returns([new PedidoEstornoManual { Id = Guid.NewGuid(), EmpresaId = f.EmpresaId, PedidoId = f.Pedido.Id,
                PagamentoId = f.Pedido.Pagamentos.Single().Id, Valor = 10m, Metodo = "pix", Motivo = "parcial" }]);

        await Cancelar(f).ExecuteAsync(new CancelarPedidoCommand(f.EmpresaId, f.Pedido.Id, NivelSolicitante: NivelAcesso.Gerente));

        f.Eventos.Should().ContainSingle(e => e.Tipo == CancelarPedidoUseCase.EventoDevolucaoAFazer)
            .Which.Detalhes.Should().Contain(15m.ToString("C", Cultura.PtBr));
        (await Consultar(f).ExecuteAsync(f.EmpresaId, f.Pedido.Id)).ADevolver.Should().Be(15m);
    }

    [Fact]
    public async Task Cancelar_sem_recebido_nao_registra_devolucao()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        f.PedidoRepo.ListarEstornosManuaisAsync(f.EmpresaId, f.Pedido.Id, Arg.Any<CancellationToken>()).Returns([]);

        await Cancelar(f).ExecuteAsync(new CancelarPedidoCommand(f.EmpresaId, f.Pedido.Id));

        f.Eventos.Should().NotContain(e => e.Tipo == CancelarPedidoUseCase.EventoDevolucaoAFazer);
        (await Consultar(f).ExecuteAsync(f.EmpresaId, f.Pedido.Id)).ADevolver.Should().Be(0m);
    }

    [Fact]
    public async Task Pedido_ativo_com_recebido_nao_tem_devolucao_a_fazer()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        f.AdicionarPagamento("pix-direto", 25m);
        f.PedidoRepo.ListarEstornosManuaisAsync(f.EmpresaId, f.Pedido.Id, Arg.Any<CancellationToken>()).Returns([]);

        (await Consultar(f).ExecuteAsync(f.EmpresaId, f.Pedido.Id)).ADevolver.Should().Be(0m);
    }

    private static SaldoFinanceiroPedido Saldos(CobrancaPedidoFixture f) =>
        new(f.PedidoRepo, Substitute.For<IEstornoOnlineRepository>(), f.Creditos);

    private static ConsultarEstornosManuaisUseCase Consultar(CobrancaPedidoFixture f) => new(f.PedidoRepo, f.CobrancaRepo, Saldos(f));

    private static CancelarPedidoUseCase Cancelar(CobrancaPedidoFixture f)
    {
        f.Uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<PedidoResult?>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<CancellationToken, Task<PedidoResult?>>>()(ci.Arg<CancellationToken>()));
        var estoque = new PedidoEstoqueIntegrationService(Substitute.For<IItemEstoqueRepository>(),
            Substitute.For<IMovimentacaoEstoqueRepository>(), f.Publicador, Options.Create(new PedidoEstoqueOptions()),
            NullLogger<PedidoEstoqueIntegrationService>.Instance);
        return new CancelarPedidoUseCase(f.PedidoRepo, estoque,
            new EfeitosCancelamentoPedido(Substitute.For<IContaReceberRepository>(), f.VagaRepo, NullLogger<EfeitosCancelamentoPedido>.Instance),
            f.Uow, NullLogger<CancelarPedidoUseCase>.Instance, f.CobrancaRepo, f.Publicador, f.OperacaoEventos, Saldos(f));
    }
}
