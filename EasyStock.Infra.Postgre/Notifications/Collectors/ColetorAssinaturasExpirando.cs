using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Notifications.Collectors;

/// <summary>
/// Detecta assinaturas e trials que expiram em até <see cref="DiasAntes"/> dias e gera
/// EventoNotificacao. Movido de EasyStock.Worker/Collectors/ para uso unificado
/// (Worker, API in-process, cron HTTP).
/// </summary>
public sealed class ColetorAssinaturasExpirando(
    EasyStockDbContext db,
    IEventoNotificacaoRepository eventoRepo,
    ILogger<ColetorAssinaturasExpirando> logger) : IColetorEventoNotificacao
{
    private static readonly Meter Meter = new("EasyStock.Notifications", "1.0");
    private static readonly Counter<long> EventsGenerated = Meter.CreateCounter<long>(
        "notifications.collector.events_generated", "events",
        "Eventos gerados pelos coletores de estado");

    private const int DiasAntes = 3;

    public async Task ColetarAsync(CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var limite = agora.AddDays(DiasAntes + 1);

        var assinaturas = await db.AssinaturasEmpresa
            .Where(a => a.Status == StatusAssinatura.Ativa
                     && (
                         (a.DataFim.HasValue && a.DataFim.Value >= agora && a.DataFim.Value <= limite)
                         || (a.TrialFim.HasValue && a.TrialFim.Value >= agora && a.TrialFim.Value <= limite)
                     ))
            .ToListAsync(ct);

        // Pré-carrega correlation IDs já existentes para evitar N+1 queries
        var candidatos = assinaturas
            .Select(a =>
            {
                var datas = new List<DateTime>();
                if (a.TrialFim.HasValue) datas.Add(a.TrialFim.Value);
                if (a.DataFim.HasValue) datas.Add(a.DataFim.Value);
                var dataExpiracao = datas.Min();
                var diasRestantes = (int)(dataExpiracao.Date - agora.Date).TotalDays;
                return (assinatura: a, dataExpiracao, diasRestantes);
            })
            .Where(x => x.diasRestantes <= DiasAntes)
            .ToList();

        var correlationIdsCandidatos = candidatos
            .Select(x => $"assinatura-expirando-{x.assinatura.Id}-d{x.diasRestantes}-{agora:yyyyMMdd}")
            .ToHashSet();

        var correlationIdsExistentes = await db.NotifEventos
            .Where(e => correlationIdsCandidatos.Contains(e.CorrelationId!))
            .Select(e => e.CorrelationId!)
            .ToHashSetAsync(ct);

        var processados = 0;
        foreach (var (assinatura, dataExpiracao, diasRestantes) in candidatos)
        {
            var correlationId = $"assinatura-expirando-{assinatura.Id}-d{diasRestantes}-{agora:yyyyMMdd}";

            if (correlationIdsExistentes.Contains(correlationId)) continue;

            var payload = JsonSerializer.Serialize(new
            {
                assinaturaId = assinatura.Id,
                empresaId = assinatura.EmpresaId,
                planoId = assinatura.PlanoId,
                dataExpiracao = dataExpiracao.ToString("yyyy-MM-dd"),
                diasRestantes,
                eTrial = assinatura.TrialFim.HasValue
            });

            var evento = EventoNotificacao.Criar(
                TipoEventoNotificacao.AssinaturaExpirando,
                assinatura.EmpresaId,
                payload,
                refEntidadeId: assinatura.Id,
                correlationId: correlationId);

            await eventoRepo.AddAsync(evento, ct);
            processados++;
        }

        if (processados > 0)
        {
            logger.LogInformation("ColetorAssinaturasExpirando: {Count} eventos gerados.", processados);
            await db.SaveChangesAsync(ct);
            EventsGenerated.Add(processados, new KeyValuePair<string, object?>("collector", nameof(ColetorAssinaturasExpirando)));
        }
    }
}
