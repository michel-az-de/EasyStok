using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ClienteDaConversa;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Atendimento.ClienteDaConversa;

public sealed class VinculoClienteConversaUseCaseTests
{
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly ICurrentUserAccessor _usuario = Substitute.For<ICurrentUserAccessor>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IVinculoClienteConversaRepository _vinculos = Substitute.For<IVinculoClienteConversaRepository>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly Conversa _conversa;
    private readonly List<AlteracaoVinculoConversa> _historico = [];
    private VinculoClienteConversaUseCase Sut => new(_conversas, _clientes, _vinculos, _uow, _usuario, TimeProvider.System);

    public VinculoClienteConversaUseCaseTests()
    {
        _usuario.EmpresaId.Returns(_empresa);
        _usuario.UsuarioId.Returns(Guid.NewGuid());
        _usuario.Nivel.Returns(NivelAcesso.Admin);
        _usuario.IsAuthenticated.Returns(true);
        _usuario.TemPermissao(Arg.Any<Permissao>()).Returns(true);
        _conversa = Conversa.Abrir(_empresa, "5511999990001", DateTime.UtcNow, clienteId: Guid.NewGuid());
        _conversas.ObterPorIdAsync(_empresa, _conversa.Id, Arg.Any<CancellationToken>()).Returns(_conversa);
        _conversas.ObterPorIdComLockAsync(_empresa, _conversa.Id, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _uow.EmTransacao.Should().BeTrue();
            return _conversa;
        });
        _clientes.GetByIdAsync(_empresa, Arg.Any<Guid>()).Returns(c =>
            new Cliente { Id = c.ArgAt<Guid>(1), EmpresaId = _empresa, Nome = "Maria", Ativo = true });
        _vinculos.ListarPedidosAsync(_empresa, _conversa.Id, Arg.Any<CancellationToken>()).Returns([]);
        _vinculos.ListarHistoricoAsync(_empresa, _conversa.Id, Arg.Any<CancellationToken>()).Returns(_historico);
        _vinculos.ObterTentativaAsync(_empresa, _conversa.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(c => _historico.SingleOrDefault(h => h.RequestId == c.ArgAt<Guid>(2)));
        _vinculos.When(x => x.AddAsync(Arg.Any<AlteracaoVinculoConversa>(), Arg.Any<CancellationToken>()))
            .Do(c => _historico.Add(c.Arg<AlteracaoVinculoConversa>()));
    }

    private CorrigirVinculoClienteCommand Retirar() => new(_empresa, _conversa.Id, Guid.NewGuid(),
        _conversa.VinculoRevisao, _conversa.ClienteId, "retirar", null, "Telefone compartilhado");

    [Fact]
    public async Task RetiraRestauraEAuditaUmaVezSemModificarCadastro()
    {
        var command = Retirar();
        await Sut.ExecuteAsync(command);
        await Sut.ExecuteAsync(command);
        _uow.CommitCount.Should().Be(1);
        _historico.Should().ContainSingle().Which.UsuarioId.Should().Be(_usuario.UsuarioId);
        _conversa.ClienteId.Should().BeNull();
        await _vinculos.Received(1).ExpirarLinksAsync(_empresa, _conversa.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _vinculos.Received(1).EncerrarSessoesChatAsync(_empresa, _conversa.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await Sut.ExecuteAsync(new(_empresa, _conversa.Id, Guid.NewGuid(), 1, null, "restaurar", null, "Pessoa conferida"));
        _conversa.ClienteId.Should().Be(command.ClienteId);
        await _clientes.DidNotReceive().UpdateAsync(Arg.Any<Cliente>());
    }

    [Fact]
    public async Task TrocaRequestIdNaoReaproveitaTentativaComOutroMotivo()
    {
        var command = Retirar();
        await Sut.ExecuteAsync(command);
        await Sut.Invoking(s => s.ExecuteAsync(command with { Motivo = "Outro motivo" }))
            .Should().ThrowAsync<VinculoClienteConflitoException>();
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task RecusaTenantDivergenteEAtendenteSemPoderDeCorrecao()
    {
        await Sut.Invoking(s => s.ExecuteAsync(Retirar() with { EmpresaId = Guid.NewGuid() }))
            .Should().ThrowAsync<UseCaseValidationException>();
        _usuario.Nivel.Returns(NivelAcesso.Operador);
        await Sut.Invoking(s => s.ExecuteAsync(Retirar())).Should().ThrowAsync<UnauthorizedAccessException>();
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task PedidoCanceladoNoHistoricoBloqueiaSemEfeitos()
    {
        _vinculos.ListarPedidosAsync(_empresa, _conversa.Id, Arg.Any<CancellationToken>())
            .Returns([new PedidoDoVinculo(Guid.NewGuid(), "cancelado", 15)]);
        var estado = await Sut.ObterAsync(_empresa, _conversa.Id);
        estado.PodeCorrigir.Should().BeFalse();
        estado.Pedidos.Should().ContainSingle();
        await Sut.Invoking(s => s.ExecuteAsync(Retirar())).Should().ThrowAsync<VinculoClienteConflitoException>();
        await _vinculos.DidNotReceive().ExpirarLinksAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EscolhaExplicitaNaoMudaHashDoTelefone()
    {
        await Sut.ExecuteAsync(Retirar());
        var outraPessoa = Guid.NewGuid();
        await Sut.ExecuteAsync(new(_empresa, _conversa.Id, Guid.NewGuid(), 1, null, "vincular", outraPessoa, "Pessoa conferida"));
        _conversa.ClienteId.Should().Be(outraPessoa);
        await _clientes.DidNotReceive().UpdateAsync(Arg.Any<Cliente>());
    }

    [Fact]
    public async Task RevisaoAntigaRecusaAntesDeInvalidarLinks()
    {
        var command = Retirar() with { VinculoRevisao = 7 };
        await Sut.Invoking(s => s.ExecuteAsync(command)).Should().ThrowAsync<VinculoClienteConflitoException>();
        _historico.Should().BeEmpty();
        _conversa.ClienteId.Should().Be(command.ClienteId);
    }
}
