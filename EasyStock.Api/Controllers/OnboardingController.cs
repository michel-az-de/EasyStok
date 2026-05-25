using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.CompletarOnboarding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Wizard de onboarding pos-signup. Coleta dados que faltam (segmento, telefone,
/// nome fantasia) e cria a primeira loja. Idempotente.
/// </summary>
[SwaggerTag("Onboarding cliente externo")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/onboarding")]
public class OnboardingController(
    CompletarOnboardingUseCase completarUseCase,
    IEmpresaRepository empresaRepository,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Get onboarding status (completo + dados ja preenchidos)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] Guid empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        var empresa = await empresaRepository.GetByIdAsync(eid);
        if (empresa is null) return DataNotFound("Empresa nao encontrada.");
        return DataOk(new
        {
            empresaId = empresa.Id,
            completo = empresa.OnboardingCompleto,
            completoEm = empresa.OnboardingCompletoEm,
            nomeFantasia = empresa.NomeFantasia,
            telefone = empresa.Telefone,
            segmento = empresa.Segmento,
        });
    }

    public sealed record CompletarRequest(
        string? NomeFantasia,
        string? Telefone,
        string Segmento,
        string LojaNome,
        string? LojaEndereco,
        string? LojaTelefone);

    [SwaggerOperation(Summary = "Completa onboarding (atualiza empresa + cria primeira loja)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("completar")]
    public async Task<IActionResult> Completar([FromQuery] Guid empresaId, [FromBody] CompletarRequest body)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        var result = await completarUseCase.ExecuteAsync(new CompletarOnboardingCommand(
            EmpresaId: eid,
            NomeFantasia: body.NomeFantasia,
            Telefone: body.Telefone,
            Segmento: body.Segmento,
            LojaNome: body.LojaNome,
            LojaEndereco: body.LojaEndereco,
            LojaTelefone: body.LojaTelefone));
        return DataOk(result);
    }
}
