using EasyStock.Api.Http;
using EasyStock.Api.Services;
using EasyStock.Application.UseCases.AlterarSenha;
using Microsoft.AspNetCore.RateLimiting;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.AtualizarUsuarioAtual;
using EasyStock.Application.UseCases.CadastrarUsuario;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.ConfirmEmail;
using EasyStock.Application.UseCases.EsqueciSenha;
using EasyStock.Application.UseCases.Logout;
using EasyStock.Application.UseCases.ObterUsuarioAtual;
using EasyStock.Application.UseCases.RefreshToken;
using EasyStock.Application.UseCases.ResetarSenha;
using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.AspNetCore.Mvc;
using AuditLogEntity = EasyStock.Domain.Entities.AuditLog;
using RefreshTokenEntity = EasyStock.Domain.Entities.RefreshToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

public sealed record LoginRequest(string Email, string Senha, Guid? EmpresaId);
public sealed record LoginUsuarioInfo(Guid id, string nome, string email, string nivel);
public sealed record LoginResponse(string token, string refreshToken, int expiresIn, LoginUsuarioInfo usuario);

[SwaggerTag("Authentication / Autenticação")]
[ApiController]
[Route("api/auth")]
public class AuthController(
    AutenticarUsuarioUseCase autenticarUseCase,
    IJwtTokenService jwtService,
    IRefreshTokenRepository refreshTokenRepository,
    IAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork,
    CadastrarUsuarioUseCase cadastrarUsuarioUseCase,
    RefreshTokenUseCase refreshTokenUseCase,
    LogoutUseCase logoutUseCase,
    EsqueciSenhaUseCase esqueciSenhaUseCase,
    ResetarSenhaUseCase resetarSenhaUseCase,
    ConfirmEmailUseCase confirmEmailUseCase,
    ObterUsuarioAtualUseCase obterUsuarioAtualUseCase,
    AtualizarUsuarioAtualUseCase atualizarUsuarioAtualUseCase,
    AlterarSenhaUseCase alterarSenhaUseCase) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Authenticate and obtain JWT token", Description = "Validates email+password and returns JWT access token and refresh token. Rate limited by IP.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var resultado = await autenticarUseCase.ExecuteAsync(
            new AutenticarUsuarioCommand(request.Email, request.Senha, request.EmpresaId));

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = HttpContext.Request.Headers.UserAgent.ToString();

        // Revogar sessões anteriores (login em novo dispositivo força logout nos outros)
        var sessoesAnteriores = await refreshTokenRepository.GetByUsuarioIdAsync(resultado.UsuarioId);
        var sessoesAtivas = sessoesAnteriores.Where(t => t.EstaValido()).ToList();
        foreach (var sessaoAtiva in sessoesAtivas)
        {
            sessaoAtiva.Revogar();
            await refreshTokenRepository.UpdateAsync(sessaoAtiva);
        }

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
            "login",
            true,
            "Login realizado com sucesso",
            ip,
            userAgent);
        await auditLogRepository.AddAsync(auditLog);

        if (sessoesAtivas.Count > 0)
        {
            var auditNovoDispositivo = AuditLogEntity.Criar(
                resultado.UsuarioId,
                "login_novo_dispositivo",
                true,
                $"Login detectado em novo dispositivo. {sessoesAtivas.Count} sessão(ões) anterior(es) encerrada(s).",
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

    [SwaggerOperation(Summary = "Register new user account")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] CadastrarUsuarioCommand command)
        => DataOk(await cadastrarUsuarioUseCase.ExecuteAsync(command));

    [SwaggerOperation(Summary = "Refresh JWT token", Description = "Exchange a valid refresh token for a new JWT access token.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [EnableRateLimiting("auth")]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenCommand command)
        => DataOk(await refreshTokenUseCase.ExecuteAsync(command));

    [SwaggerOperation(Summary = "Invalidate refresh token")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutCommand command)
        => DataOk(await logoutUseCase.ExecuteAsync(command));

    [SwaggerOperation(Summary = "Request password reset email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [EnableRateLimiting("auth")]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] EsqueciSenhaCommand command)
        => DataOk(await esqueciSenhaUseCase.ExecuteAsync(command));

    [SwaggerOperation(Summary = "Reset password using token from email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [EnableRateLimiting("auth")]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetarSenhaCommand command)
        => DataOk(await resetarSenhaUseCase.ExecuteAsync(command));

    [Authorize]
    [SwaggerOperation(Summary = "Get current authenticated user profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
        => DataOk(await obterUsuarioAtualUseCase.ExecuteAsync(new ObterUsuarioAtualCommand()));

    [Authorize]
    [SwaggerOperation(Summary = "Update current user profile")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
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
