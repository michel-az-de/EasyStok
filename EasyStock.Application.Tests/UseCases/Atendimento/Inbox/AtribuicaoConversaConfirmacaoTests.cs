using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Inbox;

/// <summary>
/// ATD-1 (#2305): assumir, responder e transferir conversa de OUTRA atendente pedem confirmação explícita (409
/// CONVERSA_DE_OUTRA). Conversa livre ou própria segue sem perguntar.
/// </summary>
public class AtribuicaoConversaConfirmacaoTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _minha = Guid.NewGuid();
    private readonly Guid _outra = Guid.NewGuid();
    private readonly Guid _destino = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IAtendenteRepository _atendentes = Substitute.For<IAtendenteRepository>();
    private readonly FakeUnitOfWork _uow = new();

    public AtribuicaoConversaConfirmacaoTests()
    {
        _atendentes.ObterUsuarioAtivoAsync(_empresaId, _outra, Arg.Any<CancellationToken>())
            .Returns(new UsuarioDaEmpresa(_outra, "Bia", "bia@x.com", [new PerfilNaEmpresa(NivelAcesso.Operador, [])]));
        _atendentes.ObterUsuarioAtivoAsync(_empresaId, _destino, Arg.Any<CancellationToken>())
            .Returns(new UsuarioDaEmpresa(_destino, "Ana", "ana@x.com", [new PerfilNaEmpresa(NivelAcesso.Operador, [])]));
    }

    private GerenciarConversaAtendimentoUseCase UseCase() => new(_conversas, _uow, atendentes: _atendentes);

    private Conversa ConversaDe(Guid? dona)
    {
        var conversa = Conversa.Abrir(_empresaId, "5511988887777", DateTime.UtcNow.AddMinutes(-5));
        if (dona is { } d) conversa.Assumir(DateTime.UtcNow, d);
        _conversas.ObterPorIdComLockAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        return conversa;
    }

    private AcaoConversaCommand Comando(Guid conversaId, bool confirmada = false) =>
        new(_empresaId, _minha, conversaId, "Thati") { Confirmada = confirmada };

    [Fact]
    public async Task AssumirConversaDeOutraSemConfirmacaoRecusaComAvisoEmPortugues()
    {
        var conversa = ConversaDe(_outra);

        var act = () => UseCase().AssumirAsync(Comando(conversa.Id));
        var erro = await act.Should().ThrowAsync<ConversaAtribuidaOutraException>();

        erro.Which.Message.Should().Be("Conversa com Bia. Confirme para assumir.");
        conversa.AssumidaPorUsuarioId.Should().Be(_outra);
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task AssumirConversaDeOutraComConfirmacaoTrocaOResponsavel()
    {
        var conversa = ConversaDe(_outra);

        var r = await UseCase().AssumirAsync(Comando(conversa.Id, confirmada: true));

        r.AssumidaPorUsuarioId.Should().Be(_minha);
    }

    [Fact]
    public async Task AssumirConversaLivreOuPropriaNaoPedeConfirmacao()
    {
        var livre = ConversaDe(null);
        (await UseCase().AssumirAsync(Comando(livre.Id))).AssumidaPorUsuarioId.Should().Be(_minha);

        var propria = ConversaDe(_minha);
        (await UseCase().AssumirAsync(Comando(propria.Id))).AssumidaPorUsuarioId.Should().Be(_minha);
    }

    [Fact]
    public async Task TransferirConversaDeOutraSemConfirmacaoRecusa()
    {
        var conversa = ConversaDe(_outra);
        var useCase = new TransferirConversaUseCase(_conversas, _atendentes, _uow);

        var act = () => useCase.ExecuteAsync(new TransferirConversaCommand(_empresaId, _minha, conversa.Id, _destino, "Thati"));

        await act.Should().ThrowAsync<ConversaAtribuidaOutraException>();
        conversa.AssumidaPorUsuarioId.Should().Be(_outra);
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task TransferirConversaDeOutraComConfirmacaoPassa()
    {
        var conversa = ConversaDe(_outra);
        var useCase = new TransferirConversaUseCase(_conversas, _atendentes, _uow);

        var r = await useCase.ExecuteAsync(new TransferirConversaCommand(_empresaId, _minha, conversa.Id, _destino, "Thati", Confirmada: true));

        r.AssumidaPorUsuarioId.Should().Be(_destino);
    }
}
