using System.Security.Cryptography;
using System.Text;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async.Pagamentos;

/// <summary>
/// Implementacao Onda P0 do <see cref="IPagamentoOrchestrator"/>:
///
/// <list type="number">
///   <item>Detecta replay de <c>Idempotency-Key</c> do cliente — retorna FaturaPagamento existente.</item>
///   <item>Calcula rota via <see cref="IPagamentoGatewayRouter.PlanejarRotaAsync"/>.</item>
///   <item>Cria <see cref="FaturaPagamento"/> Pendente + <see cref="PaymentAttempt"/> Iniciado em transacao.</item>
///   <item>Chama gateway com idempotencyKey derivada (SHA-256).</item>
///   <item>Em sucesso: persiste <c>GatewayTransactionId</c> + dados; attempt continua <c>Iniciado</c>
///   ate webhook (ou reconciliador em P1) confirmar.</item>
///   <item>Em falha: classifica via <see cref="IGatewayErrorClassifier"/>, marca attempt como
///   <c>FalhaPermanente</c>, marca <see cref="FaturaPagamento"/> como <c>Falhou</c> e retorna sem fallback.</item>
/// </list>
///
/// <para>
/// Em P1 ganha loop de fallback (itera <c>RoutingPlan.ProvedoresOrdenados</c>) com Polly
/// retry/circuit breaker e alimenta <see cref="IGatewayHealthStore"/> via decorator.
/// </para>
/// </summary>
public sealed class PagamentoOrchestrator(
    IFaturaRepository faturaRepo,
    IPagamentoGatewayRouter router,
    IPaymentAttemptRepository attemptRepo,
    IGatewayErrorClassifier classifier,
    IUnitOfWork uow,
    ILogger<PagamentoOrchestrator> logger) : IPagamentoOrchestrator
{
    public async Task<OrquestracaoResult> CriarComFallbackAsync(
        Fatura fatura,
        string metodo,
        string? clientIdempotencyKey = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fatura);
        if (string.IsNullOrWhiteSpace(metodo))
            return Falha("metodo-vazio", StatusFaturaPagamento.Pendente, faturaPagamentoId: null);

        // 1. Idempotencia da intencao
        if (!string.IsNullOrWhiteSpace(clientIdempotencyKey))
        {
            var existing = await faturaRepo.ObterPagamentoPorClientIdempotencyKeyAsync(
                fatura.EmpresaId, clientIdempotencyKey, ct);
            if (existing is not null)
            {
                logger.LogInformation(
                    "Replay de Idempotency-Key cliente — FaturaPagamento existente {PagamentoId}, Status={Status}",
                    existing.Id, existing.Status);
                return new OrquestracaoResult(
                    Sucesso: true,
                    Instrucao: null,
                    FaturaPagamentoId: existing.Id,
                    StatusFaturaPagamento: existing.Status,
                    Tentativas: Array.Empty<PaymentAttemptResumo>(),
                    MotivoFalhaFinal: null);
            }
        }

        // 2. Calcular rota
        var ctx = new RoutingContext(
            EmpresaId: fatura.EmpresaId,
            Metodo: metodo,
            Valor: fatura.Total,
            Moeda: string.IsNullOrWhiteSpace(fatura.Moeda) ? "BRL" : fatura.Moeda,
            Pais: "BR",
            ProvedoresJaTentados: Array.Empty<string>());
        var plan = await router.PlanejarRotaAsync(ctx, ct);

        if (plan.ProvedoresOrdenados.Count == 0)
        {
            logger.LogWarning(
                "Sem rota para EmpresaId={EmpresaId} Metodo={Metodo}: {Motivo}",
                fatura.EmpresaId, metodo, plan.Motivo);
            return Falha($"sem-rota: {plan.Motivo}", StatusFaturaPagamento.Pendente, faturaPagamentoId: null);
        }

        // P0: tenta apenas o primeiro provedor (sem fallback automatico).
        var provedor = plan.ProvedoresOrdenados[0];
        var gateway = router.ResolverPorProvedor(provedor)
            ?? throw new InvalidOperationException($"Gateway '{provedor}' nao registrado no DI mesmo apos cross-check no router.");
        const int tentativaNumero = 1;

        // 3. Cria FaturaPagamento + PaymentAttempt em transacao
        var pagamento = FaturaPagamento.CriarPendente(
            faturaId: fatura.Id,
            metodo: metodo,
            valor: fatura.Total,
            gatewayProvedor: provedor,
            empresaId: fatura.EmpresaId,
            clientIdempotencyKey: clientIdempotencyKey);

        try
        {
            fatura.RegistrarPagamento(pagamento);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            logger.LogWarning(ex,
                "Fatura nao aceita pagamento (FaturaId={FaturaId}): {Msg}", fatura.Id, ex.Message);
            return Falha(ex.Message, StatusFaturaPagamento.Pendente, faturaPagamentoId: null);
        }

        var idempotencyKey = ComputarIdempotencyKey(fatura.EmpresaId, pagamento.Id, provedor, tentativaNumero);
        var attempt = PaymentAttempt.Iniciar(
            empresaId: fatura.EmpresaId,
            faturaPagamentoId: pagamento.Id,
            faturaId: fatura.Id,
            provedor: provedor,
            metodo: metodo,
            tentativa: tentativaNumero,
            idempotencyKey: idempotencyKey,
            routingMotivo: plan.Motivo,
            cobrancaAssinaturaId: null,
            clientIdempotencyKey: clientIdempotencyKey);

        pagamento.MarcarEmProcessamento(provedor);
        pagamento.RegistrarNovaTentativa(attempt.Id, tentativaNumero);

        await attemptRepo.AdicionarAsync(attempt, motivoEvento: "iniciado", ct);
        await faturaRepo.UpdateAsync(fatura, ct);
        await uow.CommitAsync();

        // 4. Chamar gateway
        InstrucaoPagamento? instrucao = null;
        Exception? erroGateway = null;
        try
        {
            instrucao = await gateway.CriarAsync(fatura, metodo, idempotencyKey, ct);
            attempt.RegistrarRespostaGateway(
                gatewayTransactionId: instrucao.TransactionId,
                metadataJson: instrucao.DadosGatewayJson,
                latenciaMs: null);
            // Atualiza FaturaPagamento com resposta do gateway, mantendo EmProcessamento
            // ate webhook chegar. UI pode renderizar PixCopiaCola etc imediatamente.
            pagamento.GatewayTransactionId = instrucao.TransactionId;
            pagamento.DadosGatewayJson = instrucao.DadosGatewayJson;
            pagamento.AlteradoEm = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            erroGateway = ex;
            var cat = classifier.Classify(provedor, ex);
            attempt.MarcarFalhaPermanente(cat, errorCode: null, errorMessage: ex.Message);
            pagamento.RegistrarErroTentativa(cat);
            try { pagamento.MarcarFalhou($"Gateway {provedor}: {ex.Message}"); }
            catch (RegraDeDominioVioladaException) { /* estado invalido — log abaixo */ }
            logger.LogWarning(ex,
                "Orchestrator: gateway {Provedor} falhou pra Fatura {FaturaId}, attempt {AttemptId}",
                provedor, fatura.Id, attempt.Id);
        }

        await attemptRepo.AtualizarAsync(attempt,
            motivoEvento: erroGateway is null ? "gateway_criou_cobranca" : "gateway_falhou_permanente",
            ct);
        await faturaRepo.UpdateAsync(fatura, ct);
        await uow.CommitAsync();

        var resumo = new PaymentAttemptResumo(
            Id: attempt.Id,
            Provedor: attempt.Provedor,
            Tentativa: attempt.Tentativa,
            Status: attempt.Status,
            ErroCategoria: attempt.ErrorCategory,
            ErroMensagem: attempt.ErrorMessage);

        if (erroGateway is null)
        {
            return new OrquestracaoResult(
                Sucesso: true,
                Instrucao: instrucao,
                FaturaPagamentoId: pagamento.Id,
                StatusFaturaPagamento: pagamento.Status,
                Tentativas: new[] { resumo },
                MotivoFalhaFinal: null);
        }

        return new OrquestracaoResult(
            Sucesso: false,
            Instrucao: null,
            FaturaPagamentoId: pagamento.Id,
            StatusFaturaPagamento: pagamento.Status,
            Tentativas: new[] { resumo },
            MotivoFalhaFinal: $"{erroGateway.GetType().Name}: {erroGateway.Message}");
    }

    private static OrquestracaoResult Falha(string motivo, StatusFaturaPagamento status, Guid? faturaPagamentoId) =>
        new(
            Sucesso: false,
            Instrucao: null,
            FaturaPagamentoId: faturaPagamentoId,
            StatusFaturaPagamento: status,
            Tentativas: Array.Empty<PaymentAttemptResumo>(),
            MotivoFalhaFinal: motivo);

    /// <summary>
    /// Idempotency-Key SHA-256 hex (64 chars uppercase) derivada de
    /// <c>(empresaId, faturaPagamentoId, provedor, tentativa)</c>. Cada attempt
    /// tem chave estavel por tentativa — repeat call com mesma tupla = mesma key.
    /// </summary>
    private static string ComputarIdempotencyKey(Guid empresaId, Guid faturaPagamentoId, string provedor, int tentativa)
    {
        var raw = $"{empresaId:N}|{faturaPagamentoId:N}|{provedor.ToLowerInvariant()}|{tentativa}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}
