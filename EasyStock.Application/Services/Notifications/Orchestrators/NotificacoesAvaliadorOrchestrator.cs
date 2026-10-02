using System.Diagnostics;
using System.Diagnostics.Metrics;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Security;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.Services.Notifications.Orchestrators;

/// <summary>
/// Implementação pura — processa eventos pendentes via <see cref="INotificadorService"/> e
/// detecta rotinas Cron disparáveis. Sem loop, sem sleep — invocada por wrapper hosted ou trigger HTTP.
/// <para>
/// Padrão da S39 (N1): a lista de eventos pendentes é cross-tenant por natureza e sai numa leitura curta com o bypass de
/// RLS ligado pela porta <see cref="IRowLevelSecurityBypass"/> (só <c>(Id, EmpresaId)</c>); cada evento roda num
/// escopo de DI novo, com o tenant da empresa fixado antes da primeira conexão e <c>try/catch</c> próprio. Assim um
/// evento cujo commit falha não deixa o ChangeTracker sujo para os 199 seguintes.
/// </para>
/// Emite métricas <c>notifications.avaliador.run.duration</c> e
/// <c>notifications.avaliador.events_processed</c>.
/// </summary>
public sealed class NotificacoesAvaliadorOrchestrator(
    IServiceScopeFactory scopeFactory,
    IRowLevelSecurityBypass bypassRls,
    IEventoNotificacaoRepository eventoRepo,
    IRotinaRepository rotinaRepo,
    RotinaScheduler rotinaScheduler,
    ILogger<NotificacoesAvaliadorOrchestrator> logger) : INotificacoesAvaliadorOrchestrator
{
    private const int LimitePorRodada = 200;

    private static readonly Meter Meter = new("EasyStock.Notifications", "1.0");
    private static readonly Histogram<double> RunDuration = Meter.CreateHistogram<double>(
        "notifications.avaliador.run.duration", "ms",
        "Duração de 1 rodada do avaliador (eventos pendentes + cron rotinas)");
    private static readonly Counter<long> EventsProcessed = Meter.CreateCounter<long>(
        "notifications.avaliador.events_processed", "events",
        "Total de eventos avaliados pelo avaliador");

    public async Task ExecutarRodadaAsync(TimeSpan janelaAvaliacao, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // Leitura curta cross-tenant: o bypass entra ANTES da primeira conexão (o interceptor lê a flag na abertura).
            IReadOnlyList<EventoPendente> pendentes;
            IReadOnlyList<RotinaNotificacao> rotinasAtivas;
            using (bypassRls.Begin())
            {
                pendentes = await eventoRepo.ListarPendentesParaAvaliarAsync(LimitePorRodada, ct);
                rotinasAtivas = await rotinaRepo.ListarAtivasAsync(ct: ct);
            }

            // 1. Processa eventos pendentes (criados por coletores de estado / publicação direta)
            foreach (var pendente in pendentes)
            {
                ct.ThrowIfCancellationRequested();
                await AvaliarComEscopoProprioAsync(pendente, ct);
            }

            if (pendentes.Count > 0)
            {
                logger.LogInformation("AvaliadorOrchestrator: processados {Count} eventos pendentes.", pendentes.Count);
                EventsProcessed.Add(pendentes.Count);
            }

            // 2. Detecta rotinas Cron disparáveis (eventos serão criados pelos coletores de estado)
            var agora = DateTime.UtcNow;
            var ultimaExecucao = agora - (janelaAvaliacao > TimeSpan.Zero ? janelaAvaliacao : TimeSpan.FromMinutes(2));

            foreach (var rotina in rotinasAtivas.Where(r => r.TriggerTipo == TriggerTipoRotina.Cron))
            {
                if (!rotinaScheduler.DeveriasExecutar(rotina, ultimaExecucao, agora))
                    continue;

                logger.LogInformation(
                    "Rotina cron {Codigo} matched (eventos serão criados pelos coletores).",
                    rotina.Codigo);
            }
        }
        finally
        {
            sw.Stop();
            RunDuration.Record(sw.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>Escopo da empresa do evento, com tenant fixado antes da primeira conexão. Nunca lança, salvo cancelamento do host.</summary>
    private async Task AvaliarComEscopoProprioAsync(EventoPendente pendente, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(pendente.EmpresaId);

            var evento = await sp.GetRequiredService<IEventoNotificacaoRepository>()
                .ObterAsync(pendente.EmpresaId, pendente.Id, ct);
            // Outro avaliador (gatilho HTTP, réplica) já o fechou entre a lista e a leitura.
            if (evento is null || evento.Status != StatusEventoNotificacao.Pendente) return;

            await sp.GetRequiredService<INotificadorService>().AvaliarEventoAsync(evento, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao avaliar evento {EventoId}", pendente.Id);
            await MarcarFalhadoEmEscopoLimpoAsync(pendente, ex, ct);
        }
    }

    /// <summary>
    /// O que escapou do <see cref="INotificadorService.AvaliarEventoAsync"/> (ele já fecha o evento por conta própria,
    /// então só chega aqui o que ele não conseguiu gravar) vira <c>Falhado</c> com o motivo, num escopo limpo.
    /// </summary>
    private async Task MarcarFalhadoEmEscopoLimpoAsync(EventoPendente pendente, Exception ex, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(pendente.EmpresaId);
            var evento = await sp.GetRequiredService<IEventoNotificacaoRepository>()
                .ObterAsync(pendente.EmpresaId, pendente.Id, ct);
            if (evento is null || evento.Status != StatusEventoNotificacao.Pendente) return;

            evento.MarcarComoFalhado($"Erro não tratado: {ex.GetType().Name}: {ex.Message}");
            await sp.GetRequiredService<IUnitOfWork>().CommitAsync();
        }
        catch (Exception salvarEx) when (!ct.IsCancellationRequested)
        {
            logger.LogError(salvarEx, "Nem o estado de falha do evento {EventoId} foi gravado.", pendente.Id);
        }
    }
}
