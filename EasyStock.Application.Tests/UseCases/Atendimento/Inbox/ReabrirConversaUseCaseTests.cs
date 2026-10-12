using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Inbox;

public class ReabrirConversaUseCaseTests
{
    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _usuario = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly FakeUnitOfWork _uow = new();
    private ReabrirConversaUseCase UseCase => new(_conversas, _clientes, _uow, TimeProvider.System);

    private Conversa Encerrada(CanalConversa canal = CanalConversa.WhatsApp)
    {
        var conversa = Conversa.Abrir(_empresa, "5511999990001", DateTime.UtcNow.AddDays(-2), canal: canal);
        conversa.RegistrarEntrada(DateTime.UtcNow.AddDays(-2));
        conversa.Encerrar(DateTime.UtcNow.AddDays(-1));
        _conversas.ObterPorIdAsync(_empresa, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        _conversas.ObterPorIdComLockAsync(_empresa, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        return conversa;
    }

    private AcaoConversaCommand Acao(Conversa conversa) => new(_empresa, _usuario, conversa.Id, "  Atendente  ");

    [Fact]
    public async Task ReabreComNotaInternaAutoriaECarimboAnterior_UmaVez()
    {
        var conversa = Encerrada();
        var encerradaEm = conversa.EncerradaEm;

        var result = await UseCase.ExecuteAsync(Acao(conversa));
        await UseCase.ExecuteAsync(Acao(conversa));

        result.Situacao.Should().Be(SituacaoConversa.Assumida);
        result.AssumidaPorUsuarioId.Should().BeNull();
        conversa.DentroDaJanela(DateTime.UtcNow).Should().BeFalse();
        _uow.CommitCount.Should().Be(1);
        await _conversas.Received(1).AddMensagemAsync(Arg.Is<Mensagem>(m => m.EmpresaId == _empresa
            && m.ConversaId == conversa.Id && m.Autor == AutorMensagem.Sistema && m.ExternoId == null
            && m.Status == StatusMensagem.Pendente && m.EnviadaPorUsuarioId == _usuario
            && m.Texto!.Contains("Atendente") && m.Texto.Contains($"{encerradaEm:dd/MM/yyyy HH:mm:ss} UTC")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OutraConversaAberta_RecusaSemAlteracao()
    {
        var conversa = Encerrada();
        _conversas.ObterAbertaPorContatoAsync(_empresa, conversa.Canal, conversa.ContatoIdExterno, Arg.Any<CancellationToken>())
            .Returns(Conversa.Abrir(_empresa, conversa.ContatoIdExterno, DateTime.UtcNow));

        var act = () => UseCase.ExecuteAsync(Acao(conversa));
        await act.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage(ReabrirConversaUseCase.ConflitoContato);
        conversa.EstaAberta.Should().BeFalse();
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task ClienteBloqueado_RecusaSemAlteracao()
    {
        var conversa = Encerrada();
        var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = _empresa, Nome = "Cliente" };
        cliente.Bloquear("motivo", DateTime.UtcNow);
        conversa.VincularCliente(cliente.Id);
        _clientes.GetByIdAsync(_empresa, cliente.Id).Returns(cliente);

        var act = () => UseCase.ExecuteAsync(Acao(conversa));
        await act.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("Cliente bloqueado*");
        conversa.EstaAberta.Should().BeFalse();
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task ChatSite_RecusaOutraSessaoAbertaDoMesmoCliente()
    {
        var conversa = Encerrada(CanalConversa.ChatSite);
        var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = _empresa, Nome = "Cliente" };
        conversa.VincularCliente(cliente.Id);
        _clientes.GetByIdAsync(_empresa, cliente.Id).Returns(cliente);
        _conversas.ObterAbertaPorClienteNoCanalAsync(_empresa, cliente.Id, conversa.Canal, Arg.Any<CancellationToken>())
            .Returns(Conversa.Abrir(_empresa, "outra-sessao", DateTime.UtcNow, clienteId: cliente.Id, canal: conversa.Canal));

        var act = () => UseCase.ExecuteAsync(Acao(conversa));
        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
        conversa.EstaAberta.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefesaExplicita_RecusaOutraEmpresaMesmoSeRepositorioDevolver(bool naLeituraComLock)
    {
        var conversa = Encerrada();
        var alheia = Conversa.Abrir(Guid.NewGuid(), "5511888881111", DateTime.UtcNow);
        if (naLeituraComLock)
            _conversas.ObterPorIdComLockAsync(_empresa, conversa.Id, Arg.Any<CancellationToken>()).Returns(alheia);
        else
            _conversas.ObterPorIdAsync(_empresa, conversa.Id, Arg.Any<CancellationToken>()).Returns(alheia);

        var act = () => UseCase.ExecuteAsync(Acao(conversa));
        await act.Should().ThrowAsync<ConversaNaoEncontradaException>();
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task SemIdentidade_RecusaAntesDoRepositorio()
    {
        var act = () => UseCase.ExecuteAsync(new AcaoConversaCommand(Guid.Empty, _usuario, Guid.NewGuid()));
        await act.Should().ThrowAsync<UseCaseValidationException>();
        _conversas.ReceivedCalls().Should().BeEmpty();
    }
}
