using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Enums.Financeiro;

namespace EasyStock.Application.Services.Pedidos;

/// <summary>
/// Efeitos de um pedido que passou a Cancelado, comuns a todo caminho de cancelamento
/// (<c>CancelarPedidoUseCase</c> e a troca de status genérica do KDS/PWA). #1506: antes só o
/// primeiro cancelava a ContaReceber e nenhum dos dois liberava a vaga da janela de entrega, que
/// seguia "esgotada" no site.
///   1. Cancela a ContaReceber gerada do pedido, se ainda cancelável. Paga fica: o estorno do
///      pagamento é cascata maior (EstornarPagamentoParcela).
///   2. Libera a <c>VagaOcupada</c> da janela (idempotente).
/// Não commita: compõe na unidade de trabalho do chamador.
/// </summary>
public class EfeitosCancelamentoPedido(
    IContaReceberRepository contaReceberRepo,
    IVagaOcupadaRepository vagaOcupadaRepo,
    ILogger<EfeitosCancelamentoPedido> logger)
{
    /// <returns>Se a ContaReceber do pedido foi cancelada agora.</returns>
    public async Task<bool> AplicarAsync(Pedido pedido, string? motivo, Guid? usuarioId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var motivoTexto = string.IsNullOrWhiteSpace(motivo) ? "Pedido cancelado" : motivo;

        await vagaOcupadaRepo.LiberarPorPedidoAsync(pedido.Id, $"Pedido cancelado: {motivoTexto}", ct);

        var contaReceber = await contaReceberRepo.GetByOrigemAsync(
            pedido.EmpresaId, OrigemContaFinanceira.Pedido, pedido.Id);
        if (contaReceber is null || contaReceber.Status == StatusContaFinanceira.Cancelada) return false;

        if (contaReceber.Status == StatusContaFinanceira.Paga)
        {
            logger.LogWarning(
                "Pedido {Id} cancelado, mas ContaReceber {CrId} esta PAGA — nao cancelada automaticamente. " +
                "Estorne os pagamentos da conta manualmente.", pedido.Id, contaReceber.Id);
            return false;
        }

        // Cancelar lanca se a conta estiver Paga; os demais status (Aberta/Rascunho/
        // ParcialmentePaga/Vencida) sao cancelaveis.
        contaReceber.Cancelar(motivoTexto, usuarioId);
        await contaReceberRepo.UpdateAsync(contaReceber);
        return true;
    }
}
