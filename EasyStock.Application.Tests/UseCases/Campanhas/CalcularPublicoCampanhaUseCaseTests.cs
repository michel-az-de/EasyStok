using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Application.UseCases.Campanhas;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Enums.Campanhas;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Campanhas;

/// <summary>
/// S29: público da campanha com filtros, exclusões com motivo e o limite de uma campanha por semana.
/// Recalcular refaz pendentes e excluídos e não toca em quem já recebeu.
/// </summary>
public class CalcularPublicoCampanhaUseCaseTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 30, 15, 0, 0, TimeSpan.Zero);
    private static DateTime Hoje => Agora.UtcDateTime;

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ICampanhaRepository _repo = Substitute.For<ICampanhaRepository>();
    private readonly ICampanhaPublicoQueries _queries = Substitute.For<ICampanhaPublicoQueries>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly List<CampanhaDestinatario> _existentes = [];
    private readonly List<CampanhaDestinatario> _adicionados = [];
    private readonly List<CampanhaDestinatario> _removidos = [];

    public CalcularPublicoCampanhaUseCaseTests()
    {
        _repo.AddDestinatariosAsync(Arg.Do<IEnumerable<CampanhaDestinatario>>(d => _adicionados.AddRange(d)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _repo.When(r => r.RemoverDestinatarios(Arg.Any<IEnumerable<CampanhaDestinatario>>()))
            .Do(c => _removidos.AddRange(c.Arg<IEnumerable<CampanhaDestinatario>>()));
    }

    private CalcularPublicoCampanhaUseCase UseCase() => new(_repo, _queries, _uow, new FakeTimeProvider(Agora));

    private Campanha Campanha(FiltroCampanha? filtro = null, params string[] restricoes)
    {
        var campanha = Domain.Entities.Campanhas.Campanha.Criar(_empresaId, Guid.NewGuid(),
            new DadosCampanha("Bolo de fubá", "Oi {{nome}}", null, null, filtro ?? FiltroCampanha.ParaTodos,
                restricoes, null, false, null),
            Hoje);
        _repo.ObterAsync(_empresaId, campanha.Id, Arg.Any<CancellationToken>()).Returns(campanha);
        _repo.ListarDestinatariosAsync(_empresaId, campanha.Id, Arg.Any<CancellationToken>()).Returns(_existentes);
        return campanha;
    }

    private void Candidatos(Campanha campanha, params CandidatoPublicoCampanha[] candidatos) =>
        _queries.ListarCandidatosAsync(_empresaId, campanha.Id, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(candidatos);

    private static CandidatoPublicoCampanha Cliente(
        string nome, string[]? tags = null, bool telefone = true, bool bloqueado = false, bool consentiu = true,
        DateTime? comprouEm = null, DateTime? recebeuCampanhaEm = null) =>
        new(Guid.NewGuid(), nome, telefone, bloqueado, consentiu, tags ?? [], comprouEm, recebeuCampanhaEm);

    private CampanhaDestinatario? Destinatario(CandidatoPublicoCampanha cliente) =>
        _existentes.Concat(_adicionados).Except(_removidos).SingleOrDefault(d => d.ClienteId == cliente.ClienteId);

    [Fact]
    public async Task ExcluiRestricao()
    {
        var campanha = Campanha(null, "intolerante_lactose");
        var ana = Cliente("Ana", ["intolerante_lactose", "vegano"]);
        var bia = Cliente("Bia", ["vegano"]);
        Candidatos(campanha, ana, bia);

        var resumo = await UseCase().ExecuteAsync(_empresaId, campanha.Id);

        Destinatario(ana)!.Status.Should().Be(StatusCampanhaDestinatario.Excluido);
        Destinatario(ana)!.MotivoExclusao.Should().Be(MotivoExclusaoCampanha.Restricao);
        Destinatario(bia)!.Status.Should().Be(StatusCampanhaDestinatario.Pendente);
        resumo.Pendentes.Should().Be(1);
        resumo.ExcluidosPorMotivo.Should().Equal(new Dictionary<string, int> { [MotivoExclusaoCampanha.Restricao] = 1 });
        resumo.Amostra.Should().Equal("Bia");
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task LimiteSemanal7Dias()
    {
        var campanha = Campanha();
        var ha5Dias = Cliente("Ana", recebeuCampanhaEm: Hoje.AddDays(-5));
        var ha8Dias = Cliente("Bia", recebeuCampanhaEm: Hoje.AddDays(-8));
        Candidatos(campanha, ha5Dias, ha8Dias);

        await UseCase().ExecuteAsync(_empresaId, campanha.Id);

        Destinatario(ha5Dias)!.MotivoExclusao.Should().Be(MotivoExclusaoCampanha.LimiteSemanal);
        Destinatario(ha8Dias)!.Status.Should().Be(StatusCampanhaDestinatario.Pendente);
    }

    [Fact]
    public async Task RecalculoPreservaEnviados()
    {
        var campanha = Campanha();
        // Recebeu esta campanha hoje; a query não conta a própria campanha, mas mesmo que contasse
        // o enviado não é recalculado.
        var enviado = Cliente("Ana", recebeuCampanhaEm: Hoje);
        var agoraBloqueada = Cliente("Bia", bloqueado: true);
        var agoraLiberada = Cliente("Cris");
        var saiuDoFiltro = Guid.NewGuid();
        var nova = Cliente("Duda");

        var jaEnviado = CampanhaDestinatario.Criar(campanha, enviado.ClienteId);
        jaEnviado.Enfileirar(1, Guid.NewGuid());
        jaEnviado.MarcarEnviado(Hoje.AddHours(-1));
        var eraPendente = CampanhaDestinatario.Criar(campanha, agoraBloqueada.ClienteId);
        var eraExcluido = CampanhaDestinatario.Criar(campanha, agoraLiberada.ClienteId);
        eraExcluido.Excluir(MotivoExclusaoCampanha.SemConsentimento);
        var fora = CampanhaDestinatario.Criar(campanha, saiuDoFiltro);
        _existentes.AddRange([jaEnviado, eraPendente, eraExcluido, fora]);
        Candidatos(campanha, enviado, agoraBloqueada, agoraLiberada, nova);

        var resumo = await UseCase().ExecuteAsync(_empresaId, campanha.Id);

        jaEnviado.Status.Should().Be(StatusCampanhaDestinatario.Enviado);
        jaEnviado.EnviadoEm.Should().Be(Hoje.AddHours(-1));
        eraPendente.Status.Should().Be(StatusCampanhaDestinatario.Excluido);
        eraPendente.MotivoExclusao.Should().Be(MotivoExclusaoCampanha.Bloqueado);
        eraExcluido.Status.Should().Be(StatusCampanhaDestinatario.Pendente);
        eraExcluido.MotivoExclusao.Should().BeNull();
        _removidos.Should().Equal(fora);
        _adicionados.Should().ContainSingle().Which.ClienteId.Should().Be(nova.ClienteId);
        resumo.Total.Should().Be(4);
        resumo.Preservados.Should().Be(1);
        resumo.Pendentes.Should().Be(2);
        resumo.ExcluidosPorMotivo.Values.Sum().Should().Be(1, "a soma por motivo é o número de excluídos");
        resumo.Amostra.Should().Equal("Cris", "Duda");
    }

    [Fact]
    public async Task FiltroComprouItem()
    {
        var item = Guid.NewGuid();
        var campanha = Campanha(new FiltroCampanha(false, [], [], item, 30));
        var recente = Cliente("Ana", comprouEm: Hoje.AddDays(-10));
        var antiga = Cliente("Bia", comprouEm: Hoje.AddDays(-40));
        var nunca = Cliente("Cris");
        Candidatos(campanha, recente, antiga, nunca);

        var resumo = await UseCase().ExecuteAsync(_empresaId, campanha.Id);

        await _queries.Received(1).ListarCandidatosAsync(_empresaId, campanha.Id, item, Arg.Any<CancellationToken>());
        Destinatario(recente)!.Status.Should().Be(StatusCampanhaDestinatario.Pendente);
        Destinatario(antiga).Should().BeNull("comprou fora da janela: não entra no público");
        Destinatario(nunca).Should().BeNull();
        resumo.Total.Should().Be(1);
    }

    [Fact]
    public async Task FiltroDeTagsExigeTodasAsIncluidasEBarraAsExcluidas()
    {
        var campanha = Campanha(new FiltroCampanha(false, ["vegano", "encomenda"], ["risco"], null, null));
        var todas = Cliente("Ana", ["vegano", "encomenda"]);
        var soUma = Cliente("Bia", ["vegano"]);
        var comRisco = Cliente("Cris", ["vegano", "encomenda", "risco"]);
        Candidatos(campanha, todas, soUma, comRisco);

        var resumo = await UseCase().ExecuteAsync(_empresaId, campanha.Id);

        resumo.Total.Should().Be(1);
        Destinatario(todas)!.Status.Should().Be(StatusCampanhaDestinatario.Pendente);
    }

    [Fact]
    public async Task ExclusoesSeguemAOrdemDaSpecEOResumoBateComOsDestinatarios()
    {
        var campanha = Campanha(null, "sem_gluten");
        // Cada cliente cai em todos os motivos a partir do seu: vale o primeiro da ordem.
        var bloqueado = Cliente("A", ["sem_gluten"], telefone: false, bloqueado: true, consentiu: false, recebeuCampanhaEm: Hoje);
        var semConsentimento = Cliente("B", ["sem_gluten"], telefone: false, consentiu: false, recebeuCampanhaEm: Hoje);
        var restricao = Cliente("C", ["sem_gluten"], telefone: false, recebeuCampanhaEm: Hoje);
        var limite = Cliente("D", telefone: false, recebeuCampanhaEm: Hoje);
        var semTelefone = Cliente("E", telefone: false);
        Candidatos(campanha, bloqueado, semConsentimento, restricao, limite, semTelefone);

        var resumo = await UseCase().ExecuteAsync(_empresaId, campanha.Id);

        Destinatario(bloqueado)!.MotivoExclusao.Should().Be(MotivoExclusaoCampanha.Bloqueado);
        Destinatario(semConsentimento)!.MotivoExclusao.Should().Be(MotivoExclusaoCampanha.SemConsentimento);
        Destinatario(restricao)!.MotivoExclusao.Should().Be(MotivoExclusaoCampanha.Restricao);
        Destinatario(limite)!.MotivoExclusao.Should().Be(MotivoExclusaoCampanha.LimiteSemanal);
        Destinatario(semTelefone)!.MotivoExclusao.Should().Be(MotivoExclusaoCampanha.SemTelefone);
        resumo.Pendentes.Should().Be(0);
        resumo.ExcluidosPorMotivo.Values.Sum().Should().Be(_adicionados.Count(d => d.Status == StatusCampanhaDestinatario.Excluido));
        resumo.ExcluidosPorMotivo.Should().HaveCount(5).And.AllSatisfy(m => m.Value.Should().Be(1));
    }

    [Fact]
    public async Task CampanhaCanceladaNaoRecalcula()
    {
        var campanha = Campanha();
        campanha.Cancelar([]);

        var recalcular = () => UseCase().ExecuteAsync(_empresaId, campanha.Id);

        await recalcular.Should().ThrowAsync<RegraDeDominioVioladaException>();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task CampanhaDeOutraEmpresaLancaNaoEncontrada()
    {
        var recalcular = () => UseCase().ExecuteAsync(_empresaId, Guid.NewGuid());

        await recalcular.Should().ThrowAsync<CampanhaNaoEncontradaException>();
        await _queries.DidNotReceive().ListarCandidatosAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }
}
