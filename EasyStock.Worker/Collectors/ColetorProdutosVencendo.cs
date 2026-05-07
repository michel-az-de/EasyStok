using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Worker.Collectors;

/// <summary>
/// Coleta lotes com itens expirando nos próximos N dias e gera EventoNotificacao.
/// Roda pelo ColetorEventosDeEstadoService a cada ColetorIntervalSeconds.
/// </summary>
public sealed class ColetorProdutosVencendo(
    EasyStockDbContext db,
    IEventoNotificacaoRepository eventoRepo,
    ILogger<ColetorProdutosVencendo> logger) : IColetorEventoNotificacao
{
    private static readonly int[] DiasPadrao = [7, 3, 1];

    public async Task ColetarAsync(CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var horizonte = agora.AddDays(DiasPadrao.Max());

        var itensVencendo = await db.Set<EasyStock.Domain.Entities.LoteItem>()
            .Include(i => i.Lote)
            .Include(i => i.Produto)
            .Where(i => i.ExpiraEm.HasValue
                     && i.ExpiraEm.Value >= agora
                     && i.ExpiraEm.Value <= horizonte
                     && i.Lote != null)
            .ToListAsync(ct);

        // Pré-carrega correlation IDs já existentes para evitar N+1 queries
        var candidatos = itensVencendo
            .Select(i => (item: i, diasRestantes: (int)(i.ExpiraEm!.Value.Date - agora.Date).TotalDays))
            .Where(x => DiasPadrao.Contains(x.diasRestantes))
            .ToList();

        var correlationIdsCandidatos = candidatos
            .Select(x => $"produto-vencendo-{x.item.Id}-d{x.diasRestantes}-{agora:yyyyMMdd}")
            .ToHashSet();

        var correlationIdsExistentes = await db.NotifEventos
            .Where(e => correlationIdsCandidatos.Contains(e.CorrelationId!))
            .Select(e => e.CorrelationId!)
            .ToHashSetAsync(ct);

        var processados = 0;
        foreach (var (item, diasRestantes) in candidatos)
        {
            var empresaId = item.Lote!.EmpresaId;
            var correlationId = $"produto-vencendo-{item.Id}-d{diasRestantes}-{agora:yyyyMMdd}";

            if (correlationIdsExistentes.Contains(correlationId)) continue;

            var payload = JsonSerializer.Serialize(new
            {
                loteItemId = item.Id,
                loteId = item.LoteId,
                produtoId = item.ProdutoId,
                nomeProduto = item.Nome,
                quantidade = item.Quantidade,
                expiraEm = item.ExpiraEm!.Value.ToString("yyyy-MM-dd"),
                diasRestantes,
                empresaId
            });

            var evento = EventoNotificacao.Criar(
                TipoEventoNotificacao.ProdutoVencendo,
                empresaId,
                payload,
                refEntidadeId: item.Id,
                correlationId: correlationId);

            await eventoRepo.AddAsync(evento, ct);
            processados++;
        }

        if (processados > 0)
            logger.LogInformation("ColetorProdutosVencendo: {Count} eventos gerados.", processados);

        if (processados > 0)
            await db.SaveChangesAsync(ct);
    }
}
