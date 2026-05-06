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

        var processados = 0;
        foreach (var item in itensVencendo)
        {
            var diasRestantes = (int)(item.ExpiraEm!.Value.Date - agora.Date).TotalDays;

            if (!DiasPadrao.Contains(diasRestantes)) continue;

            var empresaId = item.Lote!.EmpresaId;
            var correlationId = $"produto-vencendo-{item.Id}-d{diasRestantes}-{agora:yyyyMMdd}";

            // Idempotência: não duplicar evento para o mesmo item/dia
            var jaExiste = await db.NotifEventos
                .AnyAsync(e => e.CorrelationId == correlationId, ct);

            if (jaExiste) continue;

            var payload = JsonSerializer.Serialize(new
            {
                loteItemId = item.Id,
                loteId = item.LoteId,
                produtoId = item.ProdutoId,
                nomeProduto = item.Nome,
                quantidade = item.Quantidade,
                expiraEm = item.ExpiraEm.Value.ToString("yyyy-MM-dd"),
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
