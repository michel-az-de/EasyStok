using EasyStock.Api.Http;
using EasyStock.Api.Services.Helpdesk;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin/sla")]
[Authorize(Policy = "SuperAdmin")]
public class AdminSlaController(SlaConfiguracaoService slaService) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var lista = await slaService.ListarAsync();
        return DataOk(lista);
    }

    [HttpPut]
    public async Task<IActionResult> Salvar([FromBody] SalvarSlaRequest req)
    {
        try
        {
            var itens = new List<SalvarSlaConfigItem>();
            foreach (var i in req.Itens ?? [])
            {
                if (!Enum.TryParse<TicketPrioridade>(i.Prioridade, out var p))
                    return DataBadRequest($"Prioridade invalida: {i.Prioridade}.");
                itens.Add(new SalvarSlaConfigItem(i.EmpresaId, i.PlanoId, p, i.MinutosResposta, i.MinutosResolucao));
            }

            await slaService.SalvarLoteAsync(itens);
            return DataOk(new { count = itens.Count });
        }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return DataBadRequest(ex.Message); }
    }

    public sealed record SalvarSlaRequest(IReadOnlyList<SalvarSlaItemReq>? Itens);
    public sealed record SalvarSlaItemReq(Guid? EmpresaId, Guid? PlanoId, string Prioridade, int MinutosResposta, int MinutosResolucao);
}
