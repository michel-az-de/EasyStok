using System.Diagnostics;
using System.Diagnostics.Metrics;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Security;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.Services.Notifications.Orchestrators;

/// <summary>
/// Implementação pura — processa eventos pendentes via <see cref="INotificadorService"/>.
/// Sem loop, sem sleep — invocada por wrapper hosted ou trigger HTTP.
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
    IUnitOfWork unitOfWork,
    PoliticaValidadeNotificacao politicaValidade,
    ILogger<NotificacoesAvaliadorOrchestrator> logger) : INotificacoesAvaliadorOrchestrator
{
    private const int LimitePorRodada = 200;

    /// <summary>Teto de eventos expirados por grupo de prazo em cada rodada; a seguinte continua o resto.</summary>
    private const int LimiteExpiracaoPorPrazo = 500;

    private static readonly Meter Meter = new("EasyStock.Notifications", "1.0");
    private static readonly Histogram<double> RunDuration = Meter.CreateHistogram<double>(
        "notifications.avaliador.run.duration", "ms",
        "Duração de 1 rodada do avaliador (eventos pendentes)");
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
            using (bypassRls.Begin())
            {
                // Quarentena: o evento Pendente além do prazo do tipo vira Expirado antes da lista, então o backlog
                // velho nunca é avaliado (e nunca vira mensagem).
                var expirados = await eventoRepo.ExpirarPendentesAsync(politicaValidade, LimiteExpiracaoPorPrazo, ct);
                if (expirados > 0)
                {
                    await unitOfWork.CommitAsync();
                    logger.LogWarning("AvaliadorOrchestrator: {Count} eventos expirados por prazo.", expirados);
                }

                pendentes = await eventoRepo.ListarPendentesParaAvaliarAsync(LimitePorRodada, ct);
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
