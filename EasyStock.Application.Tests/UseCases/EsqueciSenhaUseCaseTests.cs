using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.TestHelpers;
using EasyStock.Application.UseCases.EsqueciSenha;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

public class EsqueciSenhaUseCaseTests
{
    private const string HostConfiavel = "https://app.easystock.com";

    private readonly IUsuarioRepository _usuarioRepository = Substitute.For<IUsuarioRepository>();
    private readonly IResetTokenRepository _resetTokenRepository = Substitute.For<IResetTokenRepository>();
    private readonly IAuditLogRepository _auditLogRepository = Substitute.For<IAuditLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ILogger<EsqueciSenhaUseCase> _logger = Substitute.For<ILogger<EsqueciSenhaUseCase>>();
    private readonly IEmailService _emailService = Substitute.For<IEmailService>();
    private readonly IConfiguration _config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:TrustedLinkOrigins:0"] = HostConfiavel
        })
        .Build();

    public EsqueciSenhaUseCaseTests()
    {
        // Sem isto o mock devolveria null para Task<ResultadoEnvio> (o record e selado e nao e substituivel).
        _emailService.EnviarAsync(Arg.Any<MensagemEmail>(), Arg.Any<CancellationToken>())
            .Returns(new ResultadoEnvio(true, "smtp"));
    }

    private EsqueciSenhaUseCase CriarUseCase(bool comEmail = true) =>
        new(_usuarioRepository, _resetTokenRepository, _auditLogRepository, _unitOfWork, _config, _logger,
            comEmail ? _emailService : null);

    private static Usuario CriarUsuario(string email = "user@empresa.com") =>
        new()
        {
            Id = Guid.NewGuid(),
            Nome = "Teste Usuario",
            Email = email,
            SenhaHash = FakePasswordHasher.MakeHash("Senha@123"),
            Ativo = true,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };

    [Fact]
    public async Task DeveGerarTokenERetornarSucesso_QuandoEmailValido()
    {
        var usuario = CriarUsuario();
        _usuarioRepository.GetByEmailAsync(usuario.Email).Returns(usuario);

        var useCase = CriarUseCase();
        var result = await useCase.ExecuteAsync(new EsqueciSenhaCommand(usuario.Email));

        result.Success.Should().BeTrue();
        await _resetTokenRepository.Received(1).AddAsync(Arg.Any<ResetToken>());
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task DeveRetornarSucessoSemRevelarEmail_QuandoEmailNaoExiste()
    {
        _usuarioRepository.GetByEmailAsync(Arg.Any<string>()).Returns((Usuario?)null);

        var useCase = CriarUseCase();
        var result = await useCase.ExecuteAsync(new EsqueciSenhaCommand("naoexiste@empresa.com"));

        // Deve retornar sucesso mesmo sem usuário para não revelar emails cadastrados
        result.Success.Should().BeTrue();
        await _resetTokenRepository.DidNotReceive().AddAsync(Arg.Any<ResetToken>());
        await _unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task DeveRetornarSucessoSemRevelarEmail_QuandoUsuarioInativo()
    {
        var usuario = CriarUsuario();
        usuario.Ativo = false;
        _usuarioRepository.GetByEmailAsync(usuario.Email).Returns(usuario);

        var useCase = CriarUseCase();
        var result = await useCase.ExecuteAsync(new EsqueciSenhaCommand(usuario.Email));

        result.Success.Should().BeTrue();
        await _resetTokenRepository.DidNotReceive().AddAsync(Arg.Any<ResetToken>());
    }

    [Fact]
    public async Task DeveEnviarEmailDeRecuperacao_QuandoEmailServiceConfigurado()
    {
        var usuario = CriarUsuario("envio@empresa.com");
        _usuarioRepository.GetByEmailAsync(usuario.Email).Returns(usuario);

        var useCase = CriarUseCase(comEmail: true);
        await useCase.ExecuteAsync(new EsqueciSenhaCommand(usuario.Email));

        await _emailService.Received(1).EnviarAsync(
            Arg.Is<MensagemEmail>(m => m.Destinatario == usuario.Email),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnviaPeloRemetenteDeSeguranca()
    {
        // N3 (#1351): o link de reset carrega credencial e nao sai da mesma caixa do relatorio de diagnostico.
        var usuario = CriarUsuario("seguranca@empresa.com");
        _usuarioRepository.GetByEmailAsync(usuario.Email).Returns(usuario);

        var useCase = CriarUseCase(comEmail: true);
        await useCase.ExecuteAsync(new EsqueciSenhaCommand(usuario.Email, HostConfiavel));

        await _emailService.Received(1).EnviarAsync(
            Arg.Is<MensagemEmail>(m => m.Remetente == RemetenteEmail.Seguranca),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NaoDeveEnviarEmail_QuandoEmailServiceNaoConfigurado()
    {
        var usuario = CriarUsuario();
        _usuarioRepository.GetByEmailAsync(usuario.Email).Returns(usuario);

        var useCase = CriarUseCase(comEmail: false);
        await useCase.ExecuteAsync(new EsqueciSenhaCommand(usuario.Email));

        await _emailService.DidNotReceive().EnviarAsync(
            Arg.Any<MensagemEmail>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeveIncluirLinkNoEmail_QuandoBaseUrlConfiavel()
    {
        var usuario = CriarUsuario("link@empresa.com");
        _usuarioRepository.GetByEmailAsync(usuario.Email).Returns(usuario);

        var useCase = CriarUseCase(comEmail: true);
        await useCase.ExecuteAsync(new EsqueciSenhaCommand(usuario.Email, HostConfiavel));

        await _emailService.Received(1).EnviarAsync(
            Arg.Is<MensagemEmail>(m => m.Destinatario == usuario.Email
                && m.Corpo.Contains($"{HostConfiavel}/auth/redefinir-senha?token=")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NaoDeveIncluirLink_QuandoBaseUrlNaoConfiavel_PasswordResetPoisoning()
    {
        var usuario = CriarUsuario("vitima@empresa.com");
        _usuarioRepository.GetByEmailAsync(usuario.Email).Returns(usuario);

        var useCase = CriarUseCase(comEmail: true);
        // Host controlado pelo atacante, fora da allowlist (#765).
        await useCase.ExecuteAsync(new EsqueciSenhaCommand(usuario.Email, "https://evil.com"));

        // E-mail e enviado, mas SEM link para o host do atacante (cai em token puro).
        await _emailService.Received(1).EnviarAsync(
            Arg.Is<MensagemEmail>(m => m.Destinatario == usuario.Email && !m.Corpo.Contains("evil.com")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeveAuditar_QuandoTokenGeradoComSucesso()
    {
        var usuario = CriarUsuario();
        _usuarioRepository.GetByEmailAsync(usuario.Email).Returns(usuario);

        var useCase = CriarUseCase();
        await useCase.ExecuteAsync(new EsqueciSenhaCommand(usuario.Email));

        await _auditLogRepository.Received(1).AddAsync(Arg.Any<AuditLog>());
    }

    [Fact]
    public async Task DeveRetornarSucessoMesmoSeEmailFalhar_QuandoSmtpLancaExcecao()
    {
        var usuario = CriarUsuario();
        _usuarioRepository.GetByEmailAsync(usuario.Email).Returns(usuario);
        _emailService
            .EnviarAsync(Arg.Any<MensagemEmail>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ResultadoEnvio>(new InvalidOperationException("SMTP indisponivel")));

        var useCase = CriarUseCase(comEmail: true);

        // Deve continuar e retornar sucesso mesmo com falha no envio de email
        var result = await useCase.ExecuteAsync(new EsqueciSenhaCommand(usuario.Email));

        result.Success.Should().BeTrue();
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Theory]
    [InlineData(DesfechoEnvio.FalhaPermanente)]
    [InlineData(DesfechoEnvio.FalhaTransitoria)]
    [InlineData(DesfechoEnvio.Simulado)]
    public async Task DeveRetornarSucessoELogarSemEndereco_QuandoOEnvioNaoSaiu(DesfechoEnvio desfecho)
    {
        // N3 (#1351): falha de envio agora volta como desfecho, nao como excecao. O token fica gravado, o pedido
        // responde igual (sem revelar nada) e o log do envio nao leva o endereco do usuario.
        var usuario = CriarUsuario("maria.souza@empresa.com");
        _usuarioRepository.GetByEmailAsync(usuario.Email).Returns(usuario);
        _emailService.EnviarAsync(Arg.Any<MensagemEmail>(), Arg.Any<CancellationToken>()).Returns(desfecho switch
        {
            DesfechoEnvio.FalhaPermanente => new ResultadoEnvio(false, "smtp", "SMTP 550: recusado", FalhaPermanente: true),
            DesfechoEnvio.FalhaTransitoria => new ResultadoEnvio(false, "smtp", "SMTP 421: indisponivel"),
            _ => ResultadoEnvio.Simulado("console"),
        });

        var result = await CriarUseCase(comEmail: true).ExecuteAsync(new EsqueciSenhaCommand(usuario.Email));

        result.Success.Should().BeTrue();
        await _unitOfWork.Received(1).CommitAsync();
        var logsDoEnvio = _logger.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(c => c.GetArguments()[2]?.ToString() ?? string.Empty)
            .Where(t => t.Contains("recuperacao de senha", StringComparison.OrdinalIgnoreCase))
            .ToList();
        logsDoEnvio.Should().NotBeEmpty("o resultado do envio fica registrado");
        logsDoEnvio.Should().NotContain(t => t.Contains(usuario.Email), "endereco e dado pessoal (LGPD)");
    }
}
