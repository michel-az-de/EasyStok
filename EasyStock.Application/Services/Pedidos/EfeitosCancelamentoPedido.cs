using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
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
    ILogger<EfeitosCancelamentoPedido> logger,
    SaldoFinanceiroPedido? saldos = null, ICreditoClienteComandaRepository? creditos = null)
{
    public static void ExigirPermissao(bool temPagamento, NivelAcesso nivel)
    {
        if (temPagamento && nivel is not (NivelAcesso.Gerente or NivelAcesso.Admin or NivelAcesso.SuperAdmin))
            throw new UnauthorizedAccessException("Só a dona ou um gerente pode cancelar pedido com pagamento, inclusive parcial.");
    }

    /// <returns>Se a ContaReceber do pedido foi cancelada agora.</returns>
    public async Task<bool> AplicarAsync(Pedido pedido, string? motivo, Guid? usuarioId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var motivoTexto = string.IsNullOrWhiteSpace(motivo) ? "Pedido cancelado" : motivo;

        await vagaOcupadaRepo.LiberarPorPedidoAsync(pedido.Id, $"Pedido cancelado: {motivoTexto}", ct);
        // Créditos usados voltam à carteira; recebimentos em dinheiro seguem a devolução existente.
        if (saldos is not null && creditos is not null && pedido.ClienteId is { } clienteId
            && await creditos.ObterPorOperacaoAsync(pedido.EmpresaId, pedido.Id, ct) is null)
        {
            var saldo = await saldos.ConsultarAsync(pedido, ct);
            var devolver = Math.Min(Math.Max(0, saldo.CupomAplicado - saldo.ConvertidoEmCupom), Math.Max(0, saldo.Alocado));
            if (devolver > 0) await creditos.AddAsync(CreditoClienteComanda.Criar(pedido.EmpresaId, clienteId,
                pedido.Id, pedido.Id, devolver, DateTime.UtcNow), ct);
        }

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
