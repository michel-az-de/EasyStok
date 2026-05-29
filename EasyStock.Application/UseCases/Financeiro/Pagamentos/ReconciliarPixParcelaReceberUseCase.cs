using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;

namespace EasyStock.Application.UseCases.Financeiro.Pagamentos;

/// <summary>
/// Reconcilia uma parcela CR via Pix. Usado:
/// 1. Pelo <see cref="WebhookPixController"/> quando recebe notificacao Efi.
/// 2. Pelo <c>ContaReceberPixReconciliacaoJob</c> horario consultando gateway.
///
/// <para>Idempotente:</para>
/// - Se ja existe pagamento confirmado com mesmo Txid, no-op.
/// - Se valor pago diverge da parcela, registra com valor recebido (parcial).
/// </summary>
public sealed record ReconciliarPixParcelaReceberCommand(
    string Txid,
    decimal? ValorPagoEfi = null,
    DateTime? PagoEm = null,
    string? E2eId = null);

public sealed record ReconciliarPixResult(
    Guid? PagamentoId,
    Guid? ParcelaId,
    Guid? ContaId,
    string? StatusParcela,
    string? StatusConta,
    bool Reconciliado,
    string? Motivo);

public class ReconciliarPixParcelaReceberUseCase(
    IContaReceberRepository contaRepo,
    ICaixaRepository caixaRepo,
    IEfiPixService pix,
    IUnitOfWork uow,
    ILogger<ReconciliarPixParcelaReceberUseCase> logger)
{
    public async Task<ReconciliarPixResult> ExecuteAsync(ReconciliarPixParcelaReceberCommand cmd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Txid))
            return new ReconciliarPixResult(null, null, null, null, null, false, "txid vazio");

        var parcela = await contaRepo.GetParcelaByEfiTxidAsync(cmd.Txid, ct);
        if (parcela is null)
            return new ReconciliarPixResult(null, null, null, null, null, false, "parcela nao encontrada");

        var conta = parcela.ContaReceber;
        if (conta is null)
            return new ReconciliarPixResult(null, parcela.Id, null, null, null, false, "parcela orfa");

        // Idempotencia: ja confirmado
        var jaConfirmado = parcela.Pagamentos.Any(p =>
            p.GatewayProvedor == "EfiPix" &&
            p.GatewayTransactionId == cmd.Txid &&
            p.Status == StatusPagamentoParcela.Confirmado);
        if (jaConfirmado)
            return new ReconciliarPixResult(null, parcela.Id, conta.Id,
                parcela.Status.ToString(), conta.Status.ToString(), false, "ja reconciliado");

        // Se nao recebemos valor, consultar Efi
        decimal valorPago;
        DateTime pagoEm;
        if (cmd.ValorPagoEfi.HasValue && cmd.ValorPagoEfi.Value > 0m)
        {
            valorPago = cmd.ValorPagoEfi.Value;
            pagoEm = cmd.PagoEm ?? DateTime.UtcNow;
        }
        else
        {
            var efi = await pix.ConsultarCobrancaAsync(cmd.Txid, ct);
            if (efi.Status != EfiCobrancaStatus.Concluida)
                return new ReconciliarPixResult(null, parcela.Id, conta.Id,
                    parcela.Status.ToString(), conta.Status.ToString(), false,
                    $"status efi={efi.Status}");
            if (!efi.ValorPago.HasValue || efi.ValorPago.Value <= 0m)
                return new ReconciliarPixResult(null, parcela.Id, conta.Id,
                    parcela.Status.ToString(), conta.Status.ToString(), false, "valor zero");
            valorPago = efi.ValorPago.Value;
            pagoEm = efi.PagoEm ?? DateTime.UtcNow;
        }

        // Tolerancia: se valor pago for menor que saldo da parcela (cliente pagou parcial), aceitar
        // Se valor pago > saldo, cap no saldo (preserva invariante)
        var aplicado = Math.Min(valorPago, parcela.Saldo);
        if (aplicado <= 0m)
            return new ReconciliarPixResult(null, parcela.Id, conta.Id,
                parcela.Status.ToString(), conta.Status.ToString(), false, "saldo zero");

        try
        {
            var pagamento = PagamentoParcela.CriarConfirmado(
                conta.EmpresaId, TipoLadoFinanceiro.Receber,
                aplicado, "pix", DataUtc.ParaUtc(pagoEm),
                "EfiPix", cmd.Txid, "Reconciliado via Pix Efi");
            parcela.RegistrarPagamento(pagamento);

            var mov = MovimentoCaixa.Criar(
                conta.EmpresaId, "entrada", aplicado,
                dataMovimento: DateTime.UtcNow,
                lojaId: conta.LojaId);
            mov.Descricao = $"Pix recebido — parcela {parcela.Numero}/{conta.Parcelas.Count} — {conta.Descricao}";
            mov.Metodo = "pix";
            mov.Categoria = "contas-a-receber";
            mov.Referencia = $"cr:{conta.Id}:parcela:{parcela.Id}:txid:{cmd.Txid}";
            mov.Origem = "webhook";

            await caixaRepo.AddMovimentoAsync(mov);
            pagamento.AssociarMovimentoCaixa(mov.Id);

            conta.AtualizarStatusPorParcelas();

            await contaRepo.AddEventoAsync(ContaFinanceiraEvento.ParaContaReceber(
                conta.EmpresaId, conta.Id, TipoEventoContaFinanceira.PixReconciliado,
                descricao: $"Pix reconciliado: R$ {aplicado:F2} (txid={cmd.Txid})",
                origem: "webhook"), ct);

            await contaRepo.UpdateAsync(conta, ct);
            await uow.CommitAsync();

            logger.LogInformation("Pix reconciliado: parcela={ParcId} txid={Txid} valor={Valor}",
                parcela.Id, cmd.Txid, aplicado);

            return new ReconciliarPixResult(
                pagamento.Id, parcela.Id, conta.Id,
                parcela.Status.ToString(), conta.Status.ToString(), true, null);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            return new ReconciliarPixResult(null, parcela.Id, conta.Id,
                parcela.Status.ToString(), conta.Status.ToString(), false, ex.Message);
        }
    }
}
