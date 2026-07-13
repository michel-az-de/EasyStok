using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Banners;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class BannerRepository(EasyStockDbContext db) : IBannerRepository
    {
        public Task<Banner?> ObterAsync(Guid id, CancellationToken ct = default)
            => db.Banners.FirstOrDefaultAsync(b => b.Id == id, ct);

        public async Task<(IReadOnlyList<Banner> Itens, int Total)> ListarAdminAsync(
            bool? ativo, int page, int pageSize, CancellationToken ct = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = db.Banners.AsNoTracking().AsQueryable();
            if (ativo.HasValue)
                query = query.Where(b => b.Ativo == ativo.Value);

            var total = await query.CountAsync(ct);
            var itens = await query
                .OrderByDescending(b => b.Prioridade)
                .ThenByDescending(b => b.CriadoEm)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return (itens, total);
        }

        public async Task<IReadOnlyList<Banner>> ListarAtivosNaoConfirmadosAsync(
            Guid usuarioId, DateTime agoraUtc, CancellationToken ct = default)
        {
            return await db.Banners
                .AsNoTracking()
                .Where(b =>
                    b.Ativo
                    && (b.InicioEm == null || b.InicioEm <= agoraUtc)
                    && (b.FimEm == null || b.FimEm > agoraUtc)
                    && !db.BannerConfirmacoes.Any(c =>
                        c.BannerId == b.Id
                        && c.UsuarioId == usuarioId
                        // Impressao é só alcance (analítico) — nunca esconde. Obrigatório some
                        // só com Confirmado; não-obrigatório some com qualquer interação ≠ Impressao.
                        && ((b.ExigeConfirmacao && c.Tipo == BannerInteracaoTipo.Confirmado)
                            || (!b.ExigeConfirmacao && c.Tipo != BannerInteracaoTipo.Impressao))))
                .OrderByDescending(b => b.Prioridade)
                .ThenByDescending(b => b.CriadoEm)
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<Banner>> ListarPendentesDeNotificacaoAsync(
            DateTime agoraUtc, CancellationToken ct = default)
            => await db.Banners
                .AsNoTracking()
                .Where(b => b.NotificarAoPublicar
                    && b.Ativo
                    && b.NotificadoEm == null
                    && (b.InicioEm == null || b.InicioEm <= agoraUtc)
                    && (b.FimEm == null || b.FimEm > agoraUtc))
                .OrderBy(b => b.CriadoEm)
                .ToListAsync(ct);

        public async Task InserirAsync(Banner banner, CancellationToken ct = default)
            => await db.Banners.AddAsync(banner, ct);

        public Task AtualizarAsync(Banner banner, CancellationToken ct = default)
        {
            db.Banners.Update(banner);
            return Task.CompletedTask;
        }

        public Task RemoverAsync(Banner banner, CancellationToken ct = default)
        {
            db.Banners.Remove(banner);
            return Task.CompletedTask;
        }

        public async Task<bool> MarcarNotificadoSeIneditoAsync(
            Guid bannerId, DateTime agoraUtc, CancellationToken ct = default)
        {
            // UPDATE atômico condicional — roda imediato no banco (ExecuteUpdate, fora do
            // SaveChanges). Na Fatia 6, o chamador o envolve numa transação explícita junto
            // com o staging do outbox. Retorna true só quando de fato marcou (rowcount=1).
            var utc = DateTime.SpecifyKind(agoraUtc, DateTimeKind.Utc);
            var linhas = await db.Banners
                .Where(b => b.Id == bannerId && b.NotificadoEm == null)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.NotificadoEm, utc), ct);
            return linhas == 1;
        }
    }
}
