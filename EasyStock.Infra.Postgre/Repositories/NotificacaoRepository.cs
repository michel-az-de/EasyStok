using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;

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
            int pageSize = 20,
            Guid? usuarioId = null)
        {
            var query = dbContext.Notificacoes
                .AsNoTracking()
                .Where(n => n.EmpresaId == empresaId && (!usuarioId.HasValue || n.UsuarioId == null || n.UsuarioId == usuarioId));

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

            await CompletarTitulosLegadosAsync(empresaId, items);
            return (items, totalCount);
        }

        public async Task<IEnumerable<Notificacao>> GetRecentesNaoLidasAsync(Guid empresaId, int limit = 5, Guid? usuarioId = null)
        {
            var items = await dbContext.Notificacoes
                .AsNoTracking()
                .Where(n => n.EmpresaId == empresaId && !n.Lida && (!usuarioId.HasValue || n.UsuarioId == null || n.UsuarioId == usuarioId))
                .OrderByDescending(n => n.CriadaEm)
                .ThenBy(n => n.Severidade)
                .Take(limit)
                .ToListAsync();
            await CompletarTitulosLegadosAsync(empresaId, items);
            return items;
        }

        // Envios antigos usavam o título do enum de estoque, mesmo para avisos de caixa.
        // Recupera o assunto do envio apenas nas cópias AsNoTracking, preservando o registro histórico.
        private async Task CompletarTitulosLegadosAsync(Guid empresaId, List<Notificacao> items)
        {
            var legadas = items.Where(n => n.OutboxMensagemId.HasValue
                && ((n.TipoAlerta == TipoAlertaEstoque.ValidadeProxima && n.Titulo == "Validade Próxima")
                    || (n.TipoAlerta == TipoAlertaEstoque.PedidoRecebido && n.Titulo == "Pedido Recebido"))).ToList();
            if (legadas.Count == 0) return;

            var ids = legadas.Select(n => n.OutboxMensagemId!.Value).ToArray();
            var assuntos = await dbContext.NotifOutboxMensagens.AsNoTracking()
                .Where(m => m.EmpresaId == empresaId && m.Canal == CanalNotificacao.InApp && ids.Contains(m.Id))
                .Select(m => new { m.Id, m.AssuntoRenderizado })
                .ToDictionaryAsync(m => m.Id, m => m.AssuntoRenderizado);
            foreach (var aviso in legadas)
                if (assuntos.TryGetValue(aviso.OutboxMensagemId!.Value, out var assunto) && !string.IsNullOrWhiteSpace(assunto))
                    aviso.Titulo = assunto;
        }

        public async Task<NotificacaoResumo> GetResumoAsync(Guid empresaId, Guid? usuarioId = null)
        {
            var gruposPorSeveridade = await dbContext.Notificacoes
                .Where(n => n.EmpresaId == empresaId && !n.Lida && (!usuarioId.HasValue || n.UsuarioId == null || n.UsuarioId == usuarioId))
                .GroupBy(n => n.Severidade)
                .Select(g => new { Severidade = g.Key, Count = g.Count() })
                .ToListAsync();

            var gruposPorTipo = await dbContext.Notificacoes
                .Where(n => n.EmpresaId == empresaId && !n.Lida && (!usuarioId.HasValue || n.UsuarioId == null || n.UsuarioId == usuarioId))
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

        public Task<int> CountNaoLidasAsync(Guid empresaId, Guid? usuarioId = null) =>
            dbContext.Notificacoes.CountAsync(n => n.EmpresaId == empresaId && !n.Lida
                && (!usuarioId.HasValue || n.UsuarioId == null || n.UsuarioId == usuarioId));

        public Task AddAsync(Notificacao notificacao) =>
            dbContext.Notificacoes.AddAsync(notificacao).AsTask();

        public Task UpdateAsync(Notificacao notificacao)
        {
            dbContext.Notificacoes.Update(notificacao);
            return Task.CompletedTask;
        }

        public async Task MarcarTodasComoLidasAsync(Guid empresaId, Guid? usuarioId = null)
        {
            var agora = DateTime.UtcNow;
            await dbContext.Notificacoes
                .Where(n => n.EmpresaId == empresaId && !n.Lida && (!usuarioId.HasValue || n.UsuarioId == null || n.UsuarioId == usuarioId))
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
