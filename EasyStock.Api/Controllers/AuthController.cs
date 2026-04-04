using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace EasyStock.Api.Controllers;

public sealed record LoginRequest(string Email, string Senha, Guid? EmpresaId);

[ApiController]
[Route("api/auth")]
public class AuthController(AutenticarUsuarioUseCase autenticarUseCase, IConfiguration configuration) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var resultado = await autenticarUseCase.ExecuteAsync(
            new AutenticarUsuarioCommand(request.Email, request.Senha, request.EmpresaId));

        var secretKey = configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("Jwt:SecretKey nao configurado.");

        var expirationMinutes = int.TryParse(configuration["Jwt:ExpirationMinutes"], out var mins) ? mins : 60;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim("sub", resultado.UsuarioId.ToString()),
            new Claim("email", resultado.Email),
            new Claim("nome", resultado.Nome),
            new Claim("nivel", resultado.Nivel.ToString())
        };

        if (resultado.EmpresaId.HasValue)
            claims.Add(new Claim("empresaId", resultado.EmpresaId.Value.ToString()));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(expirationMinutes),
            Issuer = configuration["Jwt:Issuer"],
            Audience = configuration["Jwt:Audience"],
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        return Ok(new
        {
            token = tokenString,
            expiresIn = resultado.ExpiresIn,
            usuario = new
            {
                id = resultado.UsuarioId,
                nome = resultado.Nome,
                email = resultado.Email,
                nivel = resultado.Nivel.ToString()
            }
        });
    }
}
