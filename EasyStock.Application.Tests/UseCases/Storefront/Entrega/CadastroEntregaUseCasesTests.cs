using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Entrega;
using EasyStock.Domain.Entities.Storefront;
using Microsoft.Extensions.Time.Testing;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Storefront.Entrega;

/// <summary>
/// Cadastro de janelas, zonas e bloqueios pela própria loja (S45). A loja é sempre a da empresa do
/// token; id de outra loja é 404, sem vazar existência (as tabelas não têm EmpresaId).
/// </summary>
public class CadastroEntregaUseCasesTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly StorefrontEntity _loja;
    private readonly IStorefrontRepository _lojas = Substitute.For<IStorefrontRepository>();
    private readonly IJanelaEntregaRepository _janelas = Substitute.For<IJanelaEntregaRepository>();
    private readonly IFreteZonaRepository _zonas = Substitute.For<IFreteZonaRepository>();
    private readonly IBloqueioEntregaRepository _bloqueios = Substitute.For<IBloqueioEntregaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    public CadastroEntregaUseCasesTests()
    {
        _loja = StorefrontEntity.Criar(empresaId: _empresaId, slug: "casa-da-baba", tituloPublico: "Casa da Babá", pedidoMinimoEntrega: 0m);
        _lojas.GetByEmpresaAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(_loja);
    }

    private CadastroJanelasEntregaUseCase Janelas() => new(_lojas, _janelas, _uow);
    private CadastroZonasFreteUseCase Zonas() => new(_lojas, _zonas, _uow);
    // 15/10/2026, 9 h em Brasília (quinta-feira): o "hoje" da loja nos bloqueios (#1776, item 21).
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
    private CadastroBloqueiosEntregaUseCase Bloqueios() => new(_lojas, _bloqueios, _janelas, _uow, _relogio);

    [Fact]
    public async Task SemVitrine_Lanca()
    {
        _lojas.GetByEmpresaAsync(_empresaId, Arg.Any<CancellationToken>()).Returns((StorefrontEntity?)null);

        var act = () => Janelas().ListarAsync(_empresaId);

        await act.Should().ThrowAsync<LojaSemVitrineException>();
    }

    [Fact]
    public async Task CriarJanela_NaLojaDaEmpresa()
    {
        var resultado = await Janelas().CriarAsync(_empresaId, new JanelaEntregaInput(2, new TimeOnly(9, 0), new TimeOnly(12, 0), 8, "Manhã"));

        await _janelas.Received(1).AddAsync(Arg.Is<JanelaEntrega>(j => j.StorefrontId == _loja.Id && j.Label == "Manhã"), Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
        resultado.Ativa.Should().BeTrue();
    }

    [Fact]
    public async Task EditarJanelaDeOutraLoja_NaoEncontrado()
    {
        var alheia = JanelaEntrega.Criar(Guid.NewGuid(), 1, new TimeOnly(9, 0), new TimeOnly(12, 0), 5, "Outra");
        _janelas.GetByIdAsync(alheia.Id, Arg.Any<CancellationToken>()).Returns(alheia);

        var act = () => Janelas().AtualizarAsync(_empresaId, alheia.Id, new JanelaEntregaInput(1, new TimeOnly(9, 0), new TimeOnly(13, 0), 5, "X"));

        await act.Should().ThrowAsync<CadastroEntregaNaoEncontradoException>();
        alheia.Label.Should().Be("Outra");
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DesativarJanela_DaLoja()
    {
        var janela = JanelaEntrega.Criar(_loja.Id, 1, new TimeOnly(9, 0), new TimeOnly(12, 0), 5, "Manhã");
        _janelas.GetByIdAsync(janela.Id, Arg.Any<CancellationToken>()).Returns(janela);

        var resultado = await Janelas().DefinirAtivaAsync(_empresaId, janela.Id, ativa: false);

        resultado.Ativa.Should().BeFalse();
        await _uow.Received(1).CommitAsync();
    }

    // #1440: excluir a janela criada por engano. Com pedido ou bloqueio apontando para ela,
    // recusa e pede para pausar (as FKs são RESTRICT; o histórico de vagas não some).
    [Fact]
    public async Task ExcluirJanelaSemUso_Remove()
    {
        var janela = JanelaEntrega.Criar(_loja.Id, 4, new TimeOnly(12, 0), new TimeOnly(14, 0), 6, "Almoço");
        _janelas.GetByIdAsync(janela.Id, Arg.Any<CancellationToken>()).Returns(janela);
        _janelas.TemUsoAsync(janela.Id, Arg.Any<CancellationToken>()).Returns(false);

        await Janelas().ExcluirAsync(_empresaId, janela.Id);

        await _janelas.Received(1).RemoveAsync(janela, Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task ExcluirJanelaComPedidoOuBloqueio_RecusaEPedeParaPausar()
    {
        var janela = JanelaEntrega.Criar(_loja.Id, 4, new TimeOnly(12, 0), new TimeOnly(14, 0), 6, "Almoço");
        _janelas.GetByIdAsync(janela.Id, Arg.Any<CancellationToken>()).Returns(janela);
        _janelas.TemUsoAsync(janela.Id, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => Janelas().ExcluirAsync(_empresaId, janela.Id);

        (await act.Should().ThrowAsync<UseCaseValidationException>()).WithMessage("*Pause*");
        await _janelas.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default);
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task ExcluirJanelaDeOutraLoja_NaoEncontrado()
    {
        var alheia = JanelaEntrega.Criar(Guid.NewGuid(), 1, new TimeOnly(9, 0), new TimeOnly(12, 0), 5, "Outra");
        _janelas.GetByIdAsync(alheia.Id, Arg.Any<CancellationToken>()).Returns(alheia);

        var act = () => Janelas().ExcluirAsync(_empresaId, alheia.Id);

        await act.Should().ThrowAsync<CadastroEntregaNaoEncontradoException>();
        await _janelas.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default);
    }

    [Fact]
    public async Task CriarZonaPorBairros_EEditarParaCep()
    {
        var criada = await Zonas().CriarAsync(_empresaId,
            new FreteZonaInput("Oeste", 8m, 40, 0, CepInicio: null, CepFim: null, Bairros: ["Butantã"]));
        criada.TipoCobertura.Should().Be(FreteZona.TipoBairrosLista);
        criada.Bairros.Should().Equal("butanta");

        var zona = FreteZona.CriarPorBairros(_loja.Id, "Oeste", ["Butantã"], 8m, 40);
        _zonas.GetByIdAsync(zona.Id, Arg.Any<CancellationToken>()).Returns(zona);

        var editada = await Zonas().AtualizarAsync(_empresaId, zona.Id,
            new FreteZonaInput("Centro", 9m, 30, 1, CepInicio: "01000-000", CepFim: "01099-999", Bairros: null));

        editada.TipoCobertura.Should().Be(FreteZona.TipoCepRange);
        editada.CepInicio.Should().Be("01000000");
        editada.Bairros.Should().BeEmpty();
        editada.Label.Should().Be("Centro");
    }

    [Fact]
    public async Task ZonaComCepEBairrosAoMesmoTempo_Recusa()
    {
        var act = () => Zonas().CriarAsync(_empresaId,
            new FreteZonaInput("Mista", 8m, 40, 0, "01000000", "01099999", ["Centro"]));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _zonas.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task BloqueioComJanelaDeOutraLoja_NaoEncontrado()
    {
        var alheia = JanelaEntrega.Criar(Guid.NewGuid(), 1, new TimeOnly(9, 0), new TimeOnly(12, 0), 5, "Outra");
        _janelas.GetByIdAsync(alheia.Id, Arg.Any<CancellationToken>()).Returns(alheia);

        var act = () => Bloqueios().CriarAsync(_empresaId, new BloqueioEntregaInput(new DateOnly(2026, 12, 25), "Natal", alheia.Id));

        await act.Should().ThrowAsync<CadastroEntregaNaoEncontradoException>();
        await _bloqueios.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task RemoverBloqueio_SoDaLoja()
    {
        var meu = BloqueioEntrega.Criar(_loja.Id, new DateOnly(2026, 12, 25), "Natal");
        var alheio = BloqueioEntrega.Criar(Guid.NewGuid(), new DateOnly(2026, 12, 25), "Natal");
        _bloqueios.GetByIdAsync(meu.Id, Arg.Any<CancellationToken>()).Returns(meu);
        _bloqueios.GetByIdAsync(alheio.Id, Arg.Any<CancellationToken>()).Returns(alheio);

        await Bloqueios().RemoverAsync(_empresaId, meu.Id);
        var act = () => Bloqueios().RemoverAsync(_empresaId, alheio.Id);

        await act.Should().ThrowAsync<CadastroEntregaNaoEncontradoException>();
        await _bloqueios.Received(1).RemoveAsync(meu, Arg.Any<CancellationToken>());
        await _bloqueios.DidNotReceive().RemoveAsync(alheio, Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
    }

    // ── #1776, itens 20 a 22 ───────────────────────────────────────────

    private JanelaEntrega JanelaDaLoja(int dia, int inicio, int fim)
    {
        var janela = JanelaEntrega.Criar(_loja.Id, dia, new TimeOnly(inicio, 0), new TimeOnly(fim, 0), 4, $"{inicio}h");
        _janelas.GetByIdAsync(janela.Id, Arg.Any<CancellationToken>()).Returns(janela);
        return janela;
    }

    private void JanelasDaLoja(params JanelaEntrega[] janelas) =>
        _janelas.GetTodasDoStorefrontAsync(_loja.Id, Arg.Any<CancellationToken>()).Returns(janelas);

    [Theory]
    [InlineData(12, 14)] // igual
    [InlineData(13, 15)] // sobreposta
    [InlineData(11, 13)]
    [InlineData(12, 13)] // dentro
    public async Task CriarJanela_SobrepostaNoMesmoDia_Recusa(int inicio, int fim)
    {
        JanelasDaLoja(JanelaDaLoja(2, 12, 14));

        var act = () => Janelas().CriarAsync(_empresaId, new JanelaEntregaInput(2, new TimeOnly(inicio, 0), new TimeOnly(fim, 0), 4, "Nova"));

        (await act.Should().ThrowAsync<UseCaseValidationException>()).WithMessage("*terça*12:00*14:00*");
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task CriarJanela_EncostadaOuEmOutroDia_Aceita()
    {
        JanelasDaLoja(JanelaDaLoja(2, 12, 14));

        await Janelas().CriarAsync(_empresaId, new JanelaEntregaInput(2, new TimeOnly(14, 0), new TimeOnly(16, 0), 4, "Tarde"));
        await Janelas().CriarAsync(_empresaId, new JanelaEntregaInput(3, new TimeOnly(12, 0), new TimeOnly(14, 0), 4, "Almoço"));

        await _uow.Received(2).CommitAsync();
    }

    [Fact]
    public async Task AtualizarJanela_NaoConflitaConsigoMesma_MasConflitaComAOutra()
    {
        var almoco = JanelaDaLoja(2, 12, 14);
        var tarde = JanelaDaLoja(2, 15, 17);
        JanelasDaLoja(almoco, tarde);

        await Janelas().AtualizarAsync(_empresaId, almoco.Id, new JanelaEntregaInput(2, new TimeOnly(11, 0), new TimeOnly(14, 0), 6, "11h"));
        var act = () => Janelas().AtualizarAsync(_empresaId, almoco.Id, new JanelaEntregaInput(2, new TimeOnly(11, 0), new TimeOnly(16, 0), 6, "11h"));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        almoco.HoraFim.Should().Be(new TimeOnly(14, 0));
    }

    [Fact]
    public async Task AtualizarJanela_DuplicataAntiga_MudaVagasSemMudarHorario()
    {
        var a = JanelaDaLoja(2, 12, 14);
        var b = JanelaDaLoja(2, 12, 14);
        JanelasDaLoja(a, b);

        await Janelas().AtualizarAsync(_empresaId, a.Id, new JanelaEntregaInput(2, new TimeOnly(12, 0), new TimeOnly(14, 0), 9, "Almoço"));

        a.CapacidadeMaxima.Should().Be(9);
    }

    [Fact]
    public async Task ExcluirZona_DaLoja_RemoveESoDaLoja()
    {
        var minha = FreteZona.CriarPorBairros(_loja.Id, "Centro", ["Centro"], 8m, 30, 1);
        var alheia = FreteZona.CriarPorBairros(Guid.NewGuid(), "Centro", ["Centro"], 8m, 30, 1);
        _zonas.GetByIdAsync(minha.Id, Arg.Any<CancellationToken>()).Returns(minha);
        _zonas.GetByIdAsync(alheia.Id, Arg.Any<CancellationToken>()).Returns(alheia);

        await Zonas().ExcluirAsync(_empresaId, minha.Id);
        var act = () => Zonas().ExcluirAsync(_empresaId, alheia.Id);

        await act.Should().ThrowAsync<CadastroEntregaNaoEncontradoException>();
        await _zonas.Received(1).RemoveAsync(minha, Arg.Any<CancellationToken>());
        await _zonas.DidNotReceive().RemoveAsync(alheia, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Bloqueio_NoPassado_Recusa()
    {
        var act = () => Bloqueios().CriarAsync(_empresaId, new BloqueioEntregaInput(new DateOnly(2026, 10, 14), "Ontem", null));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*passou*");
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Bloqueio_Hoje_Aceita()
    {
        await Bloqueios().CriarAsync(_empresaId, new BloqueioEntregaInput(new DateOnly(2026, 10, 15), "Falta gás", null));

        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Bloqueio_AlemDeUmAno_Recusa()
    {
        var act = () => Bloqueios().CriarAsync(_empresaId, new BloqueioEntregaInput(new DateOnly(2027, 10, 17), "Longe", null));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task Bloqueio_JanelaDeOutroDiaDaSemana_Recusa()
    {
        var domingo = JanelaDaLoja(0, 12, 14);

        // 21/10/2026 é quarta.
        var act = () => Bloqueios().CriarAsync(_empresaId, new BloqueioEntregaInput(new DateOnly(2026, 10, 21), "Folga", domingo.Id));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*domingo*");
    }

    [Theory]
    [InlineData(true, true)]   // dia inteiro de novo
    [InlineData(true, false)]  // janela num dia já fechado
    [InlineData(false, false)] // a mesma janela de novo
    [InlineData(false, true)]  // dia inteiro com a janela já fechada: aceita, cobre o resto do dia
    public async Task Bloqueio_JaCoberto_Recusa(bool existenteDiaInteiro, bool novoDiaInteiro)
    {
        var quarta = JanelaDaLoja(3, 12, 14);
        var data = new DateOnly(2026, 10, 21);
        var existente = BloqueioEntrega.Criar(_loja.Id, data, "Folga", existenteDiaInteiro ? null : quarta.Id);
        _bloqueios.GetByStorefrontPeriodoAsync(_loja.Id, data, data, Arg.Any<CancellationToken>()).Returns([existente]);

        var act = () => Bloqueios().CriarAsync(_empresaId, new BloqueioEntregaInput(data, "De novo", novoDiaInteiro ? null : quarta.Id));

        if (!existenteDiaInteiro && novoDiaInteiro)
            await act.Should().NotThrowAsync();
        else
            await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*já*bloque*");
    }
}
