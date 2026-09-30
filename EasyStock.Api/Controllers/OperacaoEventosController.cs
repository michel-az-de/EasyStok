using EasyStock.Api.Services.Operacao;

namespace EasyStock.Api.Controllers;

/// <summary>
/// SSE de operação do console (S18): <c>GET api/operacao/eventos</c> com JWT no header <c>Authorization</c>.
/// Entrega os eventos nomeados da empresa da claim (<c>pedido.pago</c>, <c>pedido.mudou_status</c>,
/// <c>conversa.mensagem_recebida</c>, ...) e um heartbeat a cada 25 s. <c>Last-Event-ID</c> é ignorado nesta
/// versão: sem replay, o cliente recarrega a lista ao reconectar.
/// </summary>
[ApiController]
[Route("api/operacao/eventos")]
[Authorize]
public class OperacaoEventosController(OperacaoEventBroker broker, ICurrentUserAccessor currentUser) : ControllerBase
{
    public static readonly TimeSpan IntervaloHeartbeat = TimeSpan.FromSeconds(25);

    [HttpGet]
    public async Task Get(CancellationToken ct)
    {
        var empresaId = currentUser.EmpresaId;
        if (empresaId == Guid.Empty)
        {
            // Token sem empresa (SuperAdmin de plataforma): não há stream de operação para ele.
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers["X-Accel-Buffering"] = "no"; // nginx/caddy não bufferizam

        using var inscricao = broker.SubscribeOperacao($"{empresaId:N}:{Guid.NewGuid():N}", empresaId);
        await TransmissaoSse.TransmitirAsync(Response, inscricao.Slot, IntervaloHeartbeat, ct);
    }
}
