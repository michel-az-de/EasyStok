using EasyStock.Domain.Integration;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// Worker que alerta operacional quando o certificado A1 esta perto de vencer.
/// Roda a cada <see cref="WorkerOptions.NfeRenovacaoCertIntervalSeconds"/> (default 6h)
/// e dispara alertas escalonados: 30, 15, 7 e 3 dias antes do vencimento.
///
/// <para>
/// <b>Idempotencia:</b> nao re-alerta no mesmo threshold via flag em
/// <c>CredencialIntegracao.MetadataJson</c> (ou tabela auxiliar — F1 simplificado:
/// loga apenas; persistencia do "ultimo alerta enviado" e debito tecnico).
/// </para>
///
/// <para>
/// <b>Bypass RLS:</b> opera cross-tenant via <see cref="EasyStockDbContext.UseRowLevelSecurityBypass"/>
/// (e usa <c>IgnoreQueryFilters()</c> nas queries) — assim cobre todos os tenants.
/// </para>
/// </summary>
public sealed class RenovacaoCertificadoA1BackgroundService(
    IServiceProvider serviceProvider,
    IOptions<WorkerOptions> options,
    ILogger<RenovacaoCertificadoA1BackgroundService> logger) : BackgroundService
{
    // 0x43455254 = "CERT" — lock unico para single-instance
    private const long LockId = 0x4345_5254_0000_0001L;

    private static readonly int[] ThresholdsDias = [30, 15, 7, 3];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("RenovacaoCertificadoA1BackgroundService iniciado");

        var intervaloSegundos = Math.Max(3600, options.Value.NfeRenovacaoCertIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecutarTickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro durante tick do RenovacaoCertificadoA1BackgroundService");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(intervaloSegundos), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ExecutarTickAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var advisoryLock = sp.GetRequiredService<PostgresAdvisoryLock>();

        await advisoryLock.TentarExecutarAsync(LockId, async _ =>
        {
            var db = sp.GetRequiredService<EasyStockDbContext>();

            using var bypass = db.UseRowLevelSecurityBypass();

            var agora = DateTime.UtcNow;
            var limite30 = agora.AddDays(31);

            var certsProximosVencer = await db.Set<CredencialIntegracao>()
                .IgnoreQueryFilters()
                .Where(c => c.Categoria == CategoriaIntegracao.Fiscal
                         && c.Ativo
                         && c.ValidoAte != null
                         && c.ValidoAte <= limite30)
                .Select(c => new { c.Id, c.EmpresaId, c.ValidoAte })
                .ToListAsync(ct);

            foreach (var cert in certsProximosVencer)
            {
                var diasRestantes = (int)Math.Floor((cert.ValidoAte!.Value - agora).TotalDays);
                if (diasRestantes < 0)
                {
                    logger.LogError(
                        "Cert A1 EXPIRADO empresa={Empresa} credencial={Id} desde={Quando}",
                        cert.EmpresaId, cert.Id, cert.ValidoAte);
                    continue;
                }

                var threshold = ThresholdsDias.FirstOrDefault(d => diasRestantes <= d);
                if (threshold == 0) continue; // > 30 dias, nao alertar

                // TODO: persistir flag "ultimo threshold alertado" em metadata da credencial
                // para evitar alertar 4x ao mesmo tenant na mesma janela (F4 + outbox).
                // Por enquanto, log estruturado serve como sinal pro stack de observabilidade.
                logger.LogWarning(
                    "Cert A1 vencendo em {Dias} dias (threshold {Threshold}) empresa={Empresa} credencial={Id} validade={Validade}",
                    diasRestantes, threshold, cert.EmpresaId, cert.Id, cert.ValidoAte);
            }

            if (certsProximosVencer.Count > 0)
            {
                logger.LogInformation("RenovacaoCertA1 tick: {Count} certificados em janela de alerta", certsProximosVencer.Count);
            }
        }, ct);
    }
}
