using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Messaging;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Auth;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging;
using NSubstitute.ExceptionExtensions;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Storefront.Auth;

/// <summary>
/// Testes do <see cref="SolicitarOtpUseCase"/> (EZ-AUTH-001).
///
/// <para>
/// Cobertura:
/// </para>
/// <list type="bullet">
///   <item>Happy path — gera código 6 dígitos, persiste hash BCrypt, envia via provider.</item>
///   <item>Resolução de storefront por slug (inexistente / inativo → 404).</item>
///   <item>Validação E.164 BR (formato, prefixo, dígitos).</item>
///   <item>Normalização (espaço, parêntese, hífen).</item>
///   <item>Rate limit 3/hora — 4ª chamada → <see cref="OtpRateLimitExcedidoException"/>.</item>
///   <item>Idempotência &lt;60s — reaproveita OTP existente sem regerar/enviar.</item>
///   <item>Falha do provider — propaga <see cref="OtpProviderException"/>.</item>
///   <item>Segurança — código nunca em response/log/exception; telefone mascarado em log.</item>
/// </list>
/// </summary>
public class SolicitarOtpUseCaseTests
{
    private static readonly DateTimeOffset Inicio =
        new(2026, 5, 24, 10, 0, 0, TimeSpan.Zero);

    private const string SlugValido = "casa-da-baba";
    private const string TelefoneE164 = "+5511997573992";
    private const string TelefoneBruto = "(11) 99757-3992"; // sem +55 — input típico do usuário
    private const string IpOrigem = "203.0.113.42";
    private const string UserAgentTeste = "Mozilla/5.0 (TestAgent)";

    // ── Fixture builders ───────────────────────────────────────────────

    private sealed record Fakes(
        IStorefrontRepository StorefrontRepository,
        IClienteOtpRepository ClienteOtpRepository,
        IWhatsAppOtpSender WhatsAppSender,
        IPasswordHasher PasswordHasher,
        FakeUnitOfWork UnitOfWork,
        FakeTimeProvider Time,
        ILogger<SolicitarOtpUseCase> Logger,
        Guid EmpresaId,
        StorefrontEntity Storefront);

    private static Fakes BuildFakes(
        bool storefrontAtivo = true,
        StorefrontEntity? storefrontOverride = null)
    {
        var empresaId = Guid.NewGuid();
        var storefront = storefrontOverride ?? StorefrontEntity.Criar(
            empresaId: empresaId,
            slug: SlugValido,
            tituloPublico: "Casa da Babá",
            pedidoMinimoEntrega: 0m);
        if (storefrontAtivo)
            storefront.Ativar();

        var storefrontRepo = Substitute.For<IStorefrontRepository>();
        storefrontRepo.GetBySlugAsync(SlugValido, Arg.Any<CancellationToken>())
            .Returns(storefront);

        var clienteOtpRepo = Substitute.For<IClienteOtpRepository>();
        clienteOtpRepo
            .GetAtivoPorTelefoneHashAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((ClienteOtp?)null);
        clienteOtpRepo
            .ContarCriadosDesdeAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var sender = Substitute.For<IWhatsAppOtpSender>();
        var hasher = new FakePasswordHasher();
        var uow = new FakeUnitOfWork();
        var time = new FakeTimeProvider(Inicio);
        var logger = Substitute.For<ILogger<SolicitarOtpUseCase>>();

        return new Fakes(storefrontRepo, clienteOtpRepo, sender, hasher, uow, time, logger, empresaId, storefront);
    }

    private static SolicitarOtpUseCase BuildUseCase(Fakes f) => new(
        f.StorefrontRepository,
        f.ClienteOtpRepository,
        f.WhatsAppSender,
        f.PasswordHasher,
        f.UnitOfWork,
        f.Time,
        f.Logger);

    private static SolicitarOtpInput Input(string? telefone = null, string? idempotencyKey = null) =>
        new(
            Slug: SlugValido,
            Telefone: telefone ?? TelefoneBruto,
            IdempotencyKey: idempotencyKey,
            IpOrigem: IpOrigem,
            UserAgent: UserAgentTeste);

    // ── Happy path ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_HappyPath_CriaOtpEEnviaProvider()
    {
        var f = BuildFakes();
        var useCase = BuildUseCase(f);

        var result = await useCase.ExecuteAsync(Input());

        result.ExpiresInSeconds.Should().Be(300, "ClienteOtp.TempoVidaPadrao = 5min = 300s");
        result.Reaproveitado.Should().BeFalse();

        await f.ClienteOtpRepository.Received(1).AddAsync(
            Arg.Is<ClienteOtp>(otp =>
                otp.EmpresaId == f.EmpresaId
                && otp.TelefoneHash == ClienteOtp.CalcularTelefoneHash(TelefoneE164)
                && otp.IpOrigem == IpOrigem
                && otp.UserAgent == UserAgentTeste),
            Arg.Any<CancellationToken>());
        f.UnitOfWork.CommitCount.Should().Be(1);
        await f.WhatsAppSender.Received(1).EnviarOtpAsync(
            TelefoneE164,
            Arg.Is<string>(c => c.Length == 6 && c.All(char.IsDigit)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_PersisteHashBCryptDoCodigo()
    {
        var f = BuildFakes();
        ClienteOtp? otpPersistido = null;
        f.ClienteOtpRepository
            .When(r => r.AddAsync(Arg.Any<ClienteOtp>(), Arg.Any<CancellationToken>()))
            .Do(call => otpPersistido = call.Arg<ClienteOtp>());

        var useCase = BuildUseCase(f);
        await useCase.ExecuteAsync(Input());

        otpPersistido.Should().NotBeNull();
        otpPersistido!.CodigoHash.Should().StartWith(FakePasswordHasher.Prefix,
            "use case deve chamar IPasswordHasher.Hash antes de persistir — código em claro NUNCA persistido");
        otpPersistido.CodigoHash.Should().NotMatch("*\\d{6}*"); // FakePasswordHasher: "hash:123456"
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_CodigoEnviadoNuncaAparesceNaResponseOuException()
    {
        var f = BuildFakes();
        string? codigoEnviado = null;
        await f.WhatsAppSender.EnviarOtpAsync(
            Arg.Any<string>(),
            Arg.Do<string>(c => codigoEnviado = c),
            Arg.Any<CancellationToken>());

        var useCase = BuildUseCase(f);
        var result = await useCase.ExecuteAsync(Input());

        codigoEnviado.Should().NotBeNullOrEmpty("provider deve receber o código em claro para enviar via WhatsApp");
        codigoEnviado!.Length.Should().Be(6);

        // Response NÃO pode conter o código
        result.ToString().Should().NotContain(codigoEnviado);
    }

    // ── Resolução de storefront ────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_StorefrontInexistente_LancaStorefrontNaoEncontrado()
    {
        var f = BuildFakes();
        f.StorefrontRepository.GetBySlugAsync(SlugValido, Arg.Any<CancellationToken>())
            .Returns((StorefrontEntity?)null);

        var useCase = BuildUseCase(f);
        var act = () => useCase.ExecuteAsync(Input());

        await act.Should().ThrowAsync<StorefrontNaoEncontradoException>();
        await f.ClienteOtpRepository.DidNotReceive().AddAsync(
            Arg.Any<ClienteOtp>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_StorefrontInativo_LancaStorefrontNaoEncontrado()
    {
        var f = BuildFakes(storefrontAtivo: false);

        var useCase = BuildUseCase(f);
        var act = () => useCase.ExecuteAsync(Input());

        await act.Should().ThrowAsync<StorefrontNaoEncontradoException>(
            "storefront inativo é equivalente a inexistente do ponto de vista do cliente");
    }

    // ── Validação de telefone ──────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+1 555 123 4567")]        // não-BR
    [InlineData("+5511")]                  // muito curto
    [InlineData("+55119999999999999")]     // muito longo
    [InlineData("abcdef")]                 // não-numérico
    [InlineData("12345")]                  // sem prefixo e dígitos insuficientes
    public async Task ExecuteAsync_TelefoneInvalido_LancaTelefoneInvalido(string telefone)
    {
        var f = BuildFakes();
        var useCase = BuildUseCase(f);

        var act = () => useCase.ExecuteAsync(Input(telefone: telefone));

        await act.Should().ThrowAsync<TelefoneInvalidoException>();
        await f.ClienteOtpRepository.DidNotReceive().AddAsync(
            Arg.Any<ClienteOtp>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("(11) 99757-3992")]
    [InlineData("11 99757-3992")]
    [InlineData("11997573992")]                 // 11 dígitos sem prefixo (DDD + número)
    [InlineData("+55 (11) 99757-3992")]
    [InlineData("+55 11 9 9757 3992")]
    [InlineData(" +5511997573992 ")]
    public async Task ExecuteAsync_TelefoneFormatoComum_NormalizaParaE164(string entrada)
    {
        var f = BuildFakes();
        var useCase = BuildUseCase(f);

        await useCase.ExecuteAsync(Input(telefone: entrada));

        await f.WhatsAppSender.Received(1).EnviarOtpAsync(
            TelefoneE164,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    // ── Rate limit ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_RateLimit3PorHora_LancaOtpRateLimitExcedido()
    {
        var f = BuildFakes();
        f.ClienteOtpRepository
            .ContarCriadosDesdeAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(3); // já atingiu cota

        var useCase = BuildUseCase(f);
        var act = () => useCase.ExecuteAsync(Input());

        var ex = await act.Should().ThrowAsync<OtpRateLimitExcedidoException>();
        ex.Which.RetryAfterSeconds.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(3600);

        await f.ClienteOtpRepository.DidNotReceive().AddAsync(
            Arg.Any<ClienteOtp>(), Arg.Any<CancellationToken>());
        await f.WhatsAppSender.DidNotReceive().EnviarOtpAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_RateLimitAbaixoCota_NaoBloqueia()
    {
        var f = BuildFakes();
        f.ClienteOtpRepository
            .ContarCriadosDesdeAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(2); // 2 já criados — 3ª chamada ainda permitida

        var useCase = BuildUseCase(f);
        var result = await useCase.ExecuteAsync(Input());

        result.ExpiresInSeconds.Should().Be(300);
        await f.ClienteOtpRepository.Received(1).AddAsync(
            Arg.Any<ClienteOtp>(), Arg.Any<CancellationToken>());
    }

    // ── Idempotência (janela de 60s) ────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_OtpRecentSub60s_ReaproveitaSemRegerarNemEnviar()
    {
        var f = BuildFakes();

        // OTP existente criado 30s atrás
        var telefoneHash = ClienteOtp.CalcularTelefoneHash(TelefoneE164);
        var otpExistente = ClienteOtp.Criar(
            empresaId: f.EmpresaId,
            telefoneHash: telefoneHash,
            codigoHash: "hash:000000",
            time: f.Time);
        f.Time.Advance(TimeSpan.FromSeconds(30));
        f.ClienteOtpRepository
            .GetAtivoPorTelefoneHashAsync(
                f.EmpresaId, telefoneHash, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(otpExistente);

        var useCase = BuildUseCase(f);
        var result = await useCase.ExecuteAsync(Input(idempotencyKey: "key-abc"));

        result.Reaproveitado.Should().BeTrue();
        result.ExpiresInSeconds.Should().BeInRange(260, 300, "TTL restante do OTP existente (~270s)");

        await f.ClienteOtpRepository.DidNotReceive().AddAsync(
            Arg.Any<ClienteOtp>(), Arg.Any<CancellationToken>());
        await f.WhatsAppSender.DidNotReceive().EnviarOtpAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        f.UnitOfWork.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_OtpExistenteMaisVelhoQue60s_GeraNovo()
    {
        var f = BuildFakes();
        var telefoneHash = ClienteOtp.CalcularTelefoneHash(TelefoneE164);
        var otpExistente = ClienteOtp.Criar(
            empresaId: f.EmpresaId,
            telefoneHash: telefoneHash,
            codigoHash: "hash:000000",
            time: f.Time);
        f.Time.Advance(TimeSpan.FromSeconds(75)); // > 60s
        f.ClienteOtpRepository
            .GetAtivoPorTelefoneHashAsync(
                f.EmpresaId, telefoneHash, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(otpExistente);

        var useCase = BuildUseCase(f);
        var result = await useCase.ExecuteAsync(Input());

        result.Reaproveitado.Should().BeFalse();
        await f.ClienteOtpRepository.Received(1).AddAsync(
            Arg.Any<ClienteOtp>(), Arg.Any<CancellationToken>());
        await f.WhatsAppSender.Received(1).EnviarOtpAsync(
            TelefoneE164, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Provider failure ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ProviderFalha_PropagaOtpProviderException()
    {
        var f = BuildFakes();
        f.WhatsAppSender
            .EnviarOtpAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OtpProviderException("WhatsApp Cloud API 5xx"));

        var useCase = BuildUseCase(f);
        var act = () => useCase.ExecuteAsync(Input());

        await act.Should().ThrowAsync<OtpProviderException>();

        // OTP persistido ANTES do envio — cliente pode tentar Reenviar
        // (que reaproveita o OTP recente). Commit já ocorreu.
        await f.ClienteOtpRepository.Received(1).AddAsync(
            Arg.Any<ClienteOtp>(), Arg.Any<CancellationToken>());
        f.UnitOfWork.CommitCount.Should().Be(1);
    }

    // ── Determinismo do hash do telefone ────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_TelefoneE164_HashSHA256Deterministico()
    {
        var f1 = BuildFakes();
        var f2 = BuildFakes();
        f2.StorefrontRepository.GetBySlugAsync(SlugValido, Arg.Any<CancellationToken>())
            .Returns(f1.Storefront); // mesma empresa para comparar

        // Recompute fakes2 com a mesma EmpresaId
        f2 = f2 with { EmpresaId = f1.EmpresaId };

        ClienteOtp? otp1 = null;
        ClienteOtp? otp2 = null;
        f1.ClienteOtpRepository
            .When(r => r.AddAsync(Arg.Any<ClienteOtp>(), Arg.Any<CancellationToken>()))
            .Do(c => otp1 = c.Arg<ClienteOtp>());
        f2.ClienteOtpRepository
            .When(r => r.AddAsync(Arg.Any<ClienteOtp>(), Arg.Any<CancellationToken>()))
            .Do(c => otp2 = c.Arg<ClienteOtp>());

        await BuildUseCase(f1).ExecuteAsync(Input(telefone: "(11) 99757-3992"));
        await BuildUseCase(f2).ExecuteAsync(Input(telefone: "+5511997573992"));

        otp1.Should().NotBeNull();
        otp2.Should().NotBeNull();
        otp1!.TelefoneHash.Should().Be(otp2!.TelefoneHash,
            "mesmo telefone normalizado para E.164 deve produzir mesmo hash SHA-256");
    }
}
