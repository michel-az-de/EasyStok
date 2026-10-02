using EasyStock.Application.UseCases.ContatoUsuario;
using Microsoft.AspNetCore.RateLimiting;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>Corpo de <c>PUT api/auth/me/telefone</c>: o telefone novo e a senha atual, que a troca de contato exige.</summary>
public sealed record DefinirTelefoneRequest(string? Telefone, string? SenhaAtual);

/// <summary>Corpo da verificação administrativa do telefone: o motivo (10 caracteres ou mais) fica auditado.</summary>
public sealed record VerificarTelefoneRequest(string? Motivo);

/// <summary>
/// Contato do usuário (N4): o próprio usuário define o telefone com a senha atual, e o superadmin verifica o telefone de
/// um usuário (a verificação por código no WhatsApp espera a N6 e a N8). O limite <c>auth</c> vale para o PUT, que vira
/// oráculo de senha como o PATCH <c>api/auth/me</c>.
/// </summary>
[SwaggerTag("Users / Contato")]
[ApiController]
[Route("api")]
public class ContatoUsuarioController(
    DefinirMeuTelefoneUseCase definirMeuTelefone,
    VerificarTelefoneUsuarioUseCase verificarTelefone,
    AdminAuditService audit) : EasyStockControllerBase
{
    [Authorize]
    [EnableRateLimiting("auth")]
    [SwaggerOperation(
        Summary = "Define o telefone do usuario autenticado",
        Description = "Exige a senha atual (errada: 403 e conta como falha de login). Telefone novo zera a verificacao, avisa o e-mail atual e derruba as sessoes.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPut("auth/me/telefone")]
    public async Task<IActionResult> DefinirTelefone([FromBody] DefinirTelefoneRequest request)
        => DataOk(await definirMeuTelefone.ExecuteAsync(new DefinirMeuTelefoneCommand(request.Telefone, request.SenhaAtual)));

    [Authorize(Policy = "SuperAdmin")]
    [SwaggerOperation(
        Summary = "Verifica o telefone de um usuario (SuperAdmin)",
        Description = "Verificacao administrativa: motivo de 10 caracteres, auditoria e consentimentos de WhatsApp (Seguranca e Operacional) gravados em nome do superadmin.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("admin/usuarios/{id:guid}/telefone/verificar")]
    public async Task<IActionResult> VerificarTelefone(Guid id, [FromBody] VerificarTelefoneRequest? request)
    {
        if (!TryEnsureNotEmpty(id, "Usuario", out var erro)) return erro!;
        if (!RequestGuards.TryValidarMotivo(request?.Motivo, out var motivo, out var erroMotivo))
            return DataBadRequest(erroMotivo!);

        var resultado = await verificarTelefone.ExecuteAsync(new VerificarTelefoneUsuarioCommand(id, motivo));

        // Sem o telefone no registro: a auditoria guarda quem verificou e de quem.
        await audit.LogAsync(
            "AdminVerificouTelefoneUsuario", $"UsuarioId={id}", motivo: motivo, entidadeAfetadaId: id);

        return DataOk(resultado);
    }
}
