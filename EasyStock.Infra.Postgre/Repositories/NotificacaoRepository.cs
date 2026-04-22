using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class NotificacaoRepository(EasyStockDbContext dbContext)
        : INotificacaoRepository
    {
        public Task<Notificacao?> GetByIdAsync(Guid id) =>
            dbContext.Notificacoes.FirstOrDefaultAsync(n => n.Id == id);

        public async Task<(IEnumerable<Notificacao> Items, int TotalCount)> GetByEmpresaAsync(
            Guid empresaId,
            bool? lida = null,
            TipoAlertaEstoque? tipo = null,
            SeveridadeNotificacao? severidade = null,
            int page = 1,
            int pageSize = 20)
        {
            var query = dbContext.Notificacoes
                .AsNoTracking()
                .Where(n => n.EmpresaId == empresaId);

            if (lida.HasValue)
                query = query.Where(n => n.Lida == lida.Value);
            if (tipo.HasValue)
                query = query.Where(n => n.TipoAlerta == tipo.Value);
            if (severidade.HasValue)
                query = query.Where(n => n.Severidade == severidade.Value);

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(n => n.CriadaEm)
                .ThenBy(n => n.Severidade)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        public async Task<IEnumerable<Notificacao>> GetRecentesNaoLidasAsync(Guid empresaId, int limit = 5)
        {
            return await dbContext.Notificacoes
                .AsNoTracking()
                .Where(n => n.EmpresaId == empresaId && !n.Lida)
                .OrderByDescending(n => n.CriadaEm)
                .ThenBy(n => n.Severidade)
                .Take(limit)
                .ToListAsync();
        }

        public async Task<NotificacaoResumo> GetResumoAsync(Guid empresaId)
        {
            var gruposPorSeveridade = await dbContext.Notificacoes
                .Where(n => n.EmpresaId == empresaId && !n.Lida)
                .GroupBy(n => n.Severidade)
                .Select(g => new { Severidade = g.Key, Count = g.Count() })
                .ToListAsync();

            var gruposPorTipo = await dbContext.Notificacoes
                .Where(n => n.EmpresaId == empresaId && !n.Lida)
                .GroupBy(n => n.TipoAlerta)
                .Select(g => new { Tipo = g.Key, Count = g.Count() })
                .ToListAsync();

            var porTipo = gruposPorTipo.ToDictionary(g => g.Tipo.ToString(), g => g.Count);

            return new NotificacaoResumo
            {
                TotalNaoLidas = gruposPorSeveridade.Sum(g => g.Count),
                Criticas = gruposPorSeveridade.FirstOrDefault(g => g.Severidade == SeveridadeNotificacao.Critica)?.Count ?? 0,
                Altas = gruposPorSeveridade.FirstOrDefault(g => g.Severidade == SeveridadeNotificacao.Alta)?.Count ?? 0,
                Medias = gruposPorSeveridade.FirstOrDefault(g => g.Severidade == SeveridadeNotificacao.Media)?.Count ?? 0,
                Informativas = gruposPorSeveridade.FirstOrDefault(g => g.Severidade == SeveridadeNotificacao.Informativa)?.Count ?? 0,
                PorTipo = porTipo
            };
        }

        public Task<bool> ExisteNotificacaoNaoLidaAsync(Guid empresaId, TipoAlertaEstoque tipo, Guid referenciaId) =>
            dbContext.Notificacoes.AnyAsync(n =>
                n.EmpresaId == empresaId &&
                n.TipoAlerta == tipo &&
                n.ReferenciaId == referenciaId &&
                !n.Lida);

        public Task<bool> ExisteNotificacaoDoDiaAsync(Guid empresaId, TipoAlertaEstoque tipo, Guid? referenciaId, DateTime dataReferencia)
        {
            var inicio = dataReferencia.Date;
            var fim = inicio.AddDays(1);
            return dbContext.Notificacoes.AnyAsync(n =>
                n.EmpresaId == empresaId &&
                n.TipoAlerta == tipo &&
                n.ReferenciaId == referenciaId &&
                n.CriadaEm >= inicio &&
                n.CriadaEm < fim);
        }

        public Task<int> CountNaoLidasAsync(Guid empresaId) =>
            dbContext.Notificacoes.CountAsync(n => n.EmpresaId == empresaId && !n.Lida);

        public Task AddAsync(Notificacao notificacao) =>
            dbContext.Notificacoes.AddAsync(notificacao).AsTask();

        public Task UpdateAsync(Notificacao notificacao)
        {
            dbContext.Notificacoes.Update(notificacao);
            return Task.CompletedTask;
        }

        public async Task MarcarTodasComoLidasAsync(Guid empresaId)
        {
            var agora = DateTime.UtcNow;
            await dbContext.Notificacoes
                .Where(n => n.EmpresaId == empresaId && !n.Lida)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.Lida, true)
                    .SetProperty(n => n.LidaEm, agora));
        }

        public async Task DeleteAsync(Guid empresaId, Guid id)
        {
            // Defesa multi-tenant: DELETE condicional via ExecuteDeleteAsync.
            await dbContext.Notificacoes
                .Where(n => n.Id == id && n.EmpresaId == empresaId)
                .ExecuteDeleteAsync();
        }
    }
}
