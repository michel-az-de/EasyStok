using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Admin.CriarTenantPorAdmin;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// N3 (#1351): a senha temporária da conta criada pelo admin sai pela caixa de segurança, e o resultado só diz que o
/// e-mail foi enviado quando ele saiu de verdade (nem o simulado do console conta como enviado).
/// </summary>
public class CriarTenantPorAdminUseCaseTests
{
    private readonly IUsuarioRepository _usuarioRepository = Substitute.For<IUsuarioRepository>();
    private readonly IPlanoRepository _planoRepository = Substitute.For<IPlanoRepository>();
    private readonly IPerfilRepository _perfilRepository = Substitute.For<IPerfilRepository>();
    private readonly IAssinaturaEmpresaRepository _assinaturaRepository = Substitute.For<IAssinaturaEmpresaRepository>();
    private readonly IEmpresaRepository _empresaRepository = Substitute.For<IEmpresaRepository>();
    private readonly IUsuarioEmpresaRepository _usuarioEmpresaRepository = Substitute.For<IUsuarioEmpresaRepository>();
    private readonly IUsuarioPerfilRepository _usuarioPerfilRepository = Substitute.For<IUsuarioPerfilRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IEmailService _emailService = Substitute.For<IEmailService>();
    private readonly ILogger<CriarTenantPorAdminUseCase> _logger = Substitute.For<ILogger<CriarTenantPorAdminUseCase>>();

    public CriarTenantPorAdminUseCaseTests()
    {
        _planoRepository.GetAtivosAsync().Returns(new List<Plano> { new() { Id = Guid.NewGuid(), Nome = "Starter", Ativo = true } });
        _perfilRepository.GetPadroesAsync().Returns(new List<Perfil> { new() { Id = Guid.NewGuid(), Nome = "Admin", Nivel = NivelAcesso.Admin } });
        _passwordHasher.Hash(Arg.Any<string>()).Returns("hash-de-teste");
        // Sem isto o mock devolveria null para Task<ResultadoEnvio> (o record e selado e nao e substituivel).
        _emailService.EnviarAsync(Arg.Any<MensagemEmail>(), Arg.Any<CancellationToken>())
            .Returns(new ResultadoEnvio(true, "smtp"));
    }

    private CriarTenantPorAdminUseCase CriarUseCase() =>
        new(_usuarioRepository, _planoRepository, _perfilRepository, _assinaturaRepository, _empresaRepository,
            _usuarioEmpresaRepository, _usuarioPerfilRepository, _passwordHasher, _unitOfWork, _emailService, _logger);

    private static CriarTenantPorAdminCommand Comando(bool enviarEmail = true) =>
        new("Padaria da Maria", null, "Maria Souza", "maria.souza@padaria.com", enviarEmail);

    [Fact]
    public async Task EnviaPeloRemetenteDeSeguranca()
    {
        var resultado = await CriarUseCase().ExecuteAsync(Comando());

        resultado.EmailEnviado.Should().BeTrue();
        resultado.EmailErro.Should().BeNull();
        await _emailService.Received(1).EnviarAsync(
            Arg.Is<MensagemEmail>(m => m.Remetente == RemetenteEmail.Seguranca
                && m.Html
                && m.Destinatario == "maria.souza@padaria.com"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(DesfechoEnvio.FalhaPermanente)]
    [InlineData(DesfechoEnvio.FalhaTransitoria)]
    [InlineData(DesfechoEnvio.Simulado)]
    public async Task EmailEnviadoSoFicaVerdadeiroQuandoOEmailSaiuDeVerdade(DesfechoEnvio desfecho)
    {
        _emailService.EnviarAsync(Arg.Any<MensagemEmail>(), Arg.Any<CancellationToken>()).Returns(desfecho switch
        {
            DesfechoEnvio.FalhaPermanente => new ResultadoEnvio(false, "smtp", "SMTP 550: recusado", FalhaPermanente: true),
            DesfechoEnvio.FalhaTransitoria => new ResultadoEnvio(false, "smtp", "SMTP 421: indisponivel"),
            _ => ResultadoEnvio.Simulado("console"),
        });

        var resultado = await CriarUseCase().ExecuteAsync(Comando());

        resultado.EmailEnviado.Should().BeFalse("o operador dita a senha por telefone quando o e-mail nao saiu");
        resultado.EmailErro.Should().NotBeNullOrWhiteSpace();
        resultado.EmailErro.Should().NotContain("maria.souza@padaria.com");
        resultado.SenhaTemporaria.Should().NotBeNullOrEmpty("a senha segue sendo mostrada ao operador uma vez");
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task NaoEnviaQuandoOOperadorNaoPediu()
    {
        var resultado = await CriarUseCase().ExecuteAsync(Comando(enviarEmail: false));

        resultado.EmailEnviado.Should().BeFalse();
        await _emailService.DidNotReceive().EnviarAsync(Arg.Any<MensagemEmail>(), Arg.Any<CancellationToken>());
    }
}
