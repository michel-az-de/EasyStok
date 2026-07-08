using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Banners;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class BannerConfirmacaoRepository(EasyStockDbContext db) : IBannerConfirmacaoRepository
    {
        public async Task<bool> RegistrarAsync(
            Guid bannerId, Guid usuarioId, BannerInteracaoTipo tipo, CancellationToken ct = default)
        {
            var jaExiste = await db.BannerConfirmacoes
                .AsNoTracking()
                .AnyAsync(c => c.BannerId == bannerId && c.UsuarioId == usuarioId && c.Tipo == tipo, ct);
            if (jaExiste) return false;

            await db.BannerConfirmacoes.AddAsync(BannerConfirmacao.Criar(bannerId, usuarioId, tipo), ct);
            return true;
        }

        public Task<bool> PossuiParaBannerAsync(Guid bannerId, CancellationToken ct = default)
            => db.BannerConfirmacoes.AsNoTracking().AnyAsync(c => c.BannerId == bannerId, ct);

        public Task<int> ContarAsync(Guid bannerId, CancellationToken ct = default)
            => db.BannerConfirmacoes.AsNoTracking().CountAsync(c => c.BannerId == bannerId, ct);
    }
}
