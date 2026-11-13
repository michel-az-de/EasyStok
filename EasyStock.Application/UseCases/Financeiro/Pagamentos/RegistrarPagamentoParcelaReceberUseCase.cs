using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;

namespace EasyStock.Application.UseCases.Financeiro.Pagamentos;

public sealed record RegistrarPagamentoParcelaReceberCommand(
    Guid EmpresaId,
    Guid ParcelaId,
    decimal Valor,
    string Metodo,
    DateTime? DataPagamento = null,
    string? Observacao = null,
    string GatewayProvedor = "Manual",
    string? GatewayTransactionId = null,
    Guid? RegistradoPorUserId = null,
    string? RegistradoPorNome = null);

public class RegistrarPagamentoParcelaReceberUseCase(
    IContaReceberRepository contaRepo,
    ICaixaRepository caixaRepo,
    IUnitOfWork uow,
    ILogger<RegistrarPagamentoParcelaReceberUseCase> logger)
{
    public async Task<RegistrarPagamentoResult?> ExecuteAsync(RegistrarPagamentoParcelaReceberCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        // #2095 (F7 a F9): arredonda antes de validar, método do caixa, textos no tamanho da coluna, data no dia de Brasília.
        var valor = ValidacaoFinanceira.Valor(cmd.Valor, "Valor");
        var metodo = ValidacaoFinanceira.MetodoPagamento(cmd.Metodo);
        ValidacaoFinanceira.Texto(cmd.Observacao, ValidacaoFinanceira.ObservacaoPagamentoMax, "Observação");
        ValidacaoFinanceira.Texto(cmd.GatewayProvedor, ValidacaoFinanceira.GatewayProvedorMax, "Gateway");
        ValidacaoFinanceira.Texto(cmd.GatewayTransactionId, ValidacaoFinanceira.GatewayTransacaoMax, "Referência do pagamento");
        var dataPagamento = ValidacaoFinanceira.DataPagamentoUtc(cmd.DataPagamento);

        var parcela = await contaRepo.GetParcelaWithContaAsync(cmd.EmpresaId, cmd.ParcelaId, ct);
        if (parcela is null) return null;
        var conta = parcela.ContaReceber
                    ?? throw new UseCaseValidationException("Parcela orfa (sem conta).");

        if (!string.IsNullOrWhiteSpace(cmd.GatewayTransactionId))
        {
            var existente = parcela.Pagamentos.FirstOrDefault(p =>
                p.GatewayProvedor == cmd.GatewayProvedor &&
                p.GatewayTransactionId == cmd.GatewayTransactionId &&
                p.Status == StatusPagamentoParcela.Confirmado);
            if (existente is not null)
                return new RegistrarPagamentoResult(
                    existente.Id, parcela.Id, conta.Id,
                    parcela.Status.ToString(), conta.Status.ToString(),
                    parcela.ValorPago, parcela.Saldo, existente.MovimentoCaixaId);
        }

        try
        {
            conta.GarantirAceitaPagamento();
            var pagamento = PagamentoParcela.CriarConfirmado(
                cmd.EmpresaId, TipoLadoFinanceiro.Receber,
                valor, metodo, dataPagamento,
                cmd.GatewayProvedor, cmd.GatewayTransactionId,
                cmd.Observacao, cmd.RegistradoPorUserId, cmd.RegistradoPorNome);

            parcela.RegistrarPagamento(pagamento, HorarioBrasil.HojeInstanteUtc());

            var mov = MovimentoCaixa.Criar(
                cmd.EmpresaId, "entrada", valor,
                dataMovimento: DateTime.UtcNow,
                lojaId: conta.LojaId);
            mov.Descricao = $"Recb. parcela {parcela.Numero}/{conta.Parcelas.Count} — {conta.Descricao}";
            mov.Metodo = metodo;
            mov.Categoria = "contas-a-receber";
            mov.Referencia = ReferenciaDeParcela.DoPagamento("cr", conta.Id, parcela.Id, pagamento.Id);
            mov.RegistradoPorUserId = cmd.RegistradoPorUserId;
            mov.RegistradoPorNome = cmd.RegistradoPorNome;
            mov.Origem = "api";

            await caixaRepo.AddMovimentoAsync(mov);
            pagamento.AssociarMovimentoCaixa(mov.Id);

            conta.AtualizarStatusPorParcelas();

            await contaRepo.AddEventoAsync(ContaFinanceiraEvento.ParaContaReceber(
                conta.EmpresaId, conta.Id, TipoEventoContaFinanceira.PagamentoConfirmado,
                descricao: $"Recebimento de R$ {valor:F2} via {metodo} na parcela {parcela.Numero}.",
                valorDepois: $"+R$ {valor:F2}",
                usuarioId: cmd.RegistradoPorUserId, usuarioNome: cmd.RegistradoPorNome,
                origem: "api"), ct);

            await contaRepo.UpdateAsync(conta, ct);
            await uow.CommitAsync();

            logger.LogInformation("Recebimento {PagId} registrado em parcela {ParcId} (CR={ContaId}). Caixa={MovId}",
                pagamento.Id, parcela.Id, conta.Id, mov.Id);

            return new RegistrarPagamentoResult(
                pagamento.Id, parcela.Id, conta.Id,
                parcela.Status.ToString(), conta.Status.ToString(),
                parcela.ValorPago, parcela.Saldo, mov.Id);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }
    }
}
