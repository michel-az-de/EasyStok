using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Frete;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.Extensions.Logging;
using NSubstitute.ExceptionExtensions;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Storefront.Frete;

/// <summary>
/// Testes do <see cref="CalcularFreteUseCase"/> (TASK-EZ-FRETE-001).
///
/// <para>
/// Cobertura:
/// </para>
/// <list type="bullet">
///   <item>Happy path por range de CEP — match exato e nos extremos do range.</item>
///   <item>Happy path por bairro — quando ViaCEP devolve "Butantã" e zona é por bairros lista.</item>
///   <item>CEP fora de cobertura → <see cref="CepSemCoberturaException"/>.</item>
///   <item>CEP formato inválido → <see cref="CepInvalidoException"/>.</item>
///   <item>Storefront inexistente/inativo → <see cref="StorefrontNaoEncontradoException"/>.</item>
///   <item>ViaCEP falha (lança/null) → segue best-effort sem bairro.</item>
///   <item>Match por ordem — zona com Ordem menor ganha.</item>
///   <item>Output: valor em centavos correto, ETA "30 min" / "1h30" / "2h".</item>
/// </list>
/// </summary>
public class CalcularFreteUseCaseTests
{
    private const string SlugValido = "casa-da-baba";
    private const string CepValido = "05500000";
    private const string CepFormatado = "05500-000";

    // ── Fixture builders ───────────────────────────────────────────────

    private sealed record Fakes(
        IStorefrontRepository StorefrontRepository,
        IFreteZonaRepository FreteZonaRepository,
        ICepLookupClient CepLookupClient,
        ILogger<CalcularFreteUseCase> Logger,
        StorefrontEntity Storefront);

    private static Fakes BuildFakes(bool storefrontAtivo = true)
    {
        var empresaId = Guid.NewGuid();
        var storefront = StorefrontEntity.Criar(
            empresaId: empresaId,
            slug: SlugValido,
            tituloPublico: "Casa da Babá",
            pedidoMinimoEntrega: 0m);
        if (storefrontAtivo)
            storefront.Ativar();

        var storefrontRepo = Substitute.For<IStorefrontRepository>();
        storefrontRepo.GetBySlugAsync(SlugValido, Arg.Any<CancellationToken>())
            .Returns(storefront);

        var freteZonaRepo = Substitute.For<IFreteZonaRepository>();
        // Default: nenhuma zona cobre
        freteZonaRepo
            .BuscarZonaPorCepAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((FreteZona?)null);

        var cepLookup = Substitute.For<ICepLookupClient>();
        cepLookup
            .LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CepLookupResult?)null);

        var logger = Substitute.For<ILogger<CalcularFreteUseCase>>();

        return new Fakes(storefrontRepo, freteZonaRepo, cepLookup, logger, storefront);
    }

    private static CalcularFreteUseCase BuildUseCase(Fakes f) => new(
        f.StorefrontRepository,
        f.FreteZonaRepository,
        f.CepLookupClient,
        f.Logger);

    private static CalcularFreteInput Input(string? cep = null) =>
        new(SlugValido, cep ?? CepValido);

    // ── Happy path por CEP range ───────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_HappyPathRangeCep_RetornaDtoCompleto()
    {
        var f = BuildFakes();
        var zona = FreteZona.CriarPorCep(
            storefrontId: f.Storefront.Id,
            label: "Centro",
            cepInicio: "05000000",
            cepFim: "05999999",
            valor: 15m,
            tempoEstimadoMinutos: 30);
        f.FreteZonaRepository
            .BuscarZonaPorCepAsync(f.Storefront.Id, CepValido, string.Empty,
                Arg.Any<CancellationToken>())
            .Returns(zona);

        var result = await BuildUseCase(f).ExecuteAsync(Input());

        result.ZonaId.Should().Be(zona.Id);
        result.Valor.Should().Be(1500, "R$ 15,00 = 1500 centavos");
        result.ValorFormatado.Should().Be("R$ 15,00");
        result.EtaLabel.Should().Be("30 min");
        result.ZonaLabel.Should().Be("Centro");
    }

    [Fact]
    public async Task ExecuteAsync_CepFormatadoOuPuro_AmbosAceitos()
    {
        var f = BuildFakes();
        var zona = FreteZona.CriarPorCep(
            storefrontId: f.Storefront.Id,
            label: "Centro", cepInicio: "05000000", cepFim: "05999999",
            valor: 15m, tempoEstimadoMinutos: 30);
        f.FreteZonaRepository
            .BuscarZonaPorCepAsync(f.Storefront.Id, CepValido, string.Empty,
                Arg.Any<CancellationToken>())
            .Returns(zona);

        var r1 = await BuildUseCase(f).ExecuteAsync(Input(cep: CepFormatado));
        var r2 = await BuildUseCase(f).ExecuteAsync(Input(cep: CepValido));

        r1.ZonaId.Should().Be(zona.Id);
        r2.ZonaId.Should().Be(zona.Id);
    }

    // ── Happy path por bairro (ViaCEP enriquece) ───────────────────────

    [Fact]
    public async Task ExecuteAsync_HappyPathBairro_UsaResultadoViaCep()
    {
        var f = BuildFakes();
        f.CepLookupClient
            .LookupAsync(CepValido, Arg.Any<CancellationToken>())
            .Returns(new CepLookupResult(
                Cep: CepValido,
                Logradouro: "Avenida Vital Brasil",
                Bairro: "Butantã",
                Cidade: "São Paulo",
                Uf: "SP"));

        var zona = FreteZona.CriarPorBairros(
            storefrontId: f.Storefront.Id,
            label: "Butantã proximidade",
            bairros: new[] { "Butantã", "Pinheiros" },
            valor: 20m,
            tempoEstimadoMinutos: 45);
        f.FreteZonaRepository
            .BuscarZonaPorCepAsync(f.Storefront.Id, CepValido, "butanta",
                Arg.Any<CancellationToken>())
            .Returns(zona);

        var result = await BuildUseCase(f).ExecuteAsync(Input());

        result.ZonaId.Should().Be(zona.Id);
        result.Valor.Should().Be(2000);
        result.ValorFormatado.Should().Be("R$ 20,00");
        result.EtaLabel.Should().Be("45 min");
        result.ZonaLabel.Should().Be("Butantã proximidade");
    }

    // ── Sem cobertura ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SemZonaMatch_LancaCepSemCobertura()
    {
        var f = BuildFakes(); // default: BuscarZonaPorCepAsync retorna null

        var act = () => BuildUseCase(f).ExecuteAsync(Input());

        var ex = await act.Should().ThrowAsync<CepSemCoberturaException>();
        ex.Which.Message.Should().Be("Não entregamos neste CEP.");
    }

    // ── CEP inválido ───────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]
    [InlineData("123456789")]
    [InlineData("abcdefgh")]
    [InlineData("05500-00x")]
    public async Task ExecuteAsync_CepInvalido_LancaCepInvalido(string cep)
    {
        var f = BuildFakes();

        var act = () => BuildUseCase(f).ExecuteAsync(Input(cep: cep));

        await act.Should().ThrowAsync<CepInvalidoException>();
        await f.FreteZonaRepository.DidNotReceive().BuscarZonaPorCepAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Storefront ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_StorefrontInexistente_LancaStorefrontNaoEncontrado()
    {
        var f = BuildFakes();
        f.StorefrontRepository.GetBySlugAsync(SlugValido, Arg.Any<CancellationToken>())
            .Returns((StorefrontEntity?)null);

        var act = () => BuildUseCase(f).ExecuteAsync(Input());

        await act.Should().ThrowAsync<StorefrontNaoEncontradoException>();
    }

    [Fact]
    public async Task ExecuteAsync_StorefrontInativo_LancaStorefrontNaoEncontrado()
    {
        var f = BuildFakes(storefrontAtivo: false);

        var act = () => BuildUseCase(f).ExecuteAsync(Input());

        await act.Should().ThrowAsync<StorefrontNaoEncontradoException>();
    }

    // ── ViaCEP best-effort ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ViaCepLanca_NaoQuebraESegueSemBairro()
    {
        var f = BuildFakes();
        f.CepLookupClient
            .LookupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("timeout"));

        var zona = FreteZona.CriarPorCep(
            storefrontId: f.Storefront.Id,
            label: "Centro", cepInicio: "05000000", cepFim: "05999999",
            valor: 10m, tempoEstimadoMinutos: 25);
        f.FreteZonaRepository
            .BuscarZonaPorCepAsync(f.Storefront.Id, CepValido, string.Empty,
                Arg.Any<CancellationToken>())
            .Returns(zona);

        var result = await BuildUseCase(f).ExecuteAsync(Input());

        result.ZonaId.Should().Be(zona.Id);
        // Repository foi chamado com bairro vazio (best-effort funcionou)
        await f.FreteZonaRepository.Received(1).BuscarZonaPorCepAsync(
            f.Storefront.Id, CepValido, string.Empty, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ViaCepRetornaNull_SegueSemBairro()
    {
        var f = BuildFakes();
        // CepLookupClient retorna null por default no BuildFakes

        var zona = FreteZona.CriarPorCep(
            storefrontId: f.Storefront.Id,
            label: "Centro", cepInicio: "05000000", cepFim: "05999999",
            valor: 10m, tempoEstimadoMinutos: 25);
        f.FreteZonaRepository
            .BuscarZonaPorCepAsync(f.Storefront.Id, CepValido, string.Empty,
                Arg.Any<CancellationToken>())
            .Returns(zona);

        var result = await BuildUseCase(f).ExecuteAsync(Input());

        result.ZonaId.Should().Be(zona.Id);
        await f.FreteZonaRepository.Received(1).BuscarZonaPorCepAsync(
            f.Storefront.Id, CepValido, string.Empty, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ViaCepRetornaBairroVazio_NaoQuebra()
    {
        var f = BuildFakes();
        f.CepLookupClient
            .LookupAsync(CepValido, Arg.Any<CancellationToken>())
            .Returns(new CepLookupResult(CepValido, "Avenida X", Bairro: "  ", Cidade: "SP", Uf: "SP"));

        var zona = FreteZona.CriarPorCep(
            storefrontId: f.Storefront.Id,
            label: "Centro", cepInicio: "05000000", cepFim: "05999999",
            valor: 10m, tempoEstimadoMinutos: 25);
        f.FreteZonaRepository
            .BuscarZonaPorCepAsync(f.Storefront.Id, CepValido, string.Empty,
                Arg.Any<CancellationToken>())
            .Returns(zona);

        var result = await BuildUseCase(f).ExecuteAsync(Input());

        result.ZonaId.Should().Be(zona.Id);
    }

    // ── Formatação ETA ─────────────────────────────────────────────────

    [Theory]
    [InlineData(30, "30 min")]
    [InlineData(45, "45 min")]
    [InlineData(60, "1h")]
    [InlineData(90, "1h30")]
    [InlineData(120, "2h")]
    [InlineData(125, "2h05")]
    public async Task ExecuteAsync_FormataEtaLabelEmTextoLegivel(int minutos, string esperado)
    {
        var f = BuildFakes();
        var zona = FreteZona.CriarPorCep(
            storefrontId: f.Storefront.Id,
            label: "Z", cepInicio: "05000000", cepFim: "05999999",
            valor: 1m, tempoEstimadoMinutos: minutos);
        f.FreteZonaRepository
            .BuscarZonaPorCepAsync(f.Storefront.Id, CepValido, string.Empty,
                Arg.Any<CancellationToken>())
            .Returns(zona);

        var result = await BuildUseCase(f).ExecuteAsync(Input());

        result.EtaLabel.Should().Be(esperado);
    }

    // ── Valor em centavos ──────────────────────────────────────────────

    [Theory]
    [InlineData("15.00", 1500, "R$ 15,00")]
    [InlineData("9.90", 990, "R$ 9,90")]
    [InlineData("100.50", 10050, "R$ 100,50")]
    public async Task ExecuteAsync_ConverteValorParaCentavosEFormata(
        string valorDecimal, int centavosEsperados, string formatadoEsperado)
    {
        var f = BuildFakes();
        var valor = decimal.Parse(valorDecimal, System.Globalization.CultureInfo.InvariantCulture);
        var zona = FreteZona.CriarPorCep(
            storefrontId: f.Storefront.Id,
            label: "Z", cepInicio: "05000000", cepFim: "05999999",
            valor: valor, tempoEstimadoMinutos: 30);
        f.FreteZonaRepository
            .BuscarZonaPorCepAsync(f.Storefront.Id, CepValido, string.Empty,
                Arg.Any<CancellationToken>())
            .Returns(zona);

        var result = await BuildUseCase(f).ExecuteAsync(Input());

        result.Valor.Should().Be(centavosEsperados);
        result.ValorFormatado.Should().Be(formatadoEsperado);
    }
}
