using EasyStock.Api.Services;
using EasyStock.Application.UseCases.AlterarSenha;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.AtualizarUsuarioAtual;
using EasyStock.Application.UseCases.CadastrarUsuario;
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
    AlterarSenhaUseCase alterarSenhaUseCase) : ControllerBase
{
    private readonly AutenticarUsuarioUseCase _autenticarUseCase = autenticarUseCase;
    private readonly IJwtTokenService _jwtService = jwtService;
    private readonly IRefreshTokenRepository _refreshTokenRepository = refreshTokenRepository;
    private readonly IAuditLogRepository _auditLogRepository = auditLogRepository;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly CadastrarUsuarioUseCase _cadastrarUsuarioUseCase = cadastrarUsuarioUseCase;
    private readonly RefreshTokenUseCase _refreshTokenUseCase = refreshTokenUseCase;
    private readonly LogoutUseCase _logoutUseCase = logoutUseCase;
    private readonly EsqueciSenhaUseCase _esqueciSenhaUseCase = esqueciSenhaUseCase;
    private readonly ResetarSenhaUseCase _resetarSenhaUseCase = resetarSenhaUseCase;
    private readonly ObterUsuarioAtualUseCase _obterUsuarioAtualUseCase = obterUsuarioAtualUseCase;
    private readonly AtualizarUsuarioAtualUseCase _atualizarUsuarioAtualUseCase = atualizarUsuarioAtualUseCase;
    private readonly AlterarSenhaUseCase _alterarSenhaUseCase = alterarSenhaUseCase;

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var resultado = await _autenticarUseCase.ExecuteAsync(
            new AutenticarUsuarioCommand(request.Email, request.Senha, request.EmpresaId));
        var token = _jwtService.GerarToken(resultado);
        var refreshTokenValue = _jwtService.GerarRefreshToken();
        var refreshTokenHash = BCrypt.Net.BCrypt.HashPassword(refreshTokenValue);
        var expiraEm = DateTime.UtcNow.AddDays(7);

        var refreshToken = RefreshTokenEntity.Criar(
            resultado.UsuarioId,
            refreshTokenHash,
            expiraEm,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            HttpContext.Request.Headers.UserAgent);

        await _refreshTokenRepository.AddAsync(refreshToken);

        // Auditar login
        var auditLog = AuditLogEntity.Criar(
            resultado.UsuarioId,
            "login",
            true,
            "Login realizado com sucesso",
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            HttpContext.Request.Headers.UserAgent);
        await _auditLogRepository.AddAsync(auditLog);

        await _unitOfWork.CommitAsync();

        return Ok(new { data = new LoginResponse(
            token,
            refreshTokenValue,
            _jwtService.ExpiresInSeconds,
            new LoginUsuarioInfo(resultado.UsuarioId, resultado.Nome, resultado.Email, resultado.Nivel.ToString())), meta = new { }, error = (object?)null });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] CadastrarUsuarioCommand command)
    {
        var result = await _cadastrarUsuarioUseCase.ExecuteAsync(command);
        return Ok(new { data = result, meta = new { }, error = (object?)null });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenCommand command)
    {
        var result = await _refreshTokenUseCase.ExecuteAsync(command);
        return Ok(new { data = result, meta = new { }, error = (object?)null });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutCommand command)
    {
        var result = await _logoutUseCase.ExecuteAsync(command);
        return Ok(new { data = result, meta = new { }, error = (object?)null });
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] EsqueciSenhaCommand command)
    {
        var result = await _esqueciSenhaUseCase.ExecuteAsync(command);
        return Ok(new { data = result, meta = new { }, error = (object?)null });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetarSenhaCommand command)
    {
        var result = await _resetarSenhaUseCase.ExecuteAsync(command);
        return Ok(new { data = result, meta = new { }, error = (object?)null });
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var result = await _obterUsuarioAtualUseCase.ExecuteAsync(new ObterUsuarioAtualCommand());
        return Ok(new { data = result, meta = new { }, error = (object?)null });
    }

    [HttpPatch("me")]
    public async Task<IActionResult> UpdateMe([FromBody] AtualizarUsuarioAtualCommand command)
    {
        var result = await _atualizarUsuarioAtualUseCase.ExecuteAsync(command);
        return Ok(new { data = result, meta = new { }, error = (object?)null });
    }

    [HttpPatch("me/password")]
    public async Task<IActionResult> ChangePassword([FromBody] AlterarSenhaCommand command)
    {
        var result = await _alterarSenhaUseCase.ExecuteAsync(command);
        return Ok(new { data = result, meta = new { }, error = (object?)null });
    }
}
