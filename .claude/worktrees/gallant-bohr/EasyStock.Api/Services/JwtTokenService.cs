using EasyStock.Application.UseCases.AutenticarUsuario;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace EasyStock.Api.Services;

public sealed class JwtTokenService(IConfiguration configuration) : IJwtTokenService
{
    public int ExpiresInSeconds =>
        int.TryParse(configuration["Jwt:ExpirationMinutes"], out var mins) ? mins * 60 : 3600;

    public string GerarToken(AutenticarUsuarioResult resultado)
    {
        var secretKey = configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("Jwt:SecretKey nao configurado.");

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

        foreach (var permissao in resultado.Permissoes.Distinct())
            claims.Add(new Claim("permissao", permissao.ToString()));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddSeconds(ExpiresInSeconds),
            Issuer = configuration["Jwt:Issuer"],
            Audience = configuration["Jwt:Audience"],
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public string GerarRefreshToken()
    {
        return Guid.NewGuid().ToString() + Guid.NewGuid().ToString(); // 64 chars
    }
}
