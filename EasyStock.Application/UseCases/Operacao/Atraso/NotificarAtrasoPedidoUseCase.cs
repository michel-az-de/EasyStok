using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Notifications;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.UseCases.Operacao.Atraso;

/// <summary>
/// Regra do <c>PedidoAtrasoJob</c> (S21): pedido ainda aguardando com o início previsto vencido ganha
/// <c>AtrasoNotificadoEm</c> e o console recebe <c>pedido.atrasado</c>, uma única vez por início previsto.
///
/// <para>
/// Liga o tenant do candidato, trava o pedido (<c>SELECT FOR UPDATE</c>) e confere de novo no lock: duas
/// instâncias ou o operador mudando o status no meio não geram aviso duplicado nem aviso de pedido que já
/// começou. O evento de UI sai só depois do commit. O <c>PrazoEstourado</c> (e-mail e WhatsApp, N11) é enfileirado
/// antes do commit, então evento e marca saem juntos (ADR-0030).
/// </para>
/// </summary>
public sealed class NotificarAtrasoPedidoUseCase(
    IPedidoStorefrontRepository pedidoRepository,
    IOperacaoEventPublisher operacaoEventos,
    ITenantContextAccessor tenantContext,
    IUnitOfWork unitOfWork,
    INotificadorService notificador,
    IOptions<PrazosOptions> prazos,
    TimeProvider relogio,
    ILogger<NotificarAtrasoPedidoUseCase> logger)
{
    /// <summary>True quando marcou e publicou; false quando o pedido não estava (mais) atrasado ou já foi notificado.</summary>
    public async Task<bool> ExecuteAsync(PedidoAtrasoCandidato candidato, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidato);
        UseCaseGuards.EnsureEmpresaId(candidato.EmpresaId);
        tenantContext.SetCurrentTenant(candidato.EmpresaId);

        var atrasado = await unitOfWork.ExecuteInTransactionSemRetryAsync(
            token => MarcarNoLockAsync(candidato, token), ct);
        if (atrasado is null) return false;

        await operacaoEventos.PublicarAsync(EventosOperacao.PedidoAtrasado, candidato.EmpresaId, atrasado, ct);
        logger.LogInformation("Pedido atrasado pedidoId={PedidoId} inicioPrevisto={Inicio:o}",
            atrasado.PedidoId, atrasado.InicioPrevistoEm);
        return true;
    }

    private async Task<PedidoAtrasadoOperacao?> MarcarNoLockAsync(PedidoAtrasoCandidato candidato, CancellationToken ct)
    {
        var pedido = await pedidoRepository.GetForUpdateAsync(candidato.PedidoId, ct);
        if (pedido is null || pedido.EmpresaId != candidato.EmpresaId) return null;
        if (!pedido.MarcarAtrasoNotificado(relogio.GetUtcNow().UtcDateTime)) return null;

        await pedidoRepository.UpdateAsync(pedido, ct);
        var agora = relogio.GetUtcNow().UtcDateTime;
        var inicio = pedido.InicioPrevistoEm!.Value;
        await PrazoEstouradoEvento.EnfileirarAsync(notificador, prazos.Value, TipoPrazo.PedidoAtrasado,
            pedido.EmpresaId, pedido.Id, PrazoEstouradoEvento.Referencia(pedido.Id),
            PrazoEstouradoEvento.InstanteTexto(inicio), PrazoEstouradoEvento.Duracao(agora - inicio), usuarioId: null, ct);
        await unitOfWork.CommitAsync();

        return new PedidoAtrasadoOperacao(pedido.Id, pedido.Id.ToString("N")[..8].ToUpperInvariant(),
            pedido.ClienteNome, pedido.InicioPrevistoEm!.Value);
    }
}
