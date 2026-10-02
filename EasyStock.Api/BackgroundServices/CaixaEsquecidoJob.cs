using EasyStock.Application.UseCases.Caixa;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Job diário que detecta caixas esquecidos abertos de dias anteriores e publica uma
/// notificação (SÓ notifica, não fecha — ADR-0034 / issue #641). A regra vive em
/// <see cref="AvisarCaixasEsquecidasUseCase"/> (N11); o job só agenda, liga o bypass e segura o lock.
///
/// <para><b>Cross-tenant + RLS:</b> liga <c>UseRowLevelSecurityBypass()</c> ANTES de o advisory
/// lock abrir a conexão (o interceptor lê a flag em ConnectionOpened; ligar depois do open é
/// apagão silencioso — provado em CaixaEsquecidoCrossTenantRlsTests).</para>
///
/// <para><b>Gate (B-BLOCKER-3):</b> só publica se a <c>RotinaNotificacao</c> do evento estiver
/// ativa; senão o pipeline descartaria o evento e a flag de dedup mataria avisos futuros.</para>
///
/// <para><b>Dedup:</b> carimba <c>MovimentoCaixa.NotificadoEsquecidoEm</c> APÓS publicar (1 aviso
/// por sessão esquecida). Semântica at-least-once: um crash entre publicar e carimbar pode gerar
/// 1 aviso extra no dia seguinte — aceitável para um lembrete.</para>
///
/// <para><b>Destinatário:</b> quem abriu o caixa (<c>RegistradoPorUserId</c>); fallback = usuário
/// ativo mais antigo da empresa (≈ proprietário). Sem destinatário resolvível, pula — o sino
/// in-app exige <c>usuarioId</c> no payload.</para>
/// </summary>
public sealed class CaixaEsquecidoJob(
    IServiceProvider serviceProvider,
    ILogger<CaixaEsquecidoJob> logger) : BackgroundService
{
    private const double AlvoHoraUtc = 10.0; // 10:00 UTC ≈ 07:00 BRT (antes do expediente)

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("CaixaEsquecidoJob iniciado");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var nextRun = now.Date.AddHours(AlvoHoraUtc);
                if (now.TimeOfDay.TotalHours >= AlvoHoraUtc)
                    nextRun = now.Date.AddDays(1).AddHours(AlvoHoraUtc);

                var delay = nextRun - now;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, stoppingToken);

                await ProcessarComLockAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "CaixaEsquecidoJob: erro — aguardando 1h.");
                try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); }
                catch { break; }
            }
        }
    }

    /// <summary>Uma rodada do job (com advisory lock). Pública para os testes de integração; o loop diário a chama.</summary>
    public async Task ProcessarComLockAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<EasyStockDbContext>();

        // Bypass de RLS ANTES de qualquer conexão abrir: o PostgresAdvisoryLock abre a conexão e o
        // interceptor lê db.BypassRowLevelSecurity em ConnectionOpened. Liga-lo depois do open não
        // re-emite o SET app.bypass_rls e a varredura cross-tenant zera (CaixaEsquecidoCrossTenantRlsTests).
        using var _rls = db.UseRowLevelSecurityBypass();

        var advisoryLock = sp.GetRequiredService<PostgresAdvisoryLock>(); // mesmo DbContext scoped
        await advisoryLock.TentarExecutarAsync(
            LockKeys.CaixaEsquecidoMonitor,
            token => ProcessarAsync(sp, token),
            ct);
    }

    private static Task ProcessarAsync(IServiceProvider sp, CancellationToken ct) =>
        sp.GetRequiredService<AvisarCaixasEsquecidasUseCase>().ExecuteAsync(ct);
}
