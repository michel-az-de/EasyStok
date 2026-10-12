using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.ClienteCrm;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.ClienteCrm;

/// <summary>S24: tags, notas internas, bloqueio e preferências do cliente pelo console.</summary>
public class ClienteCrmUseCasesTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 30, 15, 0, 0, TimeSpan.Zero);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IClienteCrmRepository _crm = Substitute.For<IClienteCrmRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _relogio = new(Agora);
    private readonly Cliente _cliente;

    public ClienteCrmUseCasesTests()
    {
        _cliente = Cliente.Criar(_empresaId, "Maria");
        _clientes.GetByIdAsync(_empresaId, _cliente.Id).Returns(_cliente);
        _crm.ObterComTagsAsync(_empresaId, _cliente.Id, Arg.Any<CancellationToken>()).Returns(_cliente);
    }

    // ── Tags ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdicionarTagNormalizaGravaComOrigemDonaECommita()
    {
        var useCase = new AdicionarTagClienteUseCase(_crm, _uow, _relogio);

        var tag = await useCase.ExecuteAsync(new AdicionarTagClienteCommand(_empresaId, _cliente.Id, "Sem Glúten"));

        tag.Tag.Should().Be("sem_gluten");
        tag.Origem.Should().Be(OrigemClienteTag.Dona);
        tag.CriadoEm.Should().Be(Agora.UtcDateTime);
        _cliente.Tags.Should().ContainSingle();
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task TagDuplicadaLancaConflitoSemSegundaLinha()
    {
        _cliente.AdicionarTag("vegano", OrigemClienteTag.Agente, Agora.UtcDateTime);
        var useCase = new AdicionarTagClienteUseCase(_crm, _uow, _relogio);

        var act = () => useCase.ExecuteAsync(new AdicionarTagClienteCommand(_empresaId, _cliente.Id, "VEGANO"));

        await act.Should().ThrowAsync<ClienteTagDuplicadaException>();
        _cliente.Tags.Should().ContainSingle();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task TagDeClienteInexistenteLancaNaoEncontrado()
    {
        var useCase = new AdicionarTagClienteUseCase(_crm, _uow, _relogio);
        var act = () => useCase.ExecuteAsync(new AdicionarTagClienteCommand(_empresaId, Guid.NewGuid(), "vegano"));
        await act.Should().ThrowAsync<ClienteCrmNaoEncontradoException>();
    }

    [Fact]
    public async Task ListarTagsDevolveTagsESugeridas()
    {
        _cliente.AdicionarTag("risco", OrigemClienteTag.Dona, Agora.UtcDateTime);
        var useCase = new ListarTagsClienteUseCase(_crm);

        var resultado = await useCase.ExecuteAsync(_empresaId, _cliente.Id);

        resultado.Tags.Should().ContainSingle().Which.Tag.Should().Be("risco");
        resultado.Sugeridas.Should().Contain("sem_gluten");
    }

    [Fact]
    public async Task RemoverTagCommitaQuandoExistia()
    {
        _cliente.AdicionarTag("risco", OrigemClienteTag.Dona, Agora.UtcDateTime);
        var useCase = new RemoverTagClienteUseCase(_crm, _uow);

        (await useCase.ExecuteAsync(new RemoverTagClienteCommand(_empresaId, _cliente.Id, "Risco"))).Should().BeTrue();
        (await useCase.ExecuteAsync(new RemoverTagClienteCommand(_empresaId, _cliente.Id, "risco"))).Should().BeFalse();

        _cliente.Tags.Should().BeEmpty();
        await _uow.Received(1).CommitAsync();
    }

    // ── Notas ───────────────────────────────────────────────────────────

    [Fact]
    public async Task NotaComPedidoDoClienteEhGravada()
    {
        var pedidoId = Guid.NewGuid();
        _crm.PedidoEhDoClienteAsync(_empresaId, pedidoId, _cliente.Id, Arg.Any<CancellationToken>()).Returns(true);
        var useCase = new AdicionarNotaClienteUseCase(_clientes, _crm, _uow, _relogio);

        var nota = await useCase.ExecuteAsync(
            new AdicionarNotaClienteCommand(_empresaId, _cliente.Id, "chegou amassado", "Baba", pedidoId));

        nota.Texto.Should().Be("chegou amassado");
        nota.PedidoId.Should().Be(pedidoId);
        nota.CriadoEm.Should().Be(Agora.UtcDateTime);
        await _crm.Received(1).AdicionarNotaAsync(
            Arg.Is<ClienteNota>(n => n.ClienteId == _cliente.Id && n.EmpresaId == _empresaId && n.Autor == "Baba"),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task NotaComPedidoDeOutroClienteEhRecusada()
    {
        var pedidoDeOutro = Guid.NewGuid();
        _crm.PedidoEhDoClienteAsync(_empresaId, pedidoDeOutro, _cliente.Id, Arg.Any<CancellationToken>()).Returns(false);
        var useCase = new AdicionarNotaClienteUseCase(_clientes, _crm, _uow, _relogio);

        var act = () => useCase.ExecuteAsync(
            new AdicionarNotaClienteCommand(_empresaId, _cliente.Id, "texto", "Baba", pedidoDeOutro));

        await act.Should().ThrowAsync<NotaPedidoDeOutroClienteException>();
        await _crm.DidNotReceiveWithAnyArgs().AdicionarNotaAsync(default!, default);
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task ListarNotasDeClienteInexistenteLancaNaoEncontrado()
    {
        var useCase = new ListarNotasClienteUseCase(_clientes, _crm);
        var act = () => useCase.ExecuteAsync(_empresaId, Guid.NewGuid());
        await act.Should().ThrowAsync<ClienteCrmNaoEncontradoException>();
    }

    // ── Bloqueio e preferências ─────────────────────────────────────────

    [Fact]
    public async Task BloquearEDesbloquear()
    {
        var useCase = new DefinirBloqueioClienteUseCase(_clientes, _uow, _relogio);

        var bloqueio = await useCase.BloquearAsync(_empresaId, _cliente.Id, "golpe");
        bloqueio.Bloqueado.Should().BeTrue();
        bloqueio.MotivoBloqueio.Should().Be("golpe");
        bloqueio.BloqueadoEm.Should().Be(Agora.UtcDateTime);

        var desbloqueio = await useCase.DesbloquearAsync(_empresaId, _cliente.Id);
        desbloqueio.Bloqueado.Should().BeFalse();
        _cliente.Bloqueado.Should().BeFalse();
        await _uow.Received(2).CommitAsync();
    }

    [Fact]
    public async Task PreferenciasAlteramSoOQueVeio()
    {
        var useCase = new DefinirPreferenciasClienteUseCase(_clientes, _uow, _relogio);

        var resultado = await useCase.ExecuteAsync(
            new DefinirPreferenciasClienteCommand(_empresaId, _cliente.Id, AvisosStatusAtivos: false, ConsentiuMarketing: null));

        resultado.AvisosStatusAtivos.Should().BeFalse();
        resultado.ConsentiuMarketing.Should().BeFalse();
        _cliente.ConsentimentoEm.Should().BeNull("consentimento não veio no pedido");

        resultado = await useCase.ExecuteAsync(
            new DefinirPreferenciasClienteCommand(_empresaId, _cliente.Id, AvisosStatusAtivos: null, ConsentiuMarketing: true));

        resultado.AvisosStatusAtivos.Should().BeFalse();
        resultado.ConsentiuMarketing.Should().BeTrue();
        resultado.ConsentimentoEm.Should().Be(Agora.UtcDateTime);
    }
}
