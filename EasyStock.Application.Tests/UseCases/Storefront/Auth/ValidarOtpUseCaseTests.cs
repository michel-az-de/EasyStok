using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Auth;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Storefront.Auth;

/// <summary>
/// Testes do <see cref="ValidarOtpUseCase"/> (EZ-AUTH-002).
///
/// <para>Cobertura:</para>
/// <list type="bullet">
///   <item>Happy path — OTP válido → cria ClienteSession + retorna result com SessionId.</item>
///   <item>Cliente novo — criado na primeira validação.</item>
///   <item>Cliente existente — atualiza UltimoAcesso, não duplica.</item>
///   <item>OTP não encontrado → OtpInvalidoException.</item>
///   <item>OTP expirado → OtpExpiradoException.</item>
///   <item>Código errado (1 tentativa) → OtpInvalidoException + tentativas incrementadas.</item>
///   <item>Código errado (5ª tentativa) → OtpTentativasExcedidasException.</item>
///   <item>Storefront não encontrado → StorefrontNaoEncontradoException.</item>
/// </list>
/// </summary>
public class ValidarOtpUseCaseTests
{
    private static readonly DateTimeOffset Inicio =
        new(2026, 5, 24, 10, 0, 0, TimeSpan.Zero);

    private const string SlugValido = "casa-da-baba";
    private const string TelefoneE164 = "+5511997573992";
    private const string TelefoneBruto = "(11) 99757-3992";
    private const string CodigoValido = "123456";
    private const string IpOrigem = "203.0.113.42";
    private const string UserAgentTeste = "Mozilla/5.0 (TestAgent)";
    private const string AcceptLanguageTeste = "pt-BR,pt;q=0.9";

    // ── Fixture builders ─────────────────────────────────────────────────────

    private sealed record Fakes(
        IStorefrontRepository StorefrontRepository,
        IClienteOtpRepository ClienteOtpRepository,
        IClienteStorefrontRepository ClienteRepository,
        IClienteSessionRepository ClienteSessionRepository,
        IPasswordHasher PasswordHasher,
        FakeUnitOfWork UnitOfWork,
        FakeTimeProvider Time,
        ILogger<ValidarOtpUseCase> Logger,
        Guid EmpresaId,
        StorefrontEntity Storefront);

    private static Fakes BuildFakes(
        bool storefrontAtivo = true,
        Cliente? clienteExistente = null)
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

        var time = new FakeTimeProvider(Inicio);
        var hasher = new FakePasswordHasher();
        var telefoneHash = ClienteOtp.CalcularTelefoneHash(TelefoneE164);
        var codigoHash = FakePasswordHasher.MakeHash(CodigoValido);

        // OTP ativo padrão (não expirado, não consumido)
        var otp = ClienteOtp.Criar(
            empresaId: empresaId,
            telefoneHash: telefoneHash,
            codigoHash: codigoHash,
            time: time);

        var clienteOtpRepo = Substitute.For<IClienteOtpRepository>();
        clienteOtpRepo
            .GetAtivoPorTelefoneHashAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(otp);

        var clienteRepo = Substitute.For<IClienteStorefrontRepository>();
        clienteRepo
            .GetByTelefoneHashAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(clienteExistente);

        var sessionRepo = Substitute.For<IClienteSessionRepository>();
        var uow = new FakeUnitOfWork();
        var logger = Substitute.For<ILogger<ValidarOtpUseCase>>();

        return new Fakes(
            storefrontRepo, clienteOtpRepo, clienteRepo, sessionRepo,
            hasher, uow, time, logger, empresaId, storefront);
    }

    private static ValidarOtpUseCase BuildUseCase(Fakes f) => new(
        f.StorefrontRepository,
        f.ClienteOtpRepository,
        f.ClienteRepository,
        f.ClienteSessionRepository,
        f.PasswordHasher,
        f.UnitOfWork,
        f.Time,
        f.Logger);

    private static ValidarOtpInput Input(
        string? telefone = null,
        string? codigo = null) => new(
        Slug: SlugValido,
        Telefone: telefone ?? TelefoneBruto,
        Codigo: codigo ?? CodigoValido,
        IpOrigem: IpOrigem,
        UserAgent: UserAgentTeste,
        AcceptLanguage: AcceptLanguageTeste);

    // ── Happy path — cliente novo ────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_HappyPath_CriaClienteESession()
    {
        var f = BuildFakes();
        var useCase = BuildUseCase(f);

        var result = await useCase.ExecuteAsync(Input());

        result.SessionId.Should().NotBeEmpty();
        result.TelefoneOfuscado.Should().StartWith("+5511").And.Contain("*");
        result.MaxAgeSecs.Should().Be(2592000, "30 dias em segundos");

        await f.ClienteRepository.Received(1).AddAsync(
            Arg.Is<Cliente>(c => c.EmpresaId == f.EmpresaId),
            Arg.Any<CancellationToken>());
        await f.ClienteSessionRepository.Received(1).AddAsync(
            Arg.Is<ClienteSession>(s => s.EmpresaId == f.EmpresaId),
            Arg.Any<CancellationToken>());
        f.UnitOfWork.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_OtpConsumidoAposValidacao()
    {
        var f = BuildFakes();
        ClienteOtp? otpAtualizado = null;
        f.ClienteOtpRepository
            .When(r => r.UpdateAsync(Arg.Any<ClienteOtp>(), Arg.Any<CancellationToken>()))
            .Do(call => otpAtualizado = call.Arg<ClienteOtp>());

        var useCase = BuildUseCase(f);
        await useCase.ExecuteAsync(Input());

        otpAtualizado.Should().NotBeNull("OTP deve ser atualizado após consumo");
        otpAtualizado!.Consumido.Should().BeTrue();
    }

    // ── Cliente existente ────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ClienteExistente_AtualizaUltimoAcessoNaoDuplica()
    {
        var time = new FakeTimeProvider(Inicio);
        var telefoneHash = ClienteOtp.CalcularTelefoneHash(TelefoneE164);
        var clienteExistente = Cliente.CriarParaStorefront(Guid.NewGuid(), telefoneHash, time);

        var f = BuildFakes(clienteExistente: clienteExistente);
        var useCase = BuildUseCase(f);

        await useCase.ExecuteAsync(Input());

        await f.ClienteRepository.DidNotReceive().AddAsync(
            Arg.Any<Cliente>(), Arg.Any<CancellationToken>());
        await f.ClienteRepository.Received(1).UpdateAsync(
            Arg.Any<Cliente>(), Arg.Any<CancellationToken>());
    }

    // ── OTP não encontrado ───────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_OtpNaoEncontrado_LancaOtpInvalido()
    {
        var f = BuildFakes();
        f.ClienteOtpRepository
            .GetAtivoPorTelefoneHashAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns((ClienteOtp?)null);

        var useCase = BuildUseCase(f);

        await useCase.Invoking(u => u.ExecuteAsync(Input()))
            .Should().ThrowAsync<OtpInvalidoException>();
        await f.ClienteRepository.DidNotReceive().AddAsync(
            Arg.Any<Cliente>(), Arg.Any<CancellationToken>());
    }

    // ── OTP expirado ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_OtpExpirado_LancaOtpExpirado()
    {
        var f = BuildFakes();
        f.Time.Advance(TimeSpan.FromMinutes(10)); // > 5min → expirado

        var useCase = BuildUseCase(f);

        await useCase.Invoking(u => u.ExecuteAsync(Input()))
            .Should().ThrowAsync<OtpExpiradoException>();
    }

    // ── Código errado ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CodigoErrado_LancaOtpInvalidoEIncrementaTentativas()
    {
        var f = BuildFakes();
        ClienteOtp? otpAtualizado = null;
        f.ClienteOtpRepository
            .When(r => r.UpdateAsync(Arg.Any<ClienteOtp>(), Arg.Any<CancellationToken>()))
            .Do(call => otpAtualizado = call.Arg<ClienteOtp>());

        var useCase = BuildUseCase(f);

        await useCase.Invoking(u => u.ExecuteAsync(Input(codigo: "000000")))
            .Should().ThrowAsync<OtpInvalidoException>();

        otpAtualizado.Should().NotBeNull();
        otpAtualizado!.Tentativas.Should().Be(1);
        await f.ClienteRepository.DidNotReceive().AddAsync(
            Arg.Any<Cliente>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_5TentativasErradas_LancaOtpTentativasExcedidas()
    {
        var f = BuildFakes();
        var telefoneHash = ClienteOtp.CalcularTelefoneHash(TelefoneE164);
        var time = new FakeTimeProvider(Inicio);
        var otpEsgotado = ClienteOtp.Criar(
            empresaId: f.EmpresaId,
            telefoneHash: telefoneHash,
            codigoHash: FakePasswordHasher.MakeHash(CodigoValido),
            time: time);

        // Simular 4 tentativas já registradas
        for (var i = 0; i < 4; i++)
            otpEsgotado.RegistrarTentativa();

        f.ClienteOtpRepository
            .GetAtivoPorTelefoneHashAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(otpEsgotado);

        var useCase = BuildUseCase(f);

        // 5ª tentativa com código errado → OtpTentativasExcedidasException
        await useCase.Invoking(u => u.ExecuteAsync(Input(codigo: "000000")))
            .Should().ThrowAsync<OtpTentativasExcedidasException>();
    }

    // ── Storefront não encontrado ────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_StorefrontInexistente_LancaStorefrontNaoEncontrado()
    {
        var f = BuildFakes();
        f.StorefrontRepository.GetBySlugAsync(SlugValido, Arg.Any<CancellationToken>())
            .Returns((StorefrontEntity?)null);

        var useCase = BuildUseCase(f);

        await useCase.Invoking(u => u.ExecuteAsync(Input()))
            .Should().ThrowAsync<StorefrontNaoEncontradoException>();
        await f.ClienteOtpRepository.DidNotReceive().GetAtivoPorTelefoneHashAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    // ── Fingerprint na session ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_HappyPath_SessionTemFingerprintCalculado()
    {
        var f = BuildFakes();
        ClienteSession? sessionCriada = null;
        f.ClienteSessionRepository
            .When(r => r.AddAsync(Arg.Any<ClienteSession>(), Arg.Any<CancellationToken>()))
            .Do(call => sessionCriada = call.Arg<ClienteSession>());

        var useCase = BuildUseCase(f);
        await useCase.ExecuteAsync(Input());

        sessionCriada.Should().NotBeNull();
        sessionCriada!.Fingerprint.Should().NotBeNullOrEmpty(
            "fingerprint deve ser calculado com UA + Accept-Language");
        sessionCriada.Fingerprint.Should().HaveLength(64);
    }
}
