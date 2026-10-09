using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Agendamento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.TestHelpers;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Storefront.Agendamento;

/// <summary>
/// Testes do <see cref="ListarJanelasDisponiveisUseCase"/> (TASK-EZ-AGEND-001).
///
/// <para>Cobertura:</para>
/// <list type="bullet">
///   <item>Happy path — janela com vagas disponíveis.</item>
///   <item>Janela esgotada — vagasRestantes=0, esgotado=true, incluída no retorno.</item>
///   <item>Dia inteiro bloqueado por BloqueioEntrega → janela omitida.</item>
///   <item>Janela específica bloqueada na data → só aquela omitida.</item>
///   <item>CEP formato inválido → CepInvalidoException.</item>
///   <item>CEP fora de cobertura → CepSemCoberturaException.</item>
///   <item>Período superior a 60 dias → RegraDeDominioVioladaException.</item>
///   <item>Storefront inexistente/inativo → StorefrontNaoEncontradoException.</item>
///   <item>Sem janelas ativas → array vazio.</item>
/// </list>
/// </summary>
public class ListarJanelasDisponiveisUseCaseTests
{
    private const string SlugValido = "casa-da-baba";

    // ── Fixture ───────────────────────────────────────────────────────────

    private sealed record Fakes(
        IStorefrontRepository StorefrontRepo,
        IJanelaEntregaRepository JanelaRepo,
        IBloqueioEntregaRepository BloqueioRepo,
        IVagaOcupadaRepository VagaRepo,
        IFreteZonaRepository FreteZonaRepo,
        StorefrontEntity Storefront);

    private static Fakes BuildFakes(bool storefrontAtivo = true)
    {
        var storefront = StorefrontEntity.Criar(
            empresaId: Guid.NewGuid(),
            slug: SlugValido,
            tituloPublico: "Casa da Babá",
            pedidoMinimoEntrega: 0m);
        if (storefrontAtivo) storefront.Ativar();

        var storefrontRepo = Substitute.For<IStorefrontRepository>();
        storefrontRepo.GetBySlugAsync(SlugValido, Arg.Any<CancellationToken>())
            .Returns(storefront);

        var janelaRepo = Substitute.For<IJanelaEntregaRepository>();
        janelaRepo.GetAtivasDoStorefrontAsync(storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new List<JanelaEntrega>());

        var bloqueioRepo = Substitute.For<IBloqueioEntregaRepository>();
        bloqueioRepo.GetByStorefrontPeriodoAsync(
                storefront.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<BloqueioEntrega>());

        var vagaRepo = Substitute.For<IVagaOcupadaRepository>();
        vagaRepo.ContarPorJanelaPeriodoAsync(
                Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<(Guid, DateOnly), int>());

        var freteZonaRepo = Substitute.For<IFreteZonaRepository>();
        freteZonaRepo.GetAtivasDoStorefrontOrdenadasAsync(storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new List<FreteZona>());

        return new Fakes(storefrontRepo, janelaRepo, bloqueioRepo, vagaRepo, freteZonaRepo, storefront);
    }

    private static ListarJanelasDisponiveisUseCase BuildUseCase(Fakes f, TimeProvider? relogio = null) => new(
        f.StorefrontRepo,
        f.JanelaRepo,
        f.BloqueioRepo,
        f.VagaRepo,
        f.FreteZonaRepo,
        relogio ?? new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero))); // #1506: antes das datas fixas

    // ── Helper: cria janela para o dia da semana de uma data específica ──

    private static JanelaEntrega CriarJanela(Guid storefrontId, DateOnly data, int capacidade = 5)
    {
        var diaDaSemana = (int)data.DayOfWeek;
        return JanelaEntrega.Criar(
            storefrontId: storefrontId,
            diaDaSemana: diaDaSemana,
            horaInicio: new TimeOnly(9, 0),
            horaFim: new TimeOnly(12, 0),
            capacidadeMaxima: capacidade,
            label: "Manhã 9-12h");
    }

    // ── Testes ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_HappyPath_RetornaJanelaComVagasRestantes()
    {
        var f = BuildFakes();
        var data = new DateOnly(2026, 6, 2); // segunda-feira
        var janela = CriarJanela(f.Storefront.Id, data, capacidade: 5);

        f.JanelaRepo.GetAtivasDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new List<JanelaEntrega> { janela });

        // 2 vagas ocupadas de 5
        f.VagaRepo.ContarPorJanelaPeriodoAsync(
                Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<(Guid, DateOnly), int> { [(janela.Id, data)] = 2 });

        var input = new ListarJanelasDisponiveisInput(SlugValido, data, data, null);
        var result = await BuildUseCase(f).ExecuteAsync(input);

        result.Should().HaveCount(1);
        var dto = result[0];
        dto.JanelaId.Should().Be(janela.Id);
        dto.VagasRestantes.Should().Be(3);
        dto.Capacidade.Should().Be(5);
        dto.Esgotado.Should().BeFalse();
        dto.Data.Should().Be(data);
    }

    [Fact]
    public async Task ExecuteAsync_JanelaEsgotada_IncluiComEsgotadoTrue()
    {
        var f = BuildFakes();
        var data = new DateOnly(2026, 6, 2);
        var janela = CriarJanela(f.Storefront.Id, data, capacidade: 3);

        f.JanelaRepo.GetAtivasDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new List<JanelaEntrega> { janela });

        // 3 ocupadas de 3 = esgotada
        f.VagaRepo.ContarPorJanelaPeriodoAsync(
                Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<(Guid, DateOnly), int> { [(janela.Id, data)] = 3 });

        var input = new ListarJanelasDisponiveisInput(SlugValido, data, data, null);
        var result = await BuildUseCase(f).ExecuteAsync(input);

        result.Should().HaveCount(1);
        result[0].VagasRestantes.Should().Be(0);
        result[0].Esgotado.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_DiaInteiroBloqueado_OmiteJanelaDaquelesDias()
    {
        var f = BuildFakes();
        var dataBloqueada = new DateOnly(2026, 6, 2);
        var janela = CriarJanela(f.Storefront.Id, dataBloqueada, capacidade: 5);

        f.JanelaRepo.GetAtivasDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new List<JanelaEntrega> { janela });

        // Bloqueio de dia inteiro (JanelaEspecificaId = null)
        var bloqueio = BloqueioEntrega.Criar(f.Storefront.Id, dataBloqueada, "Feriado");
        f.BloqueioRepo.GetByStorefrontPeriodoAsync(
                f.Storefront.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<BloqueioEntrega> { bloqueio });

        var input = new ListarJanelasDisponiveisInput(SlugValido, dataBloqueada, dataBloqueada, null);
        var result = await BuildUseCase(f).ExecuteAsync(input);

        result.Should().BeEmpty("dia inteiro bloqueado deve omitir todas as janelas da data");
    }

    [Fact]
    public async Task ExecuteAsync_JanelaEspecificaBloqueada_OmiteSoAquela()
    {
        var f = BuildFakes();
        var data = new DateOnly(2026, 6, 2); // segunda-feira
        var janelaA = JanelaEntrega.Criar(f.Storefront.Id, (int)data.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(12, 0), 5, "Manhã");
        var janelaB = JanelaEntrega.Criar(f.Storefront.Id, (int)data.DayOfWeek, new TimeOnly(14, 0), new TimeOnly(17, 0), 5, "Tarde");

        f.JanelaRepo.GetAtivasDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new List<JanelaEntrega> { janelaA, janelaB });

        // Bloqueia apenas janelaA naquela data
        var bloqueio = BloqueioEntrega.Criar(f.Storefront.Id, data, "Manutenção manhã", janelaEspecificaId: janelaA.Id);
        f.BloqueioRepo.GetByStorefrontPeriodoAsync(
                f.Storefront.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<BloqueioEntrega> { bloqueio });

        var input = new ListarJanelasDisponiveisInput(SlugValido, data, data, null);
        var result = await BuildUseCase(f).ExecuteAsync(input);

        result.Should().HaveCount(1);
        result[0].JanelaId.Should().Be(janelaB.Id, "apenas a janela específica bloqueada deve ser omitida");
    }

    [Fact]
    public async Task ExecuteAsync_CepFormatoInvalido_LancaCepInvalidoException()
    {
        var f = BuildFakes();
        var data = new DateOnly(2026, 6, 2);
        var input = new ListarJanelasDisponiveisInput(SlugValido, data, data, "05500"); // menos de 8 dígitos

        var act = () => BuildUseCase(f).ExecuteAsync(input);

        await act.Should().ThrowAsync<CepInvalidoException>();
    }

    [Fact]
    public async Task ExecuteAsync_CepForaDeZona_LancaCepSemCoberturaException()
    {
        var f = BuildFakes();
        var data = new DateOnly(2026, 6, 2);

        // Zona que NÃO cobre o CEP informado
        var zona = FreteZona.CriarPorCep(f.Storefront.Id, "Zona Sul", "04000000", "04999999", 10m, 30);
        f.FreteZonaRepo.GetAtivasDoStorefrontOrdenadasAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new List<FreteZona> { zona });

        var input = new ListarJanelasDisponiveisInput(SlugValido, data, data, "01310100"); // CEP fora da zona

        var act = () => BuildUseCase(f).ExecuteAsync(input);

        await act.Should().ThrowAsync<CepSemCoberturaException>();
    }

    [Fact]
    public async Task ExecuteAsync_PeriodoAcimaDeMaximo_LancaRegraDeDominioVioladaException()
    {
        var f = BuildFakes();
        var inicio = new DateOnly(2026, 6, 1);
        var fim = inicio.AddDays(61); // 61 dias > 60 máximo

        var input = new ListarJanelasDisponiveisInput(SlugValido, inicio, fim, null);

        var act = () => BuildUseCase(f).ExecuteAsync(input);

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>()
            .WithMessage("*60*");
    }

    [Fact]
    public async Task ExecuteAsync_StorefrontInexistente_LancaStorefrontNaoEncontradoException()
    {
        var f = BuildFakes();
        f.StorefrontRepo.GetBySlugAsync("inexistente", Arg.Any<CancellationToken>())
            .Returns((StorefrontEntity?)null);

        var data = new DateOnly(2026, 6, 2);
        var input = new ListarJanelasDisponiveisInput("inexistente", data, data, null);

        var act = () => BuildUseCase(f).ExecuteAsync(input);

        await act.Should().ThrowAsync<StorefrontNaoEncontradoException>();
    }

    [Fact]
    public async Task ExecuteAsync_StorefrontInativo_LancaStorefrontNaoEncontradoException()
    {
        var f = BuildFakes(storefrontAtivo: false);
        var data = new DateOnly(2026, 6, 2);
        var input = new ListarJanelasDisponiveisInput(SlugValido, data, data, null);

        var act = () => BuildUseCase(f).ExecuteAsync(input);

        await act.Should().ThrowAsync<StorefrontNaoEncontradoException>();
    }

    [Fact]
    public async Task ExecuteAsync_SemJanelasAtivas_RetornaArrayVazio()
    {
        var f = BuildFakes();
        // janelaRepo já mockado para retornar lista vazia no BuildFakes
        var data = new DateOnly(2026, 6, 2);
        var input = new ListarJanelasDisponiveisInput(SlugValido, data, data, null);

        var result = await BuildUseCase(f).ExecuteAsync(input);

        result.Should().BeEmpty();
    }
    // ── S16: antecedência mínima ─────────────────────────────────────────

    [Fact]
    public async Task CortaPorPrazoMinimo()
    {
        // Hoje 11:00 em Brasília (14:00Z), terça 02/06/2026; prazo 100 min → nada antes das 12:40.
        var f = BuildFakes();
        var hoje = new DateOnly(2026, 6, 2);
        var amanha = hoje.AddDays(1);
        var relogio = new FakeTimeProvider(new DateTimeOffset(2026, 6, 2, 14, 0, 0, TimeSpan.Zero));

        JanelaEntrega Janela(DateOnly dia, int hora) => JanelaEntrega.Criar(
            f.Storefront.Id, (int)dia.DayOfWeek, new TimeOnly(hora, 0), new TimeOnly(hora + 1, 0), 5, $"{hora}h");
        var hoje12 = Janela(hoje, 12);
        var hoje13 = Janela(hoje, 13);
        var amanha12 = Janela(amanha, 12);
        var amanha13 = Janela(amanha, 13);
        f.JanelaRepo.GetAtivasDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new List<JanelaEntrega> { hoje12, hoje13, amanha12, amanha13 });

        var input = new ListarJanelasDisponiveisInput(SlugValido, hoje, amanha, null, PrazoMinimoMinutos: 100);
        var result = await BuildUseCase(f, relogio).ExecuteAsync(input);

        result.Select(r => (r.Data, r.JanelaId)).Should().Equal(
            (hoje, hoje13.Id),
            (amanha, amanha12.Id),
            (amanha, amanha13.Id));
    }

    [Fact]
    public async Task SemPrazoMinimo_NaoCorta()
    {
        // 11:00 em Brasilia; a janela 12-13h ficaria fora com prazo de 100 min, sem prazo fica.
        var f = BuildFakes();
        var hoje = new DateOnly(2026, 6, 2);
        var relogio = new FakeTimeProvider(new DateTimeOffset(2026, 6, 2, 14, 0, 0, TimeSpan.Zero));
        var janela = JanelaEntrega.Criar(f.Storefront.Id, (int)hoje.DayOfWeek, new TimeOnly(12, 0), new TimeOnly(13, 0), 5, "12h");
        f.JanelaRepo.GetAtivasDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new List<JanelaEntrega> { janela });

        var result = await BuildUseCase(f, relogio).ExecuteAsync(new ListarJanelasDisponiveisInput(SlugValido, hoje, hoje, null));

        result.Should().ContainSingle(r => r.JanelaId == janela.Id);
    }

    [Fact]
    public async Task JanelaQueJaTerminou_SaiMesmoSemPrazo()
    {
        // #1506: 11:00 em Brasilia; a janela 9-10h de hoje acabou e o site a oferecia (cliente pagava
        // por horario passado). Data passada inteira tambem sai.
        var f = BuildFakes();
        var hoje = new DateOnly(2026, 6, 2);
        var ontem = hoje.AddDays(-1);
        var relogio = new FakeTimeProvider(new DateTimeOffset(2026, 6, 2, 14, 0, 0, TimeSpan.Zero));
        var encerrada = JanelaEntrega.Criar(f.Storefront.Id, (int)hoje.DayOfWeek, new TimeOnly(9, 0), new TimeOnly(10, 0), 5, "9h");
        var deOntem = JanelaEntrega.Criar(f.Storefront.Id, (int)ontem.DayOfWeek, new TimeOnly(15, 0), new TimeOnly(16, 0), 5, "15h");
        f.JanelaRepo.GetAtivasDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new List<JanelaEntrega> { encerrada, deOntem });

        var result = await BuildUseCase(f, relogio).ExecuteAsync(new ListarJanelasDisponiveisInput(SlugValido, ontem, hoje, null));

        result.Should().BeEmpty();
    }
}
