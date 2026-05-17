using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AlterarPlano;
using EasyStock.Application.UseCases.CancelarAssinatura;
using EasyStock.Application.UseCases.ListarFaturas;
using EasyStock.Application.UseCases.PagarAgora;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Portal self-service de faturamento — operações que o próprio cliente
/// pode fazer sem passar pelo admin: ver faturas, cancelar, trocar plano.
/// </summary>
[SwaggerTag("Assinatura / Billing self-service")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/assinatura")]
public class AssinaturaClienteController(
    IAssinaturaEmpresaRepository assinaturaRepo,
    ListarFaturasUseCase listarFaturasUseCase,
    CancelarAssinaturaUseCase cancelarUseCase,
    AlterarPlanoUseCase alterarPlanoUseCase,
    PagarAgoraUseCase pagarAgoraUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Get current subscription status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        var assinatura = await assinaturaRepo.GetAtivaAsync(eid);
        if (assinatura is null) return DataNotFound("Nenhuma assinatura ativa.");
        return DataOk(new
        {
            assinatura.Id,
            assinatura.PlanoId,
            PlanNome = assinatura.Plano?.Nome,
            PrecoMensal = assinatura.Plano?.PrecoMensal,
            assinatura.Status,
            assinatura.DataInicio,
            assinatura.DataFim,
            assinatura.TrialAtivo,
            assinatura.TrialFim,
            assinatura.CupomCodigo,
            assinatura.DescontoAplicado,
            assinatura.SuspensaEm,
        });
    }

    [SwaggerOperation(Summary = "List billing invoices (last 24)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet("faturas")]
    public async Task<IActionResult> Faturas([FromQuery] Guid empresaId, [FromQuery] int limit = 24)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        var result = await listarFaturasUseCase.ExecuteAsync(eid, Math.Clamp(limit, 1, 100));
        return DataOk(result);
    }

    public sealed record CancelarRequest(bool Imediata = false);

    [SwaggerOperation(
        Summary = "Cancel subscription",
        Description = "By default keeps access until end of paid period. Set Imediata=true to cancel immediately.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("cancelar")]
    public async Task<IActionResult> Cancelar([FromQuery] Guid empresaId, [FromBody] CancelarRequest? body)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        await cancelarUseCase.ExecuteAsync(new CancelarAssinaturaCommand(eid, body?.Imediata ?? false));
        return DataOk(new { message = "Assinatura cancelada com sucesso." });
    }

    [SwaggerOperation(
        Summary = "Gera cobranca Pix imediata pra reativacao/upgrade",
        Description = "Self-service. Cria cobranca via Efi sem esperar o job diario. Idempotente: se ja existe pendente nao expirada, retorna a mesma.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("pagar-agora")]
    public async Task<IActionResult> PagarAgora([FromQuery] Guid empresaId, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        var result = await pagarAgoraUseCase.ExecuteAsync(new PagarAgoraCommand(eid), ct);
        return DataOk(result);
    }

    public sealed record AlterarPlanoRequest(Guid NovoPlanoId);

    [SwaggerOperation(
        Summary = "Upgrade or downgrade plan",
        Description = "Swaps the current plan immediately. Next billing cycle uses new price.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("upgrade")]
    public async Task<IActionResult> Upgrade([FromQuery] Guid empresaId, [FromBody] AlterarPlanoRequest body)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;
        var result = await alterarPlanoUseCase.ExecuteAsync(new AlterarPlanoCommand(eid, body.NovoPlanoId));
        return DataOk(result);
    }
}
