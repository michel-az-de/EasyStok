using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// Proxy BFF das abas de Auditoria (detalhe do pedido) e Timeline (detalhe do cliente).
/// As views fazem <c>fetch('/api/audit/...')</c> relativo que, no app host, cai no Web e nao
/// na Api (o Caddy do app host nao roteia <c>/api/*</c> e o Web tem rotas proprias sob
/// <c>/api/</c>). Aqui o Web chama a Api com a sessao (Bearer server-side) e devolve o envelope
/// <c>{ data }</c> que o frontend espera. Sem este proxy, <c>/api/audit/*</c> dava 404 no app
/// host (QA v1.10 BUG-PED-001 / #551). Espelha <see cref="AssinaturaController"/>.
/// </summary>
public class AuditController(AuditService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/api/audit/entity/{tipoEntidade}/{entidadeId:guid}")]
    public async Task<IActionResult> GetByEntity(
        string tipoEntidade, Guid entidadeId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var result = await svc.PorEntidadeAsync(tipoEntidade, entidadeId, page, pageSize);
        return result.Success
            ? Ok(new { data = result.Data })
            : StatusCode(result.HttpStatus >= 400 ? result.HttpStatus : 502, new { data = (object?)null });
    }

    [HttpGet("/api/audit/client-timeline/{clienteId:guid}")]
    public async Task<IActionResult> GetClientTimeline(
        Guid clienteId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var result = await svc.TimelineClienteAsync(clienteId, page, pageSize);
        return result.Success
            ? Ok(new { data = result.Data })
            : StatusCode(result.HttpStatus >= 400 ? result.HttpStatus : 502, new { data = (object?)null });
    }
}
