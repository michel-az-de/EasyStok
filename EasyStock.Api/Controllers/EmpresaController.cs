using EasyStock.Api.Http;
using EasyStock.Api.Services;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.RegistrarEmpresa;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using RefreshTokenEntity = EasyStock.Domain.Entities.RefreshToken;
using IJwtTokenService = EasyStock.Api.Services.IJwtTokenService;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Company / Empresa")]
[ApiController]
[Route("api/empresas")]
public class EmpresaController(
    RegistrarEmpresaUseCase registrarUseCase,
    IJwtTokenService jwtService,
    IRefreshTokenRepository refreshTokenRepository,
    IUnitOfWork unitOfWork) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Register a new company", Description = "Creates company account with initial admin user, 14-day trial, and returns a JWT for immediate access.")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("registrar")]
    public async Task<IActionResult> Registrar([FromBody] RegistrarEmpresaCommand command)
    {
        var resultado = await registrarUseCase.ExecuteAsync(command);

        var authResult = new AutenticarUsuarioResult(
            UsuarioId: resultado.UsuarioId,
            EmpresaId: resultado.EmpresaId,
            Nome: resultado.NomeAdmin,
            Email: resultado.Email,
            Nivel: NivelAcesso.Admin,
            Permissoes: []);

        var token = jwtService.GerarToken(authResult);
        var refreshTokenValue = jwtService.GerarRefreshToken();
        var refreshTokenHash = TokenHashHelper.ComputeSha256Hash(refreshTokenValue);

        var refreshToken = RefreshTokenEntity.Criar(
            resultado.UsuarioId,
            refreshTokenHash,
            DateTime.UtcNow.AddDays(30),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            HttpContext.Request.Headers.UserAgent.ToString());

        await refreshTokenRepository.AddAsync(refreshToken);
        await unitOfWork.CommitAsync();

        return DataCreated($"/api/empresas/{resultado.EmpresaId}", new
        {
            empresaId = resultado.EmpresaId,
            usuarioId = resultado.UsuarioId,
            nomeEmpresa = resultado.NomeEmpresa,
            token,
            refreshToken = refreshTokenValue,
            expiresIn = jwtService.ExpiresInSeconds,
            trialDias = 14,
            usuario = new
            {
                id = resultado.UsuarioId,
                nome = resultado.NomeAdmin,
                email = resultado.Email,
                nivel = "Admin"
            }
        });
    }
}
