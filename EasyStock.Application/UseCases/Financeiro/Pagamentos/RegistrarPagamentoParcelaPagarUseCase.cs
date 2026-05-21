using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

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
        if (cmd.Valor <= 0m) throw new UseCaseValidationException("Valor deve ser positivo.");
        if (string.IsNullOrWhiteSpace(cmd.Metodo)) throw new UseCaseValidationException("Metodo e obrigatorio.");

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

        var dataPagamento = DataUtc.ParaUtcOpcional(cmd.DataPagamento) ?? DateTime.UtcNow;
        try
        {
            var pagamento = PagamentoParcela.CriarConfirmado(
                cmd.EmpresaId, TipoLadoFinanceiro.Pagar,
                cmd.Valor, cmd.Metodo, dataPagamento,
                cmd.GatewayProvedor, cmd.GatewayTransactionId,
                cmd.Observacao, cmd.RegistradoPorUserId, cmd.RegistradoPorNome);

            parcela.RegistrarPagamento(pagamento);

            // MovimentoCaixa na MESMA transacao — sai pelo caixa do dia atual
            var mov = MovimentoCaixa.Criar(
                cmd.EmpresaId, "saida", cmd.Valor,
                dataMovimento: DateTime.UtcNow,
                lojaId: conta.LojaId);
            mov.Descricao = $"Pag. parcela {parcela.Numero}/{conta.Parcelas.Count} — {conta.Descricao}";
            mov.Metodo = cmd.Metodo.ToLowerInvariant();
            mov.Categoria = "contas-a-pagar";
            mov.Referencia = $"cp:{conta.Id}:parcela:{parcela.Id}:pag:{pagamento.Id}";
            mov.RegistradoPorUserId = cmd.RegistradoPorUserId;
            mov.RegistradoPorNome = cmd.RegistradoPorNome;
            mov.Origem = "api";

            await caixaRepo.AddMovimentoAsync(mov);
            pagamento.AssociarMovimentoCaixa(mov.Id);

            conta.AtualizarStatusPorParcelas();

            await contaRepo.AddEventoAsync(ContaFinanceiraEvento.ParaContaPagar(
                conta.EmpresaId, conta.Id, TipoEventoContaFinanceira.PagamentoConfirmado,
                descricao: $"Pagamento de R$ {cmd.Valor:F2} via {cmd.Metodo} na parcela {parcela.Numero}.",
                valorDepois: $"+R$ {cmd.Valor:F2}",
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
