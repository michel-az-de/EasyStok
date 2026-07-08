using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Banners;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories
{
    /// <summary>
    /// Leitura do console de recebimento (#875). <c>banner_confirmacoes</c> e <c>Usuario</c>
    /// são globais (sem EmpresaId), então o SuperAdmin enxerga todos os lojistas sem RLS.
    /// A série é agrupada por dia BRT em memória (volume por aviso é limitado); se crescer,
    /// migrar para agregação no banco (AT TIME ZONE).
    /// </summary>
    public sealed class BannerRecebimentoQuery(EasyStockDbContext db) : IBannerRecebimentoQuery
    {
        private static readonly TimeZoneInfo Brt = ResolverFusoBrasil();

        public async Task<BannerRecebimentoReadModel?> ObterAsync(
            Guid bannerId, int page, int pageSize, string? tipo, string? busca, CancellationToken ct = default)
        {
            var banner = await db.Banners.AsNoTracking()
                .Where(b => b.Id == bannerId)
                .Select(b => new { b.TituloInterno, b.ExigeConfirmacao })
                .FirstOrDefaultAsync(ct);
            if (banner is null) return null;

            // Público-alvo: usuários ativos (lojistas). SuperAdmins são poucos; refino de
            // exclusão por nível fica para depois. Denominador do "%".
            var elegiveis = await db.Usuarios.AsNoTracking().CountAsync(u => u.Ativo, ct);

            var doBanner = db.BannerConfirmacoes.AsNoTracking().Where(c => c.BannerId == bannerId);
            var viram = await doBanner.Select(c => c.UsuarioId).Distinct().CountAsync(ct);
            var confirmaram = await doBanner
                .Where(c => c.Tipo == BannerInteracaoTipo.Confirmado)
                .Select(c => c.UsuarioId).Distinct().CountAsync(ct);

            // Série diária (BRT): carrega os timestamps do aviso (limitado) e agrupa em memória.
            var stamps = await doBanner.Select(c => c.RegistradoEm).ToListAsync(ct);
            var serie = stamps
                .GroupBy(ts => TimeZoneInfo
                    .ConvertTimeFromUtc(DateTime.SpecifyKind(ts, DateTimeKind.Utc), Brt)
                    .ToString("yyyy-MM-dd"))
                .OrderBy(g => g.Key)
                .Select(g => new RecebimentoSerieDia(g.Key, g.Count()))
                .ToList();

            // Log paginado — join a Usuario (global). Filtro por tipo + busca (nome/email).
            var eventos =
                from c in db.BannerConfirmacoes.AsNoTracking()
                join u in db.Usuarios.AsNoTracking() on c.UsuarioId equals u.Id
                where c.BannerId == bannerId
                select new { c.UsuarioId, u.Nome, u.Email, c.Tipo, c.RegistradoEm };

            if (!string.IsNullOrWhiteSpace(tipo) && Enum.TryParse<BannerInteracaoTipo>(tipo, ignoreCase: true, out var t))
                eventos = eventos.Where(e => e.Tipo == t);

            if (!string.IsNullOrWhiteSpace(busca))
            {
                var termo = $"%{busca.Trim()}%";
                eventos = eventos.Where(e => EF.Functions.ILike(e.Nome, termo) || EF.Functions.ILike(e.Email, termo));
            }

            var totalEventos = await eventos.CountAsync(ct);
            var pagina = await eventos
                .OrderByDescending(e => e.RegistradoEm)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            var linhas = pagina
                .Select(e => new RecebimentoEventoRaw(e.UsuarioId, e.Nome, e.Email, e.Tipo.ToString(), e.RegistradoEm))
                .ToList();

            return new BannerRecebimentoReadModel(
                banner.TituloInterno, banner.ExigeConfirmacao, elegiveis, viram, confirmaram, serie, linhas, totalEventos);
        }

        public Task<string?> ObterEmailUsuarioAsync(Guid usuarioId, CancellationToken ct = default)
            => db.Usuarios.AsNoTracking()
                .Where(u => u.Id == usuarioId)
                .Select(u => (string?)u.Email)
                .FirstOrDefaultAsync(ct);

        private static TimeZoneInfo ResolverFusoBrasil()
        {
            foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT");
        }
    }
}
