using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.CadastrarUsuario;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// N3 (#1351): o e-mail de confirmação de cadastro carrega um token e sai pela caixa de segurança, não pela de avisos.
/// </summary>
public class CadastrarUsuarioUseCaseTests
{
    private const string HostConfiavel = "https://app.easystock.com";

    private readonly IUsuarioRepository _usuarioRepository = Substitute.For<IUsuarioRepository>();
    private readonly IAuditLogRepository _auditLogRepository = Substitute.For<IAuditLogRepository>();
    private readonly IEmailConfirmationTokenRepository _emailTokenRepository = Substitute.For<IEmailConfirmationTokenRepository>();
    private readonly IEmailService _emailService = Substitute.For<IEmailService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly ILogger<CadastrarUsuarioUseCase> _logger = Substitute.For<ILogger<CadastrarUsuarioUseCase>>();
    private readonly IConfiguration _config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:TrustedLinkOrigins:0"] = HostConfiavel })
        .Build();

    public CadastrarUsuarioUseCaseTests()
    {
        _passwordHasher.Hash(Arg.Any<string>()).Returns("hash-de-teste");
        // Sem isto o mock devolveria null para Task<ResultadoEnvio> (o record e selado e nao e substituivel).
        _emailService.EnviarAsync(Arg.Any<MensagemEmail>(), Arg.Any<CancellationToken>())
            .Returns(new ResultadoEnvio(true, "smtp"));
    }

    private CadastrarUsuarioUseCase CriarUseCase() =>
        new(_usuarioRepository, _auditLogRepository, _emailTokenRepository, _emailService, _unitOfWork,
            _passwordHasher, _config, _logger);

    [Fact]
    public async Task EnviaPeloRemetenteDeSeguranca()
    {
        var comando = new CadastrarUsuarioCommand("Maria Souza", "maria.souza@empresa.com", "Senha@123", HostConfiavel);

        var resultado = await CriarUseCase().ExecuteAsync(comando);

        resultado.UsuarioId.Should().NotBeEmpty();
        await _emailService.Received(1).EnviarAsync(
            Arg.Is<MensagemEmail>(m => m.Remetente == RemetenteEmail.Seguranca
                && m.Html
                && m.Destinatario == "maria.souza@empresa.com"
                && m.Corpo.Contains($"{HostConfiavel}/auth/confirmar-email?token=")),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(DesfechoEnvio.FalhaPermanente)]
    [InlineData(DesfechoEnvio.FalhaTransitoria)]
    [InlineData(DesfechoEnvio.Simulado)]
    public async Task OEnvioQueNaoSaiuNaoImpedeOCadastroENaoLogaOEndereco(DesfechoEnvio desfecho)
    {
        _emailService.EnviarAsync(Arg.Any<MensagemEmail>(), Arg.Any<CancellationToken>()).Returns(desfecho switch
        {
            DesfechoEnvio.FalhaPermanente => new ResultadoEnvio(false, "smtp", "SMTP 550: recusado", FalhaPermanente: true),
            DesfechoEnvio.FalhaTransitoria => new ResultadoEnvio(false, "smtp", "SMTP 421: indisponivel"),
            _ => ResultadoEnvio.Simulado("console"),
        });
        var comando = new CadastrarUsuarioCommand("Maria Souza", "maria.souza@empresa.com", "Senha@123", HostConfiavel);

        var resultado = await CriarUseCase().ExecuteAsync(comando);

        resultado.UsuarioId.Should().NotBeEmpty();
        await _unitOfWork.Received(1).CommitAsync();
        var logsDoEnvio = _logger.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(c => c.GetArguments()[2]?.ToString() ?? string.Empty)
            .Where(t => t.Contains("confirma", StringComparison.OrdinalIgnoreCase)
                && !t.Contains("Iniciando", StringComparison.OrdinalIgnoreCase))
            .ToList();
        logsDoEnvio.Should().NotBeEmpty("o resultado do envio fica registrado");
        logsDoEnvio.Should().NotContain(t => t.Contains("maria.souza@empresa.com"), "endereco e dado pessoal (LGPD)");
    }

    [Fact]
    public async Task SemBaseUrlConfiavelNaoEnviaNada()
    {
        var comando = new CadastrarUsuarioCommand("Maria Souza", "maria.souza@empresa.com", "Senha@123", "https://evil.com");

        await CriarUseCase().ExecuteAsync(comando);

        await _emailService.DidNotReceive().EnviarAsync(Arg.Any<MensagemEmail>(), Arg.Any<CancellationToken>());
    }
}
