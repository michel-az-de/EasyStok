using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Inbox;

/// <summary>
/// #2004 atendimento-09: transferir lia a conversa sem trava e gravava <c>Assumida</c> por cima de uma conversa que a
/// dona encerrou em outra aba no mesmo instante (o defeito que o #1792 corrigiu em assumir).
/// </summary>
public class TransferirConversaUseCaseTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IAtendenteRepository _atendentes = Substitute.For<IAtendenteRepository>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly Guid _destino = Guid.NewGuid();

    public TransferirConversaUseCaseTests()
    {
        _atendentes.ObterUsuarioAtivoAsync(_empresaId, _destino, Arg.Any<CancellationToken>())
            .Returns(new UsuarioDaEmpresa(_destino, "Bia", "bia@x.com", [new PerfilNaEmpresa(NivelAcesso.Operador, [])]));
    }

    private TransferirConversaUseCase UseCase() => new(_conversas, _atendentes, _uow);

    private TransferirConversaCommand Comando(Guid conversaId) =>
        new(_empresaId, Guid.NewGuid(), conversaId, _destino, "Thati");

    [Fact]
    public async Task TransferirReleSobTravaEmTransacao_ConversaEncerradaNoMeioNaoReabre()
    {
        var aberta = Conversa.Abrir(_empresaId, "5511988887777", DateTime.UtcNow.AddMinutes(-5));
        // O que está gravado agora: a outra aba já encerrou e confirmou. A leitura sem trava ainda vê a conversa aberta.
        var encerrada = Conversa.Abrir(_empresaId, "5511988887777", DateTime.UtcNow.AddMinutes(-5));
        typeof(Conversa).GetProperty(nameof(Conversa.Id))!.SetValue(encerrada, aberta.Id);
        encerrada.Encerrar(DateTime.UtcNow);
        _conversas.ObterPorIdAsync(_empresaId, aberta.Id, Arg.Any<CancellationToken>()).Returns(aberta);
        var dentro = false;
        _conversas.ObterPorIdComLockAsync(_empresaId, aberta.Id, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            dentro = _uow.EmTransacao;
            return encerrada;
        });

        var act = () => UseCase().ExecuteAsync(Comando(aberta.Id));

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
        dentro.Should().BeTrue("a leitura com trava só vale dentro da transação");
        encerrada.Situacao.Should().Be(SituacaoConversa.Encerrada);
        encerrada.AssumidaPorUsuarioId.Should().BeNull();
        await _conversas.DidNotReceiveWithAnyArgs().AddMensagemAsync(default!, default);
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task TransferirConversaAbertaTrocaOResponsavelEDeixaANota()
    {
        var conversa = Conversa.Abrir(_empresaId, "5511988887777", DateTime.UtcNow.AddMinutes(-5));
        _conversas.ObterPorIdComLockAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        Mensagem? nota = null;
        await _conversas.AddMensagemAsync(Arg.Do<Mensagem>(m => nota = m), Arg.Any<CancellationToken>());

        var r = await UseCase().ExecuteAsync(Comando(conversa.Id));

        r.AssumidaPorUsuarioId.Should().Be(_destino);
        conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        nota!.Texto.Should().Contain("Thati").And.Contain("Bia");
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task ConversaInexistenteNaoEncontrada()
    {
        var act = () => UseCase().ExecuteAsync(Comando(Guid.NewGuid()));

        await act.Should().ThrowAsync<ConversaNaoEncontradaException>();
    }
}
