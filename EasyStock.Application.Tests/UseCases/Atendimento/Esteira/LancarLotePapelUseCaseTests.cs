using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services;
using EasyStock.Application.UseCases.AtualizarStatusPedido;
using EasyStock.Application.UseCases.Atendimento.Esteira;
using EasyStock.Application.UseCases.Financeiro.ContasReceber;
using EasyStock.Application.UseCases.Financeiro.Integracao;
using EasyStock.Application.UseCases.Operacao.Kds;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Esteira;

public class LancarLotePapelUseCaseTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IPedidoRepository _pedidoRepo = Substitute.For<IPedidoRepository>();
    private readonly IPublicadorEventoIntegracao _publicador = Substitute.For<IPublicadorEventoIntegracao>();
    private readonly List<PedidoEvento> _eventos = [];

    private LancarLotePapelUseCase Build()
    {
        var uow = Substitute.For<IUnitOfWork>();
        var configRepo = Substitute.For<IConfiguracaoLojaRepository>();
        var crRepo = Substitute.For<IContaReceberRepository>();
        var catRepo = Substitute.For<ICategoriaFinanceiraRepository>();
        var criarCr = new CriarContaReceberUseCase(crRepo, catRepo, Substitute.For<ICentroCustoRepository>(), uow,
            NullLogger<CriarContaReceberUseCase>.Instance);
        var gerarCr = new GerarContaReceberDePedidoUseCase(crRepo, catRepo, configRepo, criarCr,
            NullLogger<GerarContaReceberDePedidoUseCase>.Instance);
        var integ = new PedidoEstoqueIntegrationService(Substitute.For<IItemEstoqueRepository>(),
            Substitute.For<IMovimentacaoEstoqueRepository>(),
            Substitute.For<IPublicadorEventoIntegracao>(),
            Options.Create(new PedidoEstoqueOptions { PermiteEstoqueNegativo = true }),
            NullLogger<PedidoEstoqueIntegrationService>.Instance);
        uow.ExecuteInTransactionSemRetryAsync(Arg.Any<Func<CancellationToken, Task<EasyStock.Application.UseCases.Pedidos.PedidoResult?>>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<CancellationToken, Task<EasyStock.Application.UseCases.Pedidos.PedidoResult?>>>()(ci.Arg<CancellationToken>()));
        var atualizar = new AtualizarStatusPedidoUseCase(_pedidoRepo, integ, configRepo, gerarCr, _publicador,
            Substitute.For<IOperacaoEventPublisher>(), uow, NullLogger<AtualizarStatusPedidoUseCase>.Instance,
            new EasyStock.Application.Services.Pedidos.CalculadoraInicioPrevistoPedido(Substitute.For<EasyStock.Application.Ports.Output.Persistence.IPrazoPreparoPedidoQueries>()),
            new EasyStock.Application.UseCases.CancelarPedido.CancelarPedidoUseCase(_pedidoRepo, integ, new EasyStock.Application.Services.Pedidos.EfeitosCancelamentoPedido(crRepo, Substitute.For<EasyStock.Application.Ports.Output.Persistence.Storefront.IVagaOcupadaRepository>(), NullLogger<EasyStock.Application.Services.Pedidos.EfeitosCancelamentoPedido>.Instance), uow, Microsoft.Extensions.Logging.Abstractions.NullLogger<EasyStock.Application.UseCases.CancelarPedido.CancelarPedidoUseCase>.Instance, Substitute.For<EasyStock.Application.Ports.Output.Persistence.Pagamentos.ICobrancaPedidoRepository>(), _publicador, Substitute.For<EasyStock.Application.Ports.Output.Atendimento.IOperacaoEventPublisher>()));
        var lote = new AtualizarStatusPedidosEmLoteUseCase(atualizar, uow,
            NullLogger<AtualizarStatusPedidosEmLoteUseCase>.Instance);
        _pedidoRepo.AddEventoAsync(Arg.Do<PedidoEvento>(_eventos.Add));
        return new LancarLotePapelUseCase(lote);
    }

    private Pedido NovoPedido(string status)
    {
        var p = Pedido.Criar(_empresaId, null, null, "web");
        p.Status = status;
        _pedidoRepo.GetByIdWithDetailsAsync(_empresaId, p.Id).Returns(p);
        return p;
    }

    [Fact]
    public async Task AplicaEmOrdemEReportaInvalido()
    {
        var uc = Build();
        var a = NovoPedido("aguardando");
        var b = NovoPedido("aguardando");
        var c = NovoPedido("entregue");
        var t0 = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

        // Fora de ordem de propósito: o lote ordena por OcorreuEm.
        var linhas = new List<LinhaLotePapel>
        {
            new(a.Id, "pronto", t0.AddMinutes(20)),
            new(b.Id, "preparando", t0.AddMinutes(5)),
            new(a.Id, "preparando", t0),
            new(c.Id, "preparando", t0.AddMinutes(1)), // inválido: entregue não volta
            new(b.Id, "pronto", t0.AddMinutes(30)),
        };

        var r = await uc.ExecuteAsync(new LancarLotePapelCommand(_empresaId, linhas));

        r.Aplicados.Should().Be(4);
        r.Rejeitados.Should().Be(1);
        var rejeitada = r.Linhas.Single(l => !l.Sucesso);
        rejeitada.PedidoId.Should().Be(c.Id);
        rejeitada.Linha.Should().Be(4);
        rejeitada.Motivo.Should().NotBeNullOrWhiteSpace();
        a.Status.Should().Be("pronto");
        b.Status.Should().Be("pronto");
        c.Status.Should().Be("entregue");
        _eventos.Select(e => e.OcorridoEm).Should().BeEquivalentTo(
            new[] { t0, t0.AddMinutes(5), t0.AddMinutes(20), t0.AddMinutes(30) },
            o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task SemAvisoRetroativo()
    {
        var uc = Build();
        var a = NovoPedido("aguardando");
        var t0 = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

        await uc.ExecuteAsync(new LancarLotePapelCommand(_empresaId,
            [new(a.Id, "preparando", t0), new(a.Id, "pronto", t0.AddMinutes(10))]));

        // Toda transição sai marcada como lote de papel: quem avisa o cliente (S13) ignora essa origem.
        _eventos.Should().OnlyContain(e => e.Origem == LancarLotePapelUseCase.OrigemLotePapel);
        await _publicador.DidNotReceive().PublicarAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(),
            Arg.Is<PedidoMudouStatusEvent>(e => e.Origem != LancarLotePapelUseCase.OrigemLotePapel),
            Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }
}
