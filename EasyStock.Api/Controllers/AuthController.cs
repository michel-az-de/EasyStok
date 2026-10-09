using EasyStock.Application.Ports.Output.Auth;
using EasyStock.Application.UseCases.AlterarSenha;
using Microsoft.AspNetCore.RateLimiting;
using EasyStock.Application.UseCases.AnonimizarMeusDados;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.AtualizarUsuarioAtual;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.ConfirmEmail;
using EasyStock.Application.UseCases.EsqueciSenha;
using EasyStock.Application.UseCases.ExportarMeusDados;
using EasyStock.Application.UseCases.Logout;
using EasyStock.Application.UseCases.ObterUsuarioAtual;
using EasyStock.Application.UseCases.RefreshToken;
using EasyStock.Application.Services.Auth;
using EasyStock.Application.UseCases.ResetarSenha;
using AuditLogEntity = EasyStock.Domain.Entities.AuditLog;
using RefreshTokenEntity = EasyStock.Domain.Entities.RefreshToken;
using Swashbuckle.AspNetCore.Annotations;
using IJwtTokenService = EasyStock.Api.Services.IJwtTokenService;

namespace EasyStock.Api.Controllers;

public sealed record LoginGoogleRequest(string? IdToken, Guid? EmpresaId);

public sealed record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Senha,
    Guid? EmpresaId);

/// <summary>Request para step-1 do login 2-etapas (ADR-0031).</summary>
public sealed record ListarEmpresasParaLoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Senha);
/// <summary>Pedido de redefinição (N8). Só o e-mail: a base do link vem da configuração, nunca do corpo (#765); um <c>baseUrl</c> enviado é ignorado.</summary>
public sealed record EsqueciSenhaRequest([Required] string Email);

/// <summary>Reset pelo link do e-mail (N8). IP e agente saem da conexão, não do corpo.</summary>
public sealed record ResetarSenhaRequest([Required] string Token, [Required] string NovaSenha);

/// <summary>Aceite do convite de primeiro acesso (N9). IP e agente saem da conexão, não do corpo.</summary>
public sealed record AceitarConviteRequest([Required] string Token, [Required] string NovaSenha);

/// <summary>Reset pelo código de 6 dígitos do WhatsApp (N8).</summary>
public sealed record ResetarSenhaPorCodigoRequest([Required] string Email, [Required] string Codigo, [Required] string NovaSenha);

public sealed record LoginUsuarioInfo(Guid id, string nome, string email, string nivel);
public sealed record LoginResponse(string token, string refreshToken, int expiresIn, LoginUsuarioInfo usuario);

[SwaggerTag("Authentication / Autenticação")]
[ApiController]
[Route("api/auth")]
public class AuthController(
    AutenticarUsuarioUseCase autenticarUseCase,
    ListarEmpresasParaLoginUseCase listarEmpresasParaLoginUseCase,
    IJwtTokenService jwtService,
    IRefreshTokenRepository refreshTokenRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    RefreshTokenUseCase refreshTokenUseCase,
    LogoutUseCase logoutUseCase,
    EsqueciSenhaUseCase esqueciSenhaUseCase,
    ResetarSenhaUseCase resetarSenhaUseCase,
    ResetarSenhaPorCodigoUseCase resetarSenhaPorCodigoUseCase,
    ConfirmEmailUseCase confirmEmailUseCase,
    ObterUsuarioAtualUseCase obterUsuarioAtualUseCase,
    AtualizarUsuarioAtualUseCase atualizarUsuarioAtualUseCase,
    AlterarSenhaUseCase alterarSenhaUseCase,
    ExportarMeusDadosUseCase exportarMeusDadosUseCase,
    AnonimizarMeusDadosUseCase anonimizarMeusDadosUseCase) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Authenticate and obtain JWT token", Description = "Validates email+password and returns JWT access token and refresh token. Rate limited by IP.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var resultado = await autenticarUseCase.ExecuteAsync(
            new AutenticarUsuarioCommand(request.Email, request.Senha, request.EmpresaId));

        return await EmitirSessaoAsync(resultado, "login");
    }

    [SwaggerOperation(Summary = "Google sign-in settings", Description = "ClientId público do Google; 404 com o login Google desligado (#1324).")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("google/config")]
    [AllowAnonymous]
    public IActionResult ConfigGoogle([FromServices] IGoogleIdTokenValidator google) =>
        google.ClientId is { } clientId ? DataOk(new { clientId }) : DataNotFound("Login com Google desligado.");

    [SwaggerOperation(Summary = "Sign in with a Google ID token",
        Description = "Só usuário que já existe (e-mail do Google, ou alias do Gmail com uma conta só). Sem empresaId, entra na empresa ativa do usuário (#1324).")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [EnableRateLimiting("auth")]
    [HttpPost("google/login")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginGoogle(
        [FromBody] LoginGoogleRequest request,
        [FromServices] IGoogleIdTokenValidator google,
        [FromServices] IdentificarUsuarioGoogleUseCase identificar,
        [FromServices] IEmpresaPadraoResolver empresaPadraoResolver,
        CancellationToken ct)
    {
        if (google.ClientId is null) return DataNotFound("Login com Google desligado.");
        try
        {
            var usuario = await identificar.ExecuteAsync(request.IdToken ?? string.Empty, ct);
            var empresaPadrao = await empresaPadraoResolver.ResolverAsync(ct);
            var resultado = await autenticarUseCase.ConcluirLoginGoogleAsync(usuario, request.EmpresaId, empresaPadrao);
            return await EmitirSessaoAsync(resultado, "login_google");
        }
        catch (CredenciaisInvalidasException ex)
        {
            return Unauthorized(new { error = new { code = "INVALID_CREDENTIALS", message = ex.Message } });
        }
    }

    /// <summary>Revoga as sessões anteriores, emite JWT + refresh token e audita. Credencial já conferida.</summary>
    private async Task<IActionResult> EmitirSessaoAsync(AutenticarUsuarioResult resultado, string acao)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = HttpContext.Request.Headers.UserAgent.ToString();

        // Revogar sessões anteriores (login em novo dispositivo força logout nos outros).
        // Um único UPDATE em batch — antes era N updates via change tracker.
        var sessoesRevogadas = await refreshTokenRepository.RevogarSessoesAtivasAsync(
            resultado.UsuarioId, DateTime.UtcNow);

        var token = jwtService.GerarToken(resultado);
        var refreshTokenValue = jwtService.GerarRefreshToken();
        var refreshTokenHash = TokenHashHelper.ComputeSha256Hash(refreshTokenValue);
        var expiraEm = DateTime.UtcNow.AddDays(30);

        var refreshToken = RefreshTokenEntity.Criar(
            resultado.UsuarioId,
            refreshTokenHash,
            expiraEm,
            ip,
            userAgent);

        await refreshTokenRepository.AddAsync(refreshToken);

        var auditLog = AuditLogEntity.Criar(
            resultado.UsuarioId,
            acao,
            true,
            "Login realizado com sucesso",
            ip,
            userAgent);
        await auditLogRepository.AddAsync(auditLog);

        if (sessoesRevogadas > 0)
        {
            var auditNovoDispositivo = AuditLogEntity.Criar(
                resultado.UsuarioId,
                "login_novo_dispositivo",
                true,
                $"Login detectado em novo dispositivo. {sessoesRevogadas} sessão(ões) anterior(es) encerrada(s).",
                ip,
                userAgent);
            await auditLogRepository.AddAsync(auditNovoDispositivo);
        }

        await unitOfWork.CommitAsync();

        return DataOk(new LoginResponse(
            token,
            refreshTokenValue,
            jwtService.ExpiresInSeconds,
            new LoginUsuarioInfo(resultado.UsuarioId, resultado.Nome, resultado.Email, resultado.Nivel.ToString())));
    }

    /// <summary>
    /// Step 1 do login 2-etapas (ADR-0031): valida credenciais sem emitir token.
    /// Retorna lista de Empresas acessíveis ao usuário para exibir seletor de empresa.
    /// Se IsSuperAdmin=true, o client prossegue direto para POST /login sem empresa.
    /// Rate-limited igual ao login.
    /// </summary>
    [SwaggerOperation(Summary = "Step-1 login: validate credentials and return empresa list")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [EnableRateLimiting("auth")]
    [HttpPost("lista-empresas")]
    [AllowAnonymous]
    public async Task<IActionResult> ListarEmpresasParaLogin([FromBody] ListarEmpresasParaLoginRequest request)
    {
        try
        {
            var resultado = await listarEmpresasParaLoginUseCase.ExecuteAsync(
                new ListarEmpresasParaLoginCommand(request.Email, request.Senha));
            return DataOk(resultado);
        }
        catch (CredenciaisInvalidasException)
        {
            return Unauthorized(new { error = new { code = "INVALID_CREDENTIALS", message = "E-mail ou senha incorretos." } });
        }
    }

    [SwaggerOperation(Summary = "Refresh JWT token", Description = "Exchange a valid refresh token for a new JWT access token.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [EnableRateLimiting("auth-refresh")]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenCommand command)
        => DataOk(await refreshTokenUseCase.ExecuteAsync(command));

    [SwaggerOperation(Summary = "Invalidate refresh token")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutCommand command)
        => DataOk(await logoutUseCase.ExecuteAsync(command));

    [SwaggerOperation(
        Summary = "Request password reset (link by e-mail, code by WhatsApp when eligible)",
        Description = "Sempre 202 com o mesmo corpo, exista a conta ou nao (N8). Nao faz rede: enfileira um evento ResetSenha no motor. " +
                      "Limites: 5 pedidos por IP em 15 min (429) e, por conta, 3 por hora, 6 por dia e 60 s entre pedidos (202 sem envio).")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [EnableRateLimiting("auth")]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] EsqueciSenhaRequest request)
    {
        try
        {
            var resultado = await esqueciSenhaUseCase.ExecuteAsync(
                new EsqueciSenhaCommand(request.Email, ClientIp(), ClientUserAgent()));
            return StatusCode(StatusCodes.Status202Accepted, new ApiResponse<EsqueciSenhaResult>(resultado, new { }));
        }
        catch (LimitePedidosAcessoExcedidoException ex)
        {
            return TooManyRequests(ex);
        }
    }

    [SwaggerOperation(Summary = "Reset password using token from email")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [EnableRateLimiting("auth")]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetarSenhaRequest request)
    {
        try
        {
            return DataOk(await resetarSenhaUseCase.ExecuteAsync(
                new ResetarSenhaCommand(request.Token, request.NovaSenha, ClientIp(), ClientUserAgent())));
        }
        catch (LimitePedidosAcessoExcedidoException ex)
        {
            return TooManyRequests(ex);
        }
    }

    [SwaggerOperation(
        Summary = "Reset password using the 6-digit code sent by WhatsApp",
        Description = "Codigo de 10 min, 5 tentativas, uso unico. Superadmin nunca redefine por codigo (mesma recusa generica).")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [EnableRateLimiting("auth")]
    [HttpPost("reset-password-code")]
    public async Task<IActionResult> ResetPasswordCode([FromBody] ResetarSenhaPorCodigoRequest request)
    {
        try
        {
            return DataOk(await resetarSenhaPorCodigoUseCase.ExecuteAsync(
                new ResetarSenhaPorCodigoCommand(request.Email, request.Codigo, request.NovaSenha, ClientIp(), ClientUserAgent())));
        }
        catch (LimitePedidosAcessoExcedidoException ex)
        {
            return TooManyRequests(ex);
        }
    }

    [SwaggerOperation(
        Summary = "Accept an access invite (set the password and verify the channel)",
        Description = "Anonimo (N9). Quem consome e este POST: abrir o link nunca consome. Define a senha pela politica existente, " +
                      "verifica o canal do token (e-mail ou WhatsApp) e revoga os outros convites. Revogado, usado, vencido ou de " +
                      "superadmin: sempre a mesma mensagem. Nao emite sessao: a pessoa volta ao login. Limite por IP da N8 (429).")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [EnableRateLimiting("auth")]
    [HttpPost("aceitar-convite")]
    public async Task<IActionResult> AceitarConvite(
        [FromBody] AceitarConviteRequest request, [FromServices] EasyStock.Application.UseCases.AceitarConvite.AceitarConviteUseCase aceitarConvite)
    {
        try
        {
            return DataOk(await aceitarConvite.ExecuteAsync(
                new EasyStock.Application.UseCases.AceitarConvite.AceitarConviteCommand(
                    request.Token, request.NovaSenha, ClientIp(), ClientUserAgent())));
        }
        catch (LimitePedidosAcessoExcedidoException ex)
        {
            return TooManyRequests(ex);
        }
    }

    // IP e agente vem da conexao, nunca do corpo. Fora de uma requisicao HTTP (testes) sao nulos.
    private string? ClientIp() => HttpContext?.Connection.RemoteIpAddress?.ToString();

    private string? ClientUserAgent()
    {
        var agente = HttpContext?.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(agente) ? null : agente;
    }

    private IActionResult TooManyRequests(LimitePedidosAcessoExcedidoException ex)
    {
        Response.Headers.Append("Retry-After", ex.RetryAfterSeconds.ToString());
        return StatusCode(StatusCodes.Status429TooManyRequests, new ApiErrorResponse(new ApiError(
            "TOO_MANY_REQUESTS", ex.Message, null, null)));
    }

    [Authorize]
    [SwaggerOperation(Summary = "Get current authenticated user profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
        => DataOk(await obterUsuarioAtualUseCase.ExecuteAsync(new ObterUsuarioAtualCommand()));

    [Authorize]
    [HttpGet("me/modulos")]
    public IActionResult GetModulos([FromServices] ICurrentUserAccessor usuario)
    {
        var modulos = Enum.GetValues<Modulo>().Select(m => new
        {
            id = Domain.Services.AcessoModulos.IdDe(m),
            liberado = usuario.EmpresaId != Guid.Empty && usuario.TemPermissao(Domain.Services.AcessoModulos.PermissaoDe(m))
        }).ToArray();
        var inicial = User.FindFirst("moduloInicial")?.Value;
        var gerente = usuario.Nivel is NivelAcesso.SuperAdmin or NivelAcesso.Admin or NivelAcesso.Gerente;
        return DataOk(new
        {
            modulos,
            portaDeEntrada = modulos.Any(m => m.id == inicial && m.liberado) ? inicial : null,
            acoes = new
            {
                editarCardapio = gerente && modulos.Any(m => m.id == "cardapio" && m.liberado),
                editarProducao = gerente && modulos.Any(m => m.id == "producao" && m.liberado),
                gerenciarCaixa = gerente && modulos.Any(m => m.id == "financeiro" && m.liberado)
            }
        });
    }

    [Authorize]
    [EnableRateLimiting("auth")]
    [SwaggerOperation(
        Summary = "Update current user profile",
        Description = "Trocar o e-mail exige senhaAtual (ausente ou errada: 403, e a errada conta como falha de login) e so grava o endereco como pendente: o e-mail da conta troca no clique do link enviado ao endereco novo.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPatch("me")]
    public async Task<IActionResult> UpdateMe([FromBody] AtualizarUsuarioAtualCommand command)
        => DataOk(await atualizarUsuarioAtualUseCase.ExecuteAsync(command));

    [Authorize]
    [SwaggerOperation(Summary = "Change current user password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPatch("me/password")]
    public async Task<IActionResult> ChangePassword([FromBody] AlterarSenhaCommand command)
        => DataOk(await alterarSenhaUseCase.ExecuteAsync(command));

    [Authorize]
    [SwaggerOperation(
        Summary = "LGPD Art.18 — exportar dados pessoais do usuario",
        Description = "Devolve um snapshot estruturado dos dados pessoais do usuario autenticado: perfil, empresas vinculadas e refresh tokens ativos.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet("me/export")]
    public async Task<IActionResult> ExportMyData()
        => DataOk(await exportarMeusDadosUseCase.ExecuteAsync());

    [Authorize]
    [SwaggerOperation(
        Summary = "LGPD Art.18 — anonimizar (direito ao esquecimento)",
        Description = "Pseudonimiza campos PII do usuario autenticado, zera senha, desativa conta e remove credenciais (refresh/reset/confirm-email tokens). AuditLogs e movimentacoes historicas sao preservadas com UsuarioId. Operacao irreversivel — exige body { confirmacaoTexto: 'ANONIMIZAR' }.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("me/anonimizar")]
    public async Task<IActionResult> AnonimizarMeusDados([FromBody] AnonimizarMeusDadosCommand command)
        => DataOk(await anonimizarMeusDadosUseCase.ExecuteAsync(command));

    [SwaggerOperation(Summary = "Confirm email address", Description = "Validates and marks an email address as confirmed using a token. Allows login access.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [AllowAnonymous]
    [HttpPost("confirmar-email")]
    public async Task<IActionResult> ConfirmarEmail([FromQuery] string token)
    {
        try
        {
            var command = new ConfirmEmailCommand(token);
            var result = await confirmEmailUseCase.ExecuteAsync(command);
            return Ok(result);
        }
        catch (Domain.Exceptions.RegraDeDominioVioladaException ex)
        {
            return BadRequest(new { erro = ex.Message });
        }
    }
}
