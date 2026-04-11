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
                .OrderBy(n => n.Severidade)
                .ThenByDescending(n => n.CriadaEm)
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
                .OrderBy(n => n.Severidade)
                .ThenByDescending(n => n.CriadaEm)
                .Take(limit)
                .ToListAsync();
        }

        public async Task<NotificacaoResumo> GetResumoAsync(Guid empresaId)
        {
            var naoLidas = await dbContext.Notificacoes
                .AsNoTracking()
                .Where(n => n.EmpresaId == empresaId && !n.Lida)
                .ToListAsync();

            var porTipo = naoLidas
                .GroupBy(n => n.TipoAlerta.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            return new NotificacaoResumo
            {
                TotalNaoLidas = naoLidas.Count,
                Criticas = naoLidas.Count(n => n.Severidade == SeveridadeNotificacao.Critica),
                Altas = naoLidas.Count(n => n.Severidade == SeveridadeNotificacao.Alta),
                Medias = naoLidas.Count(n => n.Severidade == SeveridadeNotificacao.Media),
                Informativas = naoLidas.Count(n => n.Severidade == SeveridadeNotificacao.Informativa),
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

        public Task DeleteAsync(Guid id)
        {
            dbContext.Notificacoes.Remove(new Notificacao { Id = id });
            return Task.CompletedTask;
        }
    }
}
