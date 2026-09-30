using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Application.UseCases.Campanhas;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Enums.Campanhas;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Campanhas;

/// <summary>S28: cadastro, agendamento e cancelamento da campanha pelo console.</summary>
public class CampanhasUseCasesTests
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 30, 15, 0, 0, TimeSpan.Zero);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _usuarioId = Guid.NewGuid();
    private readonly ICampanhaRepository _repo = Substitute.For<ICampanhaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _relogio = new(Agora);

    private static DadosCampanha Dados(string mensagem = "Oi {{nome}}") =>
        new("Bolo de fubá", mensagem, null, "novidade_semana", FiltroCampanha.ParaTodos, ["vegano"], null, false, null);

    private Campanha Existente()
    {
        var campanha = Campanha.Criar(_empresaId, _usuarioId, Dados(), Agora.UtcDateTime);
        _repo.ObterAsync(_empresaId, campanha.Id, Arg.Any<CancellationToken>()).Returns(campanha);
        return campanha;
    }

    [Fact]
    public async Task CriarComDisparoJaNasceAgendada()
    {
        var useCase = new CriarCampanhaUseCase(_repo, _uow, _relogio);

        var resultado = await useCase.ExecuteAsync(new SalvarCampanhaCommand(
            _empresaId, _usuarioId, Dados(), Agora.UtcDateTime.AddDays(1)));

        resultado.Status.Should().Be(StatusCampanha.Agendada);
        resultado.DisparoEm.Should().Be(Agora.UtcDateTime.AddDays(1));
        resultado.CriadaPorUsuarioId.Should().Be(_usuarioId);
        resultado.TagsRestricaoExcluidas.Should().Equal("vegano");
        await _repo.Received(1).AddAsync(Arg.Is<Campanha>(c => c.EmpresaId == _empresaId), Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task CriarSemDisparoFicaRascunho()
    {
        var useCase = new CriarCampanhaUseCase(_repo, _uow, _relogio);

        var resultado = await useCase.ExecuteAsync(new SalvarCampanhaCommand(_empresaId, _usuarioId, Dados(""), null));

        resultado.Status.Should().Be(StatusCampanha.Rascunho);
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task CriarComDisparoNoPassadoNaoGrava()
    {
        var useCase = new CriarCampanhaUseCase(_repo, _uow, _relogio);

        var criar = () => useCase.ExecuteAsync(new SalvarCampanhaCommand(
            _empresaId, _usuarioId, Dados(), Agora.UtcDateTime.AddMinutes(-1)));

        await criar.Should().ThrowAsync<RegraDeDominioVioladaException>();
        await _repo.DidNotReceive().AddAsync(Arg.Any<Campanha>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task AtualizarSemDisparoDesagendaAntesDeAplicar()
    {
        var campanha = Existente();
        campanha.Agendar(Agora.UtcDateTime.AddDays(1), Agora.UtcDateTime);
        var useCase = new AtualizarCampanhaUseCase(_repo, _uow, _relogio);

        // Mensagem vazia só vale em rascunho: por isso desagendar vem primeiro.
        var resultado = await useCase.ExecuteAsync(campanha.Id, new SalvarCampanhaCommand(_empresaId, _usuarioId, Dados(""), null));

        resultado.Status.Should().Be(StatusCampanha.Rascunho);
        resultado.DisparoEm.Should().BeNull();
        resultado.Mensagem.Should().BeEmpty();
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task AtualizarDeOutraEmpresaOuInexistenteLancaNaoEncontrada()
    {
        var useCase = new AtualizarCampanhaUseCase(_repo, _uow, _relogio);

        var atualizar = () => useCase.ExecuteAsync(Guid.NewGuid(), new SalvarCampanhaCommand(_empresaId, _usuarioId, Dados(), null));

        await atualizar.Should().ThrowAsync<CampanhaNaoEncontradaException>();
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task CancelarEmEnviandoExcluiSoOsPendentes()
    {
        var campanha = Existente();
        campanha.Agendar(Agora.UtcDateTime.AddHours(1), Agora.UtcDateTime);
        campanha.IniciarOnda(Agora.UtcDateTime.AddHours(1));
        var pendente = CampanhaDestinatario.Criar(campanha, Guid.NewGuid());
        _repo.ListarPendentesAsync(_empresaId, campanha.Id, Arg.Any<CancellationToken>()).Returns([pendente]);
        var useCase = new CancelarCampanhaUseCase(_repo, _uow);

        var resultado = await useCase.ExecuteAsync(_empresaId, campanha.Id);

        resultado.Campanha.Status.Should().Be(StatusCampanha.Cancelada);
        resultado.DestinatariosExcluidos.Should().Be(1);
        pendente.MotivoExclusao.Should().Be(MotivoExclusaoCampanha.Cancelada);
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task ObterEListarRespeitamAEmpresa()
    {
        var campanha = Existente();
        _repo.ListarAsync(_empresaId, StatusCampanha.Rascunho, ListarCampanhasUseCase.LimitePadrao, Arg.Any<CancellationToken>())
            .Returns([campanha]);

        (await new ObterCampanhaUseCase(_repo).ExecuteAsync(_empresaId, campanha.Id)).Id.Should().Be(campanha.Id);
        (await new ListarCampanhasUseCase(_repo).ExecuteAsync(_empresaId, StatusCampanha.Rascunho))
            .Should().ContainSingle().Which.Id.Should().Be(campanha.Id);

        var deOutra = () => new ObterCampanhaUseCase(_repo).ExecuteAsync(Guid.NewGuid(), campanha.Id);
        await deOutra.Should().ThrowAsync<CampanhaNaoEncontradaException>();
    }
}
