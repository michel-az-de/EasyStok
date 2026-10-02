using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.UseCases.AlterarSenha;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.AtualizarUsuarioAtual;
using EasyStock.Application.UseCases.ConfirmEmail;
using EasyStock.Application.UseCases.EsqueciSenha;
using EasyStock.Application.UseCases.Logout;
using EasyStock.Application.UseCases.ObterUsuarioAtual;
using EasyStock.Application.UseCases.RefreshToken;
using EasyStock.Application.UseCases.ResetarSenha;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public class AuthControllerTests
{
    private readonly IUsuarioRepository _usuarioRepository = Substitute.For<IUsuarioRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IAuditLogRepository _auditLogRepository = Substitute.For<IAuditLogRepository>();
    private readonly ILogger<AutenticarUsuarioUseCase> _autenticarLogger = Substitute.For<ILogger<AutenticarUsuarioUseCase>>();
    private readonly EasyStock.Api.Services.IJwtTokenService _mockJwtService = Substitute.For<EasyStock.Api.Services.IJwtTokenService>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly AutenticarUsuarioUseCase _autenticarUseCase;
    private readonly AuthController _controller;

    // N8: o esqueci a senha e o reset usam estes para o teste ver o que o controller monta e o que vai ao motor.
    private readonly IUsuarioRepository _acessoUsuarios = Substitute.For<IUsuarioRepository>();
    private readonly IResetTokenRepository _acessoTokens = Substitute.For<IResetTokenRepository>();
    private readonly EasyStock.Application.Ports.Output.Notifications.INotificadorService _notificador =
        Substitute.For<EasyStock.Application.Ports.Output.Notifications.INotificadorService>();
    private readonly ICacheService _cacheDoLimite = Substitute.For<ICacheService>();
    private readonly List<string> _payloadsEnfileirados = [];

    public AuthControllerTests()
    {
        var passwordHasher = _passwordHasher;
        _autenticarUseCase = new AutenticarUsuarioUseCase(_usuarioRepository, _unitOfWork, passwordHasher, _autenticarLogger);

        _mockJwtService.GerarToken(Arg.Any<AutenticarUsuarioResult>()).Returns("mocked-jwt-token");
        _mockJwtService.GerarRefreshToken().Returns("mocked-refresh-token");
        _mockJwtService.ExpiresInSeconds.Returns(3600);

        _unitOfWork.CommitAsync().Returns(1);

        var usuarioRepo2 = Substitute.For<IUsuarioRepository>();
        var unitOfWork2 = Substitute.For<IUnitOfWork>();
        var refreshTokenRepo2 = Substitute.For<IRefreshTokenRepository>();
        var auditLogRepo2 = Substitute.For<IAuditLogRepository>();
        var resetTokenRepo = Substitute.For<IResetTokenRepository>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();

        var refreshTokenLogger = Substitute.For<ILogger<RefreshTokenUseCase>>();
        var logoutLogger = Substitute.For<ILogger<LogoutUseCase>>();
        var esqueciSenhaLogger = Substitute.For<ILogger<EsqueciSenhaUseCase>>();
        var resetarSenhaLogger = Substitute.For<ILogger<ResetarSenhaUseCase>>();
        var obterUsuarioAtualLogger = Substitute.For<ILogger<ObterUsuarioAtualUseCase>>();
        var atualizarUsuarioAtualLogger = Substitute.For<ILogger<AtualizarUsuarioAtualUseCase>>();
        var alterarSenhaLogger = Substitute.For<ILogger<AlterarSenhaUseCase>>();
        var jwtServiceApp = Substitute.For<EasyStock.Application.Ports.Output.IJwtTokenService>();

        var emailTokenRepo = Substitute.For<IEmailConfirmationTokenRepository>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:LinkRedefinirSenha"] = "https://app.easystok.com.br/auth/redefinir-senha?token={0}",
            })
            .Build();
        var refreshTokenUseCase = new RefreshTokenUseCase(refreshTokenRepo2, usuarioRepo2, auditLogRepo2, jwtServiceApp, unitOfWork2, refreshTokenLogger);
        var logoutUseCase = new LogoutUseCase(refreshTokenRepo2, auditLogRepo2, unitOfWork2, logoutLogger);
        var revogadorSessoes = new RevogadorSessoes(
            usuarioRepo2, refreshTokenRepo2, Substitute.For<ICacheService>(), TimeProvider.System, Substitute.For<ILogger<RevogadorSessoes>>());

        // N8: pedido e reset de acesso sobre os substitutos de campo (o teste le o que foi gravado e enfileirado).
        var empresaDoEvento = new EmpresaDoEventoAnonimo(
            Substitute.For<IEmpresaPadraoResolver>(), Substitute.For<ITenantContextAccessor>(),
            Substitute.For<ILogger<EmpresaDoEventoAnonimo>>());
        _cacheDoLimite.IncrementAsync(Arg.Any<string>(), Arg.Any<long>()).Returns(1L);
        _acessoTokens.ContarPedidosAsync(Arg.Any<Guid>(), Arg.Any<DateTime>()).Returns(new ContagemPedidosReset(0, 0, null));
        _notificador.EnfileirarEventoAsync(
                Arg.Any<EasyStock.Domain.Enums.Notifications.TipoEventoNotificacao>(), Arg.Any<Guid>(), Arg.Any<string>(),
                Arg.Any<Guid?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(call => { _payloadsEnfileirados.Add(call.ArgAt<string>(2)); return Task.FromResult(Guid.NewGuid()); });
        var limite = new LimitePedidosAcesso(_cacheDoLimite);
        var esqueciSenhaUseCase = new EsqueciSenhaUseCase(
            _acessoUsuarios, _acessoTokens, auditLogRepo2,
            Substitute.For<EasyStock.Application.Ports.Output.Notifications.IConsentimentoRepository>(),
            _notificador, empresaDoEvento, limite, unitOfWork2, config, TimeProvider.System, esqueciSenhaLogger);
        var concluidor = new ConcluidorDeReset(
            usuarioRepo2, _acessoTokens, auditLogRepo2, revogadorSessoes, _notificador, empresaDoEvento, passwordHasher,
            TimeProvider.System, Substitute.For<ILogger<ConcluidorDeReset>>());
        var resetarSenhaUseCase = new ResetarSenhaUseCase(
            _acessoTokens, usuarioRepo2, concluidor, limite, unitOfWork2, TimeProvider.System, resetarSenhaLogger);
        var resetarSenhaPorCodigoUseCase = new ResetarSenhaPorCodigoUseCase(
            _acessoTokens, usuarioRepo2, concluidor, limite, unitOfWork2, TimeProvider.System,
            Substitute.For<ILogger<ResetarSenhaPorCodigoUseCase>>());
        var obterUsuarioAtualUseCase = new ObterUsuarioAtualUseCase(usuarioRepo2, currentUser, obterUsuarioAtualLogger);
        var trocaDeContato = new TrocaDeContatoService(
            usuarioRepo2, emailTokenRepo, Substitute.For<EasyStock.Application.Ports.Output.Notifications.INotificadorService>(),
            Substitute.For<IEmpresaPadraoResolver>(), Substitute.For<ITenantContextAccessor>(), currentUser, passwordHasher,
            unitOfWork2, config, TimeProvider.System, Substitute.For<ILogger<TrocaDeContatoService>>());
        var atualizarUsuarioAtualUseCase = new AtualizarUsuarioAtualUseCase(usuarioRepo2, currentUser, unitOfWork2, trocaDeContato, atualizarUsuarioAtualLogger);
        var alterarSenhaUseCase = new AlterarSenhaUseCase(usuarioRepo2, currentUser, revogadorSessoes, unitOfWork2, passwordHasher, alterarSenhaLogger);
        var confirmEmailLogger = Substitute.For<ILogger<ConfirmEmailUseCase>>();
        var confirmEmailUseCase = new ConfirmEmailUseCase(emailTokenRepo, usuarioRepo2, auditLogRepo2, revogadorSessoes, unitOfWork2, confirmEmailLogger);

        var exportarLogger = Substitute.For<ILogger<EasyStock.Application.UseCases.ExportarMeusDados.ExportarMeusDadosUseCase>>();
        var anonimizarLogger = Substitute.For<ILogger<EasyStock.Application.UseCases.AnonimizarMeusDados.AnonimizarMeusDadosUseCase>>();
        var usuarioEmpresaRepo = Substitute.For<IUsuarioEmpresaRepository>();
        var exportarUseCase = new EasyStock.Application.UseCases.ExportarMeusDados.ExportarMeusDadosUseCase(usuarioRepo2, usuarioEmpresaRepo, refreshTokenRepo2, Substitute.For<EasyStock.Application.Ports.Output.Notifications.IConsentimentoRepository>(), Substitute.For<EasyStock.Application.Ports.Output.Notifications.IPreferenciaNotificacaoRepository>(), currentUser, exportarLogger);
        var anonimizarUseCase = new EasyStock.Application.UseCases.AnonimizarMeusDados.AnonimizarMeusDadosUseCase(usuarioRepo2, refreshTokenRepo2, resetTokenRepo, emailTokenRepo, Substitute.For<EasyStock.Application.Ports.Output.Notifications.IConsentimentoRepository>(), Substitute.For<EasyStock.Application.Ports.Output.Notifications.IPreferenciaNotificacaoRepository>(), currentUser, unitOfWork2, anonimizarLogger);

        var listarEmpresasLogger = Substitute.For<ILogger<ListarEmpresasParaLoginUseCase>>();
        var listarEmpresasUseCase = new ListarEmpresasParaLoginUseCase(
            Substitute.For<IUsuarioRepository>(),
            unitOfWork2,
            Substitute.For<IPasswordHasher>(),
            listarEmpresasLogger);

        _controller = new AuthController(
            _autenticarUseCase,
            listarEmpresasUseCase,
            _mockJwtService,
            _refreshTokenRepository,
            _auditLogRepository,
            _unitOfWork,
            refreshTokenUseCase,
            logoutUseCase,
            esqueciSenhaUseCase,
            resetarSenhaUseCase,
            resetarSenhaPorCodigoUseCase,
            confirmEmailUseCase,
            obterUsuarioAtualUseCase,
            atualizarUsuarioAtualUseCase,
            alterarSenhaUseCase,
            exportarUseCase,
            anonimizarUseCase);
    }

    [Fact]
    public async Task Login_DevePropagar_CredenciaisInvalidasException_QuandoLoginFalha()
    {
        // Arrange
        _usuarioRepository.GetByEmailAsync(Arg.Any<string>()).Returns((Usuario?)null);

        var request = new LoginRequest("invalido@teste.com", "senhaErrada", null);

        // Act
        Func<Task> act = async () => await _controller.Login(request);

        // Assert
        await act.Should().ThrowAsync<CredenciaisInvalidasException>();
    }

    [Fact]
    public async Task Login_RevogaORefreshDosOutrosAparelhosMasNaoGravaOCarimboDeSessao()
    {
        // #1352: logar em outro aparelho revoga só o refresh dos outros; o JWT deles segue valendo, então o
        // carimbo de sessão não pode mudar no login (logar o tablet derrubaria o balcão).
        var usuario = Usuario.Criar("Ana", "ana@casadababa.com", "hash");
        _usuarioRepository.GetByEmailAsync(usuario.Email).Returns(usuario);
        _passwordHasher.Verify("Senha@12345", "hash").Returns(true);
        _controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };

        await _controller.Login(new LoginRequest(usuario.Email, "Senha@12345", null));

        await _refreshTokenRepository.Received(1).RevogarSessoesAtivasAsync(usuario.Id, Arg.Any<DateTime>());
        await _usuarioRepository.DidNotReceiveWithAnyArgs().AtualizarSessoesValidasDesdeAsync(default, default);
        usuario.SessoesValidasDesde.Should().BeNull();
    }

    // ── N8: esqueci a senha ───────────────────────────────────────────────────────────────────

    private void UsarContextoHttp(string ip = "203.0.113.7")
    {
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
        http.Request.Headers.UserAgent = "TesteNavegador/1.0";
        _controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = http };
    }

    private Usuario UsuarioDoAcesso(string email = "ana@casadababa.com")
    {
        var usuario = Usuario.Criar("Ana", email, "hash");
        usuario.Empresas.Add(new UsuarioEmpresa { UsuarioId = usuario.Id, EmpresaId = Guid.NewGuid(), Ativo = true });
        _acessoUsuarios.GetByEmailAsync(usuario.Email).Returns(usuario);
        return usuario;
    }

    [Fact]
    public async Task ForgotPasswordDevolve202ComMesmoCorpo()
    {
        UsarContextoHttp();
        var existente = UsuarioDoAcesso();
        _acessoUsuarios.GetByEmailAsync("ninguem@casadababa.com").Returns((Usuario?)null);

        var comConta = await _controller.ForgotPassword(new EsqueciSenhaRequest(existente.Email)) as Microsoft.AspNetCore.Mvc.ObjectResult;
        var semConta = await _controller.ForgotPassword(new EsqueciSenhaRequest("ninguem@casadababa.com")) as Microsoft.AspNetCore.Mvc.ObjectResult;

        comConta!.StatusCode.Should().Be(202);
        semConta!.StatusCode.Should().Be(202);
        System.Text.Json.JsonSerializer.Serialize(comConta.Value).Should().Be(System.Text.Json.JsonSerializer.Serialize(semConta.Value));
    }

    [Fact]
    public async Task ForgotPasswordIgnoraBaseUrlDoCorpo()
    {
        UsarContextoHttp();
        var usuario = UsuarioDoAcesso();
        var corpo = System.Text.Json.JsonSerializer.Deserialize<EsqueciSenhaRequest>(
            "{\"email\":\"" + usuario.Email + "\",\"baseUrl\":\"https://evil.example\"}",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

        await _controller.ForgotPassword(corpo);

        _payloadsEnfileirados.Should().ContainSingle().Which.Should()
            .Contain("https://app.easystok.com.br/auth/redefinir-senha?token=").And.NotContain("evil.example");
    }

    [Fact]
    public async Task ForgotPasswordGravaOIpEOAgenteDaConexao()
    {
        UsarContextoHttp(ip: "198.51.100.77");
        var usuario = UsuarioDoAcesso();

        await _controller.ForgotPassword(new EsqueciSenhaRequest(usuario.Email));

        await _acessoTokens.Received().AddAsync(Arg.Is<ResetToken>(t => t.IpCriacao == "198.51.100.77" && t.UserAgent == "TesteNavegador/1.0"));
    }

    [Fact]
    public async Task ResetPasswordCodeMapeiaLimiteParaQuatrocentosEVinteENove()
    {
        UsarContextoHttp();
        _cacheDoLimite.IncrementAsync(Arg.Any<string>(), Arg.Any<long>()).Returns(6L);

        var resposta = await _controller.ResetPasswordCode(new ResetarSenhaPorCodigoRequest("ana@casadababa.com", "123456", "Nova@Senha123"))
            as Microsoft.AspNetCore.Mvc.ObjectResult;

        resposta!.StatusCode.Should().Be(429);
        _controller.Response.Headers["Retry-After"].ToString().Should().Be("900");
    }

    [Fact]
    public async Task ForgotPasswordMapeiaLimiteDeIpParaQuatrocentosEVinteENove()
    {
        UsarContextoHttp();
        _cacheDoLimite.IncrementAsync(Arg.Any<string>(), Arg.Any<long>()).Returns(6L);

        var resposta = await _controller.ForgotPassword(new EsqueciSenhaRequest("ana@casadababa.com")) as Microsoft.AspNetCore.Mvc.ObjectResult;

        resposta!.StatusCode.Should().Be(429);
    }
}
