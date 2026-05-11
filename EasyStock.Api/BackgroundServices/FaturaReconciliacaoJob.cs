using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Job de reconciliacao com gateways de pagamento (F6).
///
/// <para>
/// Roda hora em hora e busca faturas em estado <c>Emitida</c> ou
/// <c>ParcialmentePaga</c> com <see cref="FaturaPagamento"/> Pendente
/// e idade entre 1h e 30 dias. Para cada uma, chama
/// <see cref="IPagamentoGateway.ConsultarAsync"/> e fecha gaps quando o
/// gateway responde <see cref="StatusGateway.Confirmado"/> mas a fatura
/// localmente ainda nao registrou o pagamento.
/// </para>
///
/// <para>
/// <b>Por que existe:</b> webhooks podem ser perdidos (caiu a rede entre
/// gateway e nosso servidor; processamento crashou apos receber 2xx;
/// retry do gateway nao pega; etc.). Reconciliacao garante que cobertura
/// 100% dos pagamentos chegue no sistema mesmo se webhook falha.
/// </para>
///
/// <para>
/// Idempotencia: cada consulta gera <see cref="TipoEventoFatura.ReconciliacaoConsultouGateway"/>
/// no <see cref="FaturaEvento"/>. Confirmacoes geram
/// <see cref="TipoEventoFatura.ReconciliacaoFechouGap"/>. Auditoria completa.
/// </para>
///
/// <para>
/// F11 — <see cref="IEfiPixService.ConsultarCobrancaAsync"/> ja foi implementado
/// (<c>GET /v2/cob/{txid}</c>); reconciliacao agora funciona ponta-a-ponta para Pix.
/// </para>
/// </summary>
public sealed class FaturaReconciliacaoJob(
    IServiceProvider serviceProvider,
    ILogger<FaturaReconciliacaoJob> logger) : BackgroundService
{
    private const long LockKeyJob = 0x4661_7475_5265_636FL; // "FaturRec\0"

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("FaturaReconciliacaoJob iniciado");

        // Aguarda 30s apos boot pra app subir antes de rodar
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunWithAdvisoryLockAsync(LockKeyJob, ProcessarReconciliacaoAsync, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "FaturaReconciliacaoJob: erro fatal — aguardando 1h antes do proximo retry.");
            }

            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Distributed lock via PG advisory — garante UMA replica processando.</summary>
    private async Task RunWithAdvisoryLockAsync(long key, Func<CancellationToken, Task> action, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetService<EasyStockDbContext>();
        if (db is null || !db.Database.IsNpgsql())
        {
            // DEV/SQLite: roda sem lock (replica unica).
            await action(ct);
            return;
        }

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "SELECT pg_try_advisory_lock(@key)";
            var p = cmd.CreateParameter();
            p.ParameterName = "key"; p.Value = key;
            cmd.Parameters.Add(p);
            var got = (bool)(await cmd.ExecuteScalarAsync(ct) ?? false);
            if (!got)
            {
                logger.LogInformation("FaturaReconciliacaoJob: outra replica esta processando. Pulando.");
                return;
            }
            try { await action(ct); }
            finally
            {
                using var unlock = db.Database.GetDbConnection().CreateCommand();
                unlock.CommandText = "SELECT pg_advisory_unlock(@key)";
                var pu = unlock.CreateParameter();
                pu.ParameterName = "key"; pu.Value = key;
                unlock.Parameters.Add(pu);
                await unlock.ExecuteScalarAsync(ct);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task ProcessarReconciliacaoAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var router = scope.ServiceProvider.GetRequiredService<IPagamentoGatewayRouter>();

        // Reconciliação varre faturas de TODOS os tenants — webhook caído num
        // tenant qualquer cai aqui. RLS exige bypass explícito ou a policy
        // tenant_isolation zeraria o batch (CurrentTenantId=Guid.Empty fora de
        // request). IgnoreQueryFilters já desligava o filtro EF; bypass cobre
        // a camada do banco.
        using var _ = db.UseRowLevelSecurityBypass();

        var agora = DateTime.UtcNow;
        var idadeMinima = agora.AddHours(-1);
        var idadeMaxima = agora.AddDays(-30);

        // Faturas candidatas: Emitida ou ParcialmentePaga, com FaturaPagamento Pendente
        // entre idadeMinima (mais antigo que 1h) e idadeMaxima (mais novo que 30d).
        var candidatas = await db.Faturas
            .IgnoreQueryFilters() // background — sem tenant context
            .Include(f => f.Pagamentos)
            .Where(f => (f.Status == StatusFatura.Emitida || f.Status == StatusFatura.ParcialmentePaga)
                && f.Pagamentos.Any(p =>
                    p.Status == StatusFaturaPagamento.Pendente
                    && p.GatewayTransactionId != null
                    && p.CriadoEm < idadeMinima
                    && p.CriadoEm > idadeMaxima))
            .Take(200) // batch — evita carregar tudo de uma vez
            .ToListAsync(ct);

        if (candidatas.Count == 0)
        {
            logger.LogDebug("FaturaReconciliacaoJob: nenhuma fatura candidata.");
            return;
        }

        logger.LogInformation("FaturaReconciliacaoJob: {N} faturas candidatas.", candidatas.Count);

        var fechadas = 0;
        foreach (var fatura in candidatas)
        {
            ct.ThrowIfCancellationRequested();
            var pendentes = fatura.Pagamentos
                .Where(p => p.Status == StatusFaturaPagamento.Pendente
                    && !string.IsNullOrEmpty(p.GatewayTransactionId))
                .ToList();

            foreach (var pag in pendentes)
            {
                try
                {
                    var gateway = router.ResolverPorProvedor(pag.GatewayProvedor);
                    if (gateway is null)
                    {
                        logger.LogWarning(
                            "FaturaReconciliacaoJob: gateway '{Provedor}' nao registrado. FaturaId={FaturaId}",
                            pag.GatewayProvedor, fatura.Id);
                        continue;
                    }

                    var status = await gateway.ConsultarAsync(pag.GatewayTransactionId!, ct);

                    db.FaturaEventos.Add(FaturaEvento.Criar(
                        fatura.Id,
                        TipoEventoFatura.ReconciliacaoConsultouGateway,
                        origem: "job-reconciliacao",
                        valorDepois: $"{pag.GatewayProvedor}:{pag.GatewayTransactionId} → {status}"));

                    if (status == StatusGateway.Confirmado)
                    {
                        // Fecha o gap CONFIRMANDO o pagamento Pendente original em vez de
                        // criar um FaturaPagamento novo. Criar novo deixa o original
                        // intocado (Pendente) — proxima rodada da janela 1h-30d ainda
                        // pegaria a fatura (se Status=ParcialmentePaga apos confirmacao
                        // parcial), e o gateway responderia Confirmado de novo, gerando
                        // pagamento duplicado e TotalPago > Total. Confirmar in-place
                        // mantem Pagamentos.Count estavel e idempotencia natural via
                        // FaturaPagamento.Confirmar() (no-op se ja Confirmado).
                        try
                        {
                            pag.Confirmar();
                            pag.Observacao = string.IsNullOrWhiteSpace(pag.Observacao)
                                ? "Confirmado via reconciliacao (webhook perdido)."
                                : $"{pag.Observacao}\nConfirmado via reconciliacao (webhook perdido).";

                            // Recalcula Status da fatura com o pagamento agora confirmado.
                            fatura.AtualizarStatusPorPagamentos();

                            db.FaturaEventos.Add(FaturaEvento.Criar(
                                fatura.Id,
                                TipoEventoFatura.PagamentoConfirmado,
                                origem: "job-reconciliacao",
                                valorDepois: $"+{pag.Valor:F2} {fatura.Moeda} via {pag.Metodo} ({pag.GatewayProvedor})"));

                            db.FaturaEventos.Add(FaturaEvento.Criar(
                                fatura.Id,
                                TipoEventoFatura.ReconciliacaoFechouGap,
                                origem: "job-reconciliacao",
                                valorDepois: $"+{pag.Valor:F2} {fatura.Moeda} (txid {pag.GatewayTransactionId})"));

                            fechadas++;
                            logger.LogInformation(
                                "FaturaReconciliacaoJob: gap fechado. FaturaId={FaturaId} Numero={Numero} txid={Txid}",
                                fatura.Id, fatura.Numero, pag.GatewayTransactionId);
                        }
                        catch (RegraDeDominioVioladaException regEx)
                        {
                            logger.LogWarning(regEx,
                                "FaturaReconciliacaoJob: regra de dominio impediu fechar gap. FaturaId={FaturaId}",
                                fatura.Id);
                        }
                    }
                    else if (status == StatusGateway.Falhou)
                    {
                        pag.MarcarFalhou("Gateway reportou Falhou via reconciliacao.");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "FaturaReconciliacaoJob: erro ao consultar pagamento. FaturaId={FaturaId} Pag={PagId}",
                        fatura.Id, pag.Id);
                }
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("FaturaReconciliacaoJob: rodada concluida. Gaps fechados={N}", fechadas);
    }
}
