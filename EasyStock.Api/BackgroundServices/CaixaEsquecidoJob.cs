using System.Text.Json;
using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Job diário que detecta caixas esquecidos abertos de dias anteriores e publica uma
/// notificação in-app (SÓ notifica, não fecha — ADR-0034 / issue #641).
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

    private async Task ProcessarComLockAsync(CancellationToken ct)
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
            token => ProcessarAsync(sp, db, token),
            ct);
    }

    private async Task ProcessarAsync(IServiceProvider sp, EasyStockDbContext db, CancellationToken ct)
    {
        // Gate (B-BLOCKER-3): sem rotina ativa, o NotificadorService descartaria o evento como
        // "processado" sem criar outbox; aí carimbar o dedup mataria o aviso quando a rotina chegar.
        var rotinaAtiva = await db.NotifRotinas.IgnoreQueryFilters()
            .AnyAsync(r => r.TipoEvento == TipoEventoNotificacao.CaixaAbertoEsquecido && r.Ativa, ct);
        if (!rotinaAtiva)
        {
            logger.LogWarning("CaixaEsquecidoJob: rotina CaixaAbertoEsquecido inativa/ausente — pulando (seed pendente?).");
            return;
        }

        var repo = sp.GetRequiredService<ICaixaRepository>();
        var notificador = sp.GetRequiredService<INotificadorService>();

        var limite = HorarioBrasil.InicioRealDoDiaUtc(HorarioBrasil.Hoje());
        var esquecidas = await repo.GetAberturasEsquecidasAsync(limite, ct);

        var avisados = 0;
        foreach (var abertura in esquecidas)
        {
            if (abertura.NotificadoEsquecidoEm != null) continue; // dedup: já avisado
            if (ct.IsCancellationRequested) break;

            var usuarioId = abertura.RegistradoPorUserId
                ?? await ResolverDestinatarioFallbackAsync(db, abertura.EmpresaId, ct);
            if (usuarioId is null)
            {
                logger.LogWarning("CaixaEsquecidoJob: sem destinatário p/ abertura {Id} (empresa {Empresa}) — pulando.",
                    abertura.Id, abertura.EmpresaId);
                continue;
            }

            var diaAbertura = HorarioBrasil.DataOperacional(abertura.DataMovimento);
            var payload = JsonSerializer.Serialize(new
            {
                usuarioId = usuarioId.Value,            // resolve o destinatário do sino in-app
                aberturaId = abertura.Id,
                data_abertura = diaAbertura.ToString("dd/MM/yyyy"),
                valor_abertura = abertura.Valor,
                loja_nome = string.Empty
            });

            try
            {
                await notificador.PublicarEventoAsync(
                    TipoEventoNotificacao.CaixaAbertoEsquecido, abertura.EmpresaId,
                    usuarioDestinoId: usuarioId, payloadJson: payload, ct: ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "CaixaEsquecidoJob: falha ao publicar p/ abertura {Id} — não carimba (retry amanhã).", abertura.Id);
                continue; // não carimba → retenta na próxima rodada
            }

            // Carimba o dedup SÓ após publicar. A abertura veio AsNoTracking → ExecuteUpdate.
            await db.MovimentosCaixa.IgnoreQueryFilters()
                .Where(m => m.Id == abertura.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.NotificadoEsquecidoEm, DateTime.UtcNow), ct);
            avisados++;
        }

        logger.LogInformation(
            "CaixaEsquecidoJob: {Avisados} caixa(s) esquecido(s) notificado(s) de {Total} aberto(s) de dias anteriores.",
            avisados, esquecidas.Count);
    }

    // Fallback (quem abriu = null): usuário ativo mais antigo da empresa (≈ proprietário). Multi-tenant
    // via UsuarioEmpresa; cross-tenant → IgnoreQueryFilters (a varredura já roda sob bypass de RLS).
    private static async Task<Guid?> ResolverDestinatarioFallbackAsync(
        EasyStockDbContext db, Guid empresaId, CancellationToken ct)
        => await db.Set<UsuarioEmpresa>().IgnoreQueryFilters()
            .Where(ue => ue.EmpresaId == empresaId && ue.Ativo)
            .OrderBy(ue => ue.CriadoEm)
            .Select(ue => (Guid?)ue.UsuarioId)
            .FirstOrDefaultAsync(ct);
}
