using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Application.UseCases.Operacao.Impressao;
using EasyStock.Domain.Entities.Operacao;
using EasyStock.Domain.Enums.Operacao;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Operacao.Impressao;

/// <summary>S20 (#1156): polling, retorno, reimpressão e alerta da fila de impressão.</summary>
public class FilaImpressaoUseCasesTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

    private sealed class RelogioFixo(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero);
    }

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IImpressaoPendenteRepository _repo = Substitute.For<IImpressaoPendenteRepository>();
    private readonly IOperacaoEventPublisher _eventos = Substitute.For<IOperacaoEventPublisher>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();

    [Fact]
    public async Task AlertaPublicaImpressaoAtrasadaPorEmpresa()
    {
        var outra = Guid.NewGuid();
        var a = new ImpressaoAtrasada(Guid.NewGuid(), _empresaId, Guid.NewGuid(), Agora.AddMinutes(-4));
        var b = new ImpressaoAtrasada(Guid.NewGuid(), outra, Guid.NewGuid(), Agora.AddMinutes(-10));
        _repo.ListarAtrasadasAsync(Agora.AddHours(-12), Agora.AddMinutes(-3), AlertarImpressoesAtrasadasUseCase.MaximoPorRodada, Arg.Any<CancellationToken>())
            .Returns(new[] { b, a });

        var total = await new AlertarImpressoesAtrasadasUseCase(_repo, _eventos, new RelogioFixo(Agora)).ExecuteAsync();

        total.Should().Be(2);
        await _eventos.Received(1).PublicarAsync(EventosOperacao.ImpressaoAtrasada, _empresaId,
            Arg.Is<ImpressaoAtrasadaOperacao>(e => e.ImpressaoId == a.ImpressaoId && e.PedidoId == a.PedidoId), Arg.Any<CancellationToken>());
        await _eventos.Received(1).PublicarAsync(EventosOperacao.ImpressaoAtrasada, outra,
            Arg.Is<ImpressaoAtrasadaOperacao>(e => e.ImpressaoId == b.ImpressaoId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AlertaSemAtrasadaNaoPublica()
    {
        _repo.ListarAtrasadasAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ImpressaoAtrasada>());

        var total = await new AlertarImpressoesAtrasadasUseCase(_repo, _eventos, new RelogioFixo(Agora)).ExecuteAsync();

        total.Should().Be(0);
        _eventos.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ReimprimirEnfileiraNovoItemEPublicaDepoisDoCommit()
    {
        var pedido = Pedido.Criar(_empresaId, origem: "whatsapp");
        _pedidos.GetByIdAsync(_empresaId, pedido.Id).Returns(pedido);
        ImpressaoPendente? nova = null;
        var ordem = new List<string>();
        _repo.When(r => r.AddAsync(Arg.Any<ImpressaoPendente>(), Arg.Any<CancellationToken>()))
            .Do(ci => { nova = ci.Arg<ImpressaoPendente>(); ordem.Add("enfileira"); });
        _uow.When(u => u.CommitAsync()).Do(_ => ordem.Add("commit"));
        _eventos.When(e => e.PublicarAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<object>(), Arg.Any<CancellationToken>()))
            .Do(_ => ordem.Add("sse"));

        var r = await new ReimprimirCanhotoUseCase(_pedidos, _repo, _eventos, _uow, new RelogioFixo(Agora))
            .ExecuteAsync(new ReimprimirCanhotoInput(_empresaId, pedido.Id));

        r.Should().NotBeNull();
        nova!.PedidoId.Should().Be(pedido.Id);
        nova.Status.Should().Be(StatusImpressao.Pendente);
        r!.Id.Should().Be(nova.Id);
        ordem.Should().Equal("enfileira", "commit", "sse");
    }

    [Fact]
    public async Task ReimprimirPedidoDeOutraEmpresaDevolveNull()
    {
        var r = await new ReimprimirCanhotoUseCase(_pedidos, _repo, _eventos, _uow, new RelogioFixo(Agora))
            .ExecuteAsync(new ReimprimirCanhotoInput(_empresaId, Guid.NewGuid()));

        r.Should().BeNull();
        await _repo.DidNotReceive().AddAsync(Arg.Any<ImpressaoPendente>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FalhouGuardaErroEComita()
    {
        var impressao = ImpressaoPendente.CriarCanhoto(_empresaId, null, Guid.NewGuid(), Agora);
        _repo.GetByIdAsync(_empresaId, impressao.Id, Arg.Any<CancellationToken>()).Returns(impressao);

        var r = await new RegistrarRetornoImpressaoUseCase(_repo, _uow, new RelogioFixo(Agora), NullLogger<RegistrarRetornoImpressaoUseCase>.Instance)
            .ExecuteAsync(new RegistrarRetornoImpressaoInput(_empresaId, impressao.Id, Impressa: false, Erro: "sem papel"));

        r!.Status.Should().Be("falhou");
        r.Erro.Should().Be("sem papel");
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task PendentesLimitaEmCinquenta()
    {
        _repo.ListarPendentesAsync(_empresaId, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<ImpressaoPendente>());

        await new ListarImpressoesPendentesUseCase(_repo).ExecuteAsync(new ListarImpressoesPendentesInput(_empresaId, 500));

        await _repo.Received(1).ListarPendentesAsync(_empresaId, ListarImpressoesPendentesUseCase.LimiteMaximo, Arg.Any<CancellationToken>());
    }
}
