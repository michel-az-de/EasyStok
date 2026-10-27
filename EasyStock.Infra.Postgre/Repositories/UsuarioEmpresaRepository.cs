using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;
namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class UsuarioEmpresaRepository(EasyStockDbContext dbContext) : IUsuarioEmpresaRepository
    {
        public Task AddAsync(UsuarioEmpresa usuarioEmpresa) =>
            dbContext.UsuariosEmpresas.AddAsync(usuarioEmpresa).AsTask();
        public Task<UsuarioEmpresa?> GetByUsuarioEEmpresaAsync(Guid usuarioId, Guid empresaId) =>
            dbContext.UsuariosEmpresas.FirstOrDefaultAsync(x => x.UsuarioId == usuarioId && x.EmpresaId == empresaId);
        public async Task<IReadOnlyList<UsuarioEmpresa>> GetByUsuarioIdAsync(Guid usuarioId) =>
            await dbContext.UsuariosEmpresas
                .AsNoTracking()
                .Include(ue => ue.Empresa)
                .Where(ue => ue.UsuarioId == usuarioId)
                .ToListAsync();
        public Task UpdateAsync(UsuarioEmpresa usuarioEmpresa)
        {
            dbContext.UsuariosEmpresas.Update(usuarioEmpresa);
            return Task.CompletedTask;
        }

        public Task TravarHierarquiaAsync(Guid empresaId, CancellationToken ct = default)
        {
            // O lock é da transação: fora dela soltaria na hora, em silêncio (mesma guarda do CaixaRepository).
            if (dbContext.Database.CurrentTransaction is null)
                throw new InvalidOperationException("TravarHierarquiaAsync exige transação aberta.");
            var empresa = empresaId.ToString();
            var chave = "hierarquia:" + empresa;
            return dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.empresa_id', {empresa}, true), pg_advisory_xact_lock(hashtextextended({chave}, 0))", ct);
        }

        public Task<int> ContarDonasAtivasAsync(Guid empresaId, CancellationToken ct = default)
        {
            // Nível Admin na empresa pela regra do domínio (Perfil.DaNivelAdminNaEmpresa, o mesmo que NivelNaEmpresa == Admin).
            var perfisDeAdministracao = dbContext.Perfis.IgnoreQueryFilters().Where(Perfil.DaNivelAdminNaEmpresa).Select(p => p.Id);
            var administram = dbContext.UsuariosPerfis.IgnoreQueryFilters()
                .Where(up => up.EmpresaId == empresaId && perfisDeAdministracao.Contains(up.PerfilId))
                .Select(up => up.UsuarioId);
            return dbContext.UsuariosEmpresas.IgnoreQueryFilters()
                .Where(ue => ue.EmpresaId == empresaId && ue.EhDona && ue.Ativo && ue.Usuario!.Ativo
                    && administram.Contains(ue.UsuarioId))
                .Select(ue => ue.UsuarioId)
                .Distinct()
                .CountAsync(ct);
        }

        public Task<int> DefinirDonaAsync(Guid usuarioId, Guid empresaId, bool dona, CancellationToken ct = default) =>
            dbContext.UsuariosEmpresas.IgnoreQueryFilters()
                .Where(ue => ue.UsuarioId == usuarioId && ue.EmpresaId == empresaId && ue.EhDona != dona)
                .ExecuteUpdateAsync(s => s.SetProperty(ue => ue.EhDona, dona), ct);
    }
}
