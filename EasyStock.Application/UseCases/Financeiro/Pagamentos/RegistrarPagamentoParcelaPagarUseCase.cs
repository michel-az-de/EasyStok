using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;

namespace EasyStock.Application.UseCases.Financeiro.Pagamentos;

public sealed record RegistrarPagamentoParcelaPagarCommand(
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

public sealed record RegistrarPagamentoResult(
    Guid PagamentoId,
    Guid ParcelaId,
    Guid ContaId,
    string StatusParcela,
    string StatusConta,
    decimal ValorPago,
    decimal Saldo,
    Guid? MovimentoCaixaId);

public class RegistrarPagamentoParcelaPagarUseCase(
    IContaPagarRepository contaRepo,
    ICaixaRepository caixaRepo,
    IUnitOfWork uow,
    ILogger<RegistrarPagamentoParcelaPagarUseCase> logger)
{
    public async Task<RegistrarPagamentoResult?> ExecuteAsync(RegistrarPagamentoParcelaPagarCommand cmd, CancellationToken ct = default)
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
        var conta = parcela.ContaPagar
                    ?? throw new UseCaseValidationException("Parcela orfa (sem conta).");

        // Idempotencia gateway: se ja existe pagamento confirmado com mesmo gateway+tx, retorna
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
                cmd.EmpresaId, TipoLadoFinanceiro.Pagar,
                valor, metodo, dataPagamento,
                cmd.GatewayProvedor, cmd.GatewayTransactionId,
                cmd.Observacao, cmd.RegistradoPorUserId, cmd.RegistradoPorNome);

            parcela.RegistrarPagamento(pagamento, HorarioBrasil.HojeInstanteUtc());

            // MovimentoCaixa na MESMA transacao — sai pelo caixa do dia atual
            var mov = MovimentoCaixa.Criar(
                cmd.EmpresaId, "saida", valor,
                dataMovimento: DateTime.UtcNow,
                lojaId: conta.LojaId);
            mov.Descricao = $"Pag. parcela {parcela.Numero}/{conta.Parcelas.Count} — {conta.Descricao}";
            mov.Metodo = metodo;
            mov.Categoria = "contas-a-pagar";
            mov.Referencia = ReferenciaDeParcela.DoPagamento("cp", conta.Id, parcela.Id, pagamento.Id);
            mov.RegistradoPorUserId = cmd.RegistradoPorUserId;
            mov.RegistradoPorNome = cmd.RegistradoPorNome;
            mov.Origem = "api";

            await caixaRepo.AddMovimentoAsync(mov);
            pagamento.AssociarMovimentoCaixa(mov.Id);

            conta.AtualizarStatusPorParcelas();

            await contaRepo.AddEventoAsync(ContaFinanceiraEvento.ParaContaPagar(
                conta.EmpresaId, conta.Id, TipoEventoContaFinanceira.PagamentoConfirmado,
                descricao: $"Pagamento de R$ {valor:F2} via {metodo} na parcela {parcela.Numero}.",
                valorDepois: $"+R$ {valor:F2}",
                usuarioId: cmd.RegistradoPorUserId, usuarioNome: cmd.RegistradoPorNome,
                origem: "api"), ct);

            await contaRepo.UpdateAsync(conta, ct);
            await uow.CommitAsync();

            logger.LogInformation("Pagamento {PagId} registrado em parcela {ParcId} (CP={ContaId}). Caixa={MovId}",
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
