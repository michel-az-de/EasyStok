using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Worker.Collectors;

/// <summary>
/// Detecta assinaturas e trials que expiram em 3 dias e gera EventoNotificacao.
/// </summary>
public sealed class ColetorAssinaturasExpirando(
    EasyStockDbContext db,
    IEventoNotificacaoRepository eventoRepo,
    ILogger<ColetorAssinaturasExpirando> logger) : IColetorEventoNotificacao
{
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

        var processados = 0;
        foreach (var assinatura in assinaturas)
        {
            var dataExpiracao = assinatura.TrialFim ?? assinatura.DataFim!.Value;
            var diasRestantes = (int)(dataExpiracao.Date - agora.Date).TotalDays;
            if (diasRestantes > DiasAntes) continue;

            var correlationId = $"assinatura-expirando-{assinatura.Id}-d{diasRestantes}-{agora:yyyyMMdd}";

            var jaExiste = await db.NotifEventos
                .AnyAsync(e => e.CorrelationId == correlationId, ct);

            if (jaExiste) continue;

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
        }
    }
}
