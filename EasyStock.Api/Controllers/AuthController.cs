using EasyStock.Api.Services;
using EasyStock.Application.UseCases.AutenticarUsuario;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

public sealed record LoginRequest(string Email, string Senha, Guid? EmpresaId);

[ApiController]
[Route("api/auth")]
public class AuthController(AutenticarUsuarioUseCase autenticarUseCase, IJwtTokenService jwtService) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var resultado = await autenticarUseCase.ExecuteAsync(
            new AutenticarUsuarioCommand(request.Email, request.Senha, request.EmpresaId));
        var token = jwtService.GerarToken(resultado);
        return Ok(new {
            token,
            expiresIn = jwtService.ExpiresInSeconds,
            usuario = new { id = resultado.UsuarioId, nome = resultado.Nome, email = resultado.Email, nivel = resultado.Nivel.ToString() }
        });
    }
}
