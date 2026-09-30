using System.Text.Json;
using EasyStock.Application.Events.Campanhas;
using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Application.UseCases.Campanhas.Interesse;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Integration;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Campanhas;

/// <summary>
/// Pendências da S31 (#1228): a dona fecha o interesse pelo console, o console lista os interesses do
/// cliente e o pagamento confirmado do pedido fecha os interesses abertos do cliente nos itens comprados.
/// </summary>
public class InteresseAtendidoUseCasesTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 18, 0, 0, DateTimeKind.Utc);
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();

    private readonly IInteresseItemRepository _interesses = Substitute.For<IInteresseItemRepository>();
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(Agora));

    private MarcarInteresseAtendidoUseCase Marcar() => new(_interesses, _uow, _relogio);
    private FecharInteressesDoPedidoUseCase Fechar() => new(_pedidos, _interesses, _uow, _relogio);

    private static InteresseItem Interesse(Guid? clienteId = null, Guid? itemId = null) =>
        InteresseItem.Registrar(EmpresaId, clienteId ?? ClienteId, itemId ?? Guid.NewGuid(), null, OrigemInteresse.Agente, Agora.AddDays(-2));

    // ── marcar atendido (dona) ─────────────────────────────────────

    [Fact]
    public async Task MarcarAtendido_FechaEGrava()
    {
        var interesse = Interesse();
        _interesses.GetByIdAsync(EmpresaId, interesse.Id, Arg.Any<CancellationToken>()).Returns(interesse);

        var result = await Marcar().ExecuteAsync(EmpresaId, ClienteId, interesse.Id);

        interesse.AtendidoEm.Should().Be(Agora);
        result.AtendidoEm.Should().Be(Agora);
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task MarcarAtendido_DeOutraEmpresa_NaoEncontrado()
    {
        var id = Guid.NewGuid();
        _interesses.GetByIdAsync(EmpresaId, id, Arg.Any<CancellationToken>()).Returns((InteresseItem?)null);

        var act = () => Marcar().ExecuteAsync(EmpresaId, ClienteId, id);

        await act.Should().ThrowAsync<InteresseNaoEncontradoException>();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task MarcarAtendido_DeOutroCliente_NaoEncontrado()
    {
        var deOutro = Interesse(clienteId: Guid.NewGuid());
        _interesses.GetByIdAsync(EmpresaId, deOutro.Id, Arg.Any<CancellationToken>()).Returns(deOutro);

        var act = () => Marcar().ExecuteAsync(EmpresaId, ClienteId, deOutro.Id);

        await act.Should().ThrowAsync<InteresseNaoEncontradoException>();
        deOutro.AtendidoEm.Should().BeNull();
    }

    [Fact]
    public async Task MarcarAtendido_Repetido_MantemOPrimeiroInstante()
    {
        var interesse = Interesse();
        var primeiro = Agora.AddHours(-3);
        interesse.MarcarAtendido(primeiro);
        _interesses.GetByIdAsync(EmpresaId, interesse.Id, Arg.Any<CancellationToken>()).Returns(interesse);

        var result = await Marcar().ExecuteAsync(EmpresaId, ClienteId, interesse.Id);

        result.AtendidoEm.Should().Be(primeiro);
    }

    // ── listagem do cliente ────────────────────────────────────────

    [Fact]
    public async Task Listar_MapeiaOsInteressesDoCliente()
    {
        var aberto = Interesse();
        var atendido = Interesse();
        atendido.MarcarAtendido(Agora);
        _interesses.ListarDoClienteAsync(EmpresaId, ClienteId, Arg.Any<CancellationToken>()).Returns([aberto, atendido]);

        var lista = await new ListarInteressesDoClienteUseCase(_interesses).ExecuteAsync(EmpresaId, ClienteId);

        lista.Select(i => i.Id).Should().Equal(aberto.Id, atendido.Id);
        lista[1].AtendidoEm.Should().Be(Agora);
    }

    // ── fechamento ao pagar ────────────────────────────────────────

    private Pedido PedidoPago(Guid? clienteId, params Guid?[] itens)
    {
        var pedido = new Pedido { Id = Guid.NewGuid(), EmpresaId = EmpresaId, ClienteId = clienteId };
        foreach (var item in itens)
            pedido.Itens.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "x", CardapioItemId = item });
        _pedidos.GetByIdWithDetailsAsync(EmpresaId, pedido.Id).Returns(pedido);
        return pedido;
    }

    [Fact]
    public async Task PedidoPago_FechaInteressesAbertosDoClienteNosItensComprados()
    {
        var bolo = Guid.NewGuid();
        var torta = Guid.NewGuid();
        var pedido = PedidoPago(ClienteId, bolo, torta, null);
        var interesse = Interesse(itemId: bolo);
        _interesses.ListarAbertosDoClienteNosItensAsync(EmpresaId, ClienteId,
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(bolo) && ids.Contains(torta)),
                Arg.Any<CancellationToken>())
            .Returns([interesse]);

        var fechados = await Fechar().ExecuteAsync(EmpresaId, pedido.Id);

        fechados.Should().Be(1);
        interesse.AtendidoEm.Should().Be(Agora);
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task PedidoPago_SemCliente_NaoConsultaInteresses()
    {
        var pedido = PedidoPago(null, Guid.NewGuid());

        var fechados = await Fechar().ExecuteAsync(EmpresaId, pedido.Id);

        fechados.Should().Be(0);
        await _interesses.DidNotReceiveWithAnyArgs().ListarAbertosDoClienteNosItensAsync(default, default, default!, default);
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task PedidoPago_SemItemDoCardapio_NaoConsultaInteresses()
    {
        var pedido = PedidoPago(ClienteId, null, null);

        (await Fechar().ExecuteAsync(EmpresaId, pedido.Id)).Should().Be(0);

        await _interesses.DidNotReceiveWithAnyArgs().ListarAbertosDoClienteNosItensAsync(default, default, default!, default);
    }

    [Fact]
    public async Task PedidoPago_SemInteresseAberto_NaoGrava()
    {
        var pedido = PedidoPago(ClienteId, Guid.NewGuid());
        _interesses.ListarAbertosDoClienteNosItensAsync(EmpresaId, ClienteId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        (await Fechar().ExecuteAsync(EmpresaId, pedido.Id)).Should().Be(0);

        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task PedidoPago_PedidoInexistente_NaoFazNada()
    {
        (await Fechar().ExecuteAsync(EmpresaId, Guid.NewGuid())).Should().Be(0);

        await _uow.DidNotReceive().CommitAsync();
    }

    // ── handler do outbox (pedido.pago) ────────────────────────────

    [Fact]
    public async Task Handler_PedidoPago_FixaOTenantEFecha()
    {
        var bolo = Guid.NewGuid();
        var pedido = PedidoPago(ClienteId, bolo);
        var interesse = Interesse(itemId: bolo);
        _interesses.ListarAbertosDoClienteNosItensAsync(EmpresaId, ClienteId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([interesse]);
        var tenant = Substitute.For<ITenantContextAccessor>();
        var handler = new FecharInteressesPedidoPagoHandler(tenant, Fechar());
        var payload = new PedidoPagoEvent(pedido.Id, EmpresaId, null, ClienteId, null, Guid.NewGuid(), "mercadopago",
            "pay-1", "pix", 50m, "aguardando", Agora);
        var evento = OutboxEventoIntegracao.Criar(EmpresaId, PedidoPagoEvent.TipoEvento, "pedido", pedido.Id,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        handler.TipoEvento.Should().Be(PedidoPagoEvent.TipoEvento);
        await handler.HandleAsync(evento, CancellationToken.None);

        tenant.Received(1).SetCurrentTenant(EmpresaId);
        interesse.AtendidoEm.Should().Be(Agora);
    }
}
