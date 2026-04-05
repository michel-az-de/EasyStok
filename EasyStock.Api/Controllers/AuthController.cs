using EasyStock.Api.Http;
using EasyStock.Api.Services;
using EasyStock.Application.UseCases.AlterarSenha;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.AtualizarUsuarioAtual;
using EasyStock.Application.UseCases.CadastrarUsuario;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.EsqueciSenha;
using EasyStock.Application.UseCases.Logout;
using EasyStock.Application.UseCases.ObterUsuarioAtual;
using EasyStock.Application.UseCases.RefreshToken;
using EasyStock.Application.UseCases.ResetarSenha;
using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.AspNetCore.Mvc;
using AuditLogEntity = EasyStock.Domain.Entities.AuditLog;
using RefreshTokenEntity = EasyStock.Domain.Entities.RefreshToken;

namespace EasyStock.Api.Controllers;

public sealed record LoginRequest(string Email, string Senha, Guid? EmpresaId);
public sealed record LoginUsuarioInfo(Guid id, string nome, string email, string nivel);
public sealed record LoginResponse(string token, string refreshToken, int expiresIn, LoginUsuarioInfo usuario);

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
    ObterUsuarioAtualUseCase obterUsuarioAtualUseCase,
    AtualizarUsuarioAtualUseCase atualizarUsuarioAtualUseCase,
    AlterarSenhaUseCase alterarSenhaUseCase) : EasyStockControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var resultado = await autenticarUseCase.ExecuteAsync(
            new AutenticarUsuarioCommand(request.Email, request.Senha, request.EmpresaId));
        var token = jwtService.GerarToken(resultado);
        var refreshTokenValue = jwtService.GerarRefreshToken();
        var refreshTokenHash = TokenHashHelper.ComputeSha256Hash(refreshTokenValue);
        var expiraEm = DateTime.UtcNow.AddDays(7);

        var refreshToken = RefreshTokenEntity.Criar(
            resultado.UsuarioId,
            refreshTokenHash,
            expiraEm,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            HttpContext.Request.Headers.UserAgent);

        await refreshTokenRepository.AddAsync(refreshToken);

        var auditLog = AuditLogEntity.Criar(
            resultado.UsuarioId,
            "login",
            true,
            "Login realizado com sucesso",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            HttpContext.Request.Headers.UserAgent);
        await auditLogRepository.AddAsync(auditLog);
        await unitOfWork.CommitAsync();

        return DataOk(new LoginResponse(
            token,
            refreshTokenValue,
            jwtService.ExpiresInSeconds,
            new LoginUsuarioInfo(resultado.UsuarioId, resultado.Nome, resultado.Email, resultado.Nivel.ToString())));
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] CadastrarUsuarioCommand command)
        => DataOk(await cadastrarUsuarioUseCase.ExecuteAsync(command));

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenCommand command)
        => DataOk(await refreshTokenUseCase.ExecuteAsync(command));

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutCommand command)
        => DataOk(await logoutUseCase.ExecuteAsync(command));

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] EsqueciSenhaCommand command)
        => DataOk(await esqueciSenhaUseCase.ExecuteAsync(command));

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetarSenhaCommand command)
        => DataOk(await resetarSenhaUseCase.ExecuteAsync(command));

    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
        => DataOk(await obterUsuarioAtualUseCase.ExecuteAsync(new ObterUsuarioAtualCommand()));

    [HttpPatch("me")]
    public async Task<IActionResult> UpdateMe([FromBody] AtualizarUsuarioAtualCommand command)
        => DataOk(await atualizarUsuarioAtualUseCase.ExecuteAsync(command));

    [HttpPatch("me/password")]
    public async Task<IActionResult> ChangePassword([FromBody] AlterarSenhaCommand command)
        => DataOk(await alterarSenhaUseCase.ExecuteAsync(command));
}
