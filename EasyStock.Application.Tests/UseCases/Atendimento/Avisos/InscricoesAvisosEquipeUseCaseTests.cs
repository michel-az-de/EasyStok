using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.Atendimento.Avisos;
using EasyStock.Domain.Entities.Notifications;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Avisos;

/// <summary>#2211: a dona escolhe quem da equipe recebe cada aviso no WhatsApp. A inscrição é a preferência da rotina.</summary>
public class InscricoesAvisosEquipeUseCaseTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IAudienciaUsuarios _usuarios = Substitute.For<IAudienciaUsuarios>();
    private readonly IPreferenciaNotificacaoRepository _preferencias = Substitute.For<IPreferenciaNotificacaoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static UsuarioParaAudiencia Pessoa(string nome, bool telefoneConfirmado = true, bool ativo = true) =>
        new(Guid.NewGuid(), nome, $"{nome}@x.com", true, ativo, telefoneConfirmado ? "+5511999990000" : null,
            telefoneConfirmado ? DateTime.UtcNow : null);

    private void Equipe(params UsuarioParaAudiencia[] pessoas) =>
        _usuarios.ListarDaEmpresaAsync(_empresaId, Arg.Any<IReadOnlyCollection<NivelAcesso>>(), Arg.Any<CancellationToken>())
            .Returns(pessoas);

    [Fact]
    public async Task ListaAEquipeAtivaComOsAvisosEOTelefone()
    {
        var thati = Pessoa("Thatiane");
        var ana = Pessoa("Ana", telefoneConfirmado: false);
        Equipe(thati, ana, Pessoa("Saiu", ativo: false));
        _preferencias.ListarDaRotinaAsync(_empresaId, AvisosDaEquipe.PedidoComCanhoto.RotinaCodigo,
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([PreferenciaNotificacaoUsuario.Criar(thati.Id, _empresaId, AvisosDaEquipe.PedidoComCanhoto.RotinaCodigo)]);
        _preferencias.ListarDaRotinaAsync(_empresaId, AvisosDaEquipe.ProblemaNoPagamento.RotinaCodigo,
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([PreferenciaNotificacaoUsuario.Criar(thati.Id, _empresaId, AvisosDaEquipe.ProblemaNoPagamento.RotinaCodigo, habilitada: false)]);

        var lista = await new ListarInscricoesAvisosEquipeUseCase(_usuarios, _preferencias).ExecuteAsync(_empresaId);

        lista.Should().HaveCount(2);
        var linha = lista.Single(p => p.UsuarioId == thati.Id);
        linha.Should().Be(new PessoaAvisadaResult(thati.Id, "Thatiane", true, Atencao: false, Pedido: true, Pagamento: false));
        lista.Single(p => p.UsuarioId == ana.Id).TelefoneConfirmado.Should().BeFalse();
    }

    [Fact]
    public async Task SalvaOsTresAvisosDaPessoaEConfirma()
    {
        var thati = Pessoa("Thatiane");
        Equipe(thati);

        var resultado = await new SalvarInscricaoAvisosEquipeUseCase(_usuarios, _preferencias, _uow)
            .ExecuteAsync(_empresaId, thati.Id, atencao: true, pedido: true, pagamento: false);

        resultado.Should().Be(new PessoaAvisadaResult(thati.Id, "Thatiane", true, true, true, false));
        await _preferencias.Received(1).DefinirAsync(thati.Id, _empresaId, AvisosDaEquipe.AtencaoNoAtendimento.RotinaCodigo, true, Arg.Any<CancellationToken>());
        await _preferencias.Received(1).DefinirAsync(thati.Id, _empresaId, AvisosDaEquipe.PedidoComCanhoto.RotinaCodigo, true, Arg.Any<CancellationToken>());
        await _preferencias.Received(1).DefinirAsync(thati.Id, _empresaId, AvisosDaEquipe.ProblemaNoPagamento.RotinaCodigo, false, Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task PessoaDeOutraLojaNaoEInscrita()
    {
        Equipe(Pessoa("Thatiane"));

        var acao = () => new SalvarInscricaoAvisosEquipeUseCase(_usuarios, _preferencias, _uow)
            .ExecuteAsync(_empresaId, Guid.NewGuid(), true, true, true);

        await acao.Should().ThrowAsync<UseCaseValidationException>();
        await _preferencias.DidNotReceiveWithAnyArgs().DefinirAsync(default, default, default!, default);
        await _uow.DidNotReceiveWithAnyArgs().CommitAsync();
    }
}
