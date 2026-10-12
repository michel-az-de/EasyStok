using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Entrega;
using EasyStock.Domain.Entities.Storefront;
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
    private CadastroBloqueiosEntregaUseCase Bloqueios() => new(_lojas, _bloqueios, _janelas, _uow);

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
}
