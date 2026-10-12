using System.Security.Claims;
using EasyStock.Api.Authentication;
using EasyStock.Api.Services.Operacao;

namespace EasyStock.Api.Controllers;

/// <summary>
/// SSE de operação do console (S18): <c>GET api/operacao/eventos</c> com JWT no header <c>Authorization</c>.
/// Entrega os eventos nomeados da empresa da claim (<c>pedido.pago</c>, <c>pedido.mudou_status</c>,
/// <c>conversa.mensagem_recebida</c>, ...) e um heartbeat a cada 25 s. <c>Last-Event-ID</c> é ignorado nesta
/// versão: sem replay, o cliente recarrega a lista ao reconectar. O stream fecha no <c>exp</c> do JWT e quando a
/// sessão do usuário é revogada (#1352), conferido a cada heartbeat.
/// </summary>
[ApiController]
[Route("api/operacao/eventos")]
[Authorize]
public class OperacaoEventosController(
    OperacaoEventBroker broker,
    ICurrentUserAccessor currentUser,
    ValidadorSessaoUsuario validadorSessao,
    TimeProvider relogio) : ControllerBase
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

        // #1352: o stream não vive mais que o JWT que o abriu. Fecha no exp do token (sem a tolerância de 5 min do
        // ClockSkew) e quando o corte de sessão do usuário o alcança; o cliente reconecta e recebe 401.
        var jwt = User;
        DateTimeOffset? expiraEm = long.TryParse(jwt.FindFirstValue("exp"), out var exp)
            ? DateTimeOffset.FromUnixTimeSeconds(exp)
            : null;

        var atendimento = currentUser.TemPermissao(Permissao.AcessarModuloAtendimento);
        // Cozinha/Entregas recebem a atualização da operação, sem eventos de conversa/cliente.
        using var inscricao = broker.SubscribeOperacao($"{empresaId:N}:{Guid.NewGuid():N}", empresaId,
            evento => atendimento || evento.StartsWith("pedido.", StringComparison.Ordinal)
                || evento.StartsWith("impressao.", StringComparison.Ordinal));
        await TransmissaoSse.TransmitirAsync(
            Response, inscricao.Slot, IntervaloHeartbeat, expiraEm, _ => validadorSessao.ValidarAsync(jwt), relogio, ct);
    }
}
