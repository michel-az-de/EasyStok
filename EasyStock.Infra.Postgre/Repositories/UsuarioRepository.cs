using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class UsuarioRepository(EasyStockDbContext dbContext) : IUsuarioRepository
    {
        public Task<Usuario?> GetByIdAsync(Guid id) =>
            dbContext.Usuarios
                .AsNoTracking()
                .Include(u => u.Empresas)
                .Include(u => u.Perfis!)
                .ThenInclude(up => up.Perfil)
                .FirstOrDefaultAsync(u => u.Id == id);

        public async Task<Usuario?> GetByEmailAsync(string email)
        {
            // Login precede o contexto de tenant: CurrentTenantId=Guid.Empty e
            // IsSuperAdmin=false durante a autenticacao. O global query filter
            // (Onda 1.2) elimina Perfil/UsuarioPerfil/UsuarioEmpresa com EmpresaId
            // != Guid.Empty, e Perfil com EmpresaId=null tambem cai porque
            // null != Guid.Empty. Sem IgnoreQueryFilters o use case nao consegue
            // resolver nivel/empresa de NINGUEM no login.
            //
            // RLS (2026-05-11): IgnoreQueryFilters sozinho não basta — a policy
            // tenant_isolation no Postgres zera as linhas mesmo com filtro EF
            // desligado. UseRowLevelSecurityBypass emite SET app.bypass_rls=true
            // na conexão pelo SetTenantOnConnectionInterceptor, dando à query
            // de login a visibilidade global que ela precisa antes do JWT existir.
            using (dbContext.UseRowLevelSecurityBypass())
            {
                return await dbContext.Usuarios
                    .AsNoTracking()
                    .IgnoreQueryFilters()
                    .Include(u => u.Empresas)
                    .Include(u => u.Perfis!)
                        .ThenInclude(up => up.Perfil)
                            .ThenInclude(p => p!.Permissoes)
                    .FirstOrDefaultAsync(u => u.Email == email);
            }
        }

        public async Task<(IEnumerable<Usuario> Usuarios, int Total)> GetByEmpresaAsync(Guid empresaId, int page, int pageSize)
        {
            var query = dbContext.Usuarios
                .AsNoTracking()
                .Include(u => u.Perfis!.Where(p => p.EmpresaId == empresaId))
                    .ThenInclude(up => up.Perfil)
                .Where(u => u.Empresas!.Any(ue => ue.EmpresaId == empresaId));

            var total = await query.CountAsync();
            var usuarios = await query
                .OrderByDescending(u => u.CriadoEm)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (usuarios, total);
        }

        public async Task<int> CountByEmpresaAsync(Guid empresaId) =>
            await dbContext.UsuariosEmpresas.CountAsync(ue => ue.EmpresaId == empresaId && ue.Ativo);

        public Task AddAsync(Usuario usuario) =>
            dbContext.Usuarios.AddAsync(usuario).AsTask();

        public Task UpdateAsync(Usuario usuario)
        {
            dbContext.Usuarios.Update(usuario);
            return Task.CompletedTask;
        }

        public async Task<IEnumerable<Usuario>> SearchAsync(Guid empresaId, string termo, int maxResults = 20)
        {
            var pattern = $"%{termo.Trim()}%";
            return await dbContext.Usuarios
                .AsNoTracking()
                .Where(u => u.Empresas!.Any(ue => ue.EmpresaId == empresaId) &&
                    (EF.Functions.ILike(u.Nome, pattern) ||
                     EF.Functions.ILike(u.Email, pattern)))
                .OrderBy(u => u.Nome)
                .Take(maxResults)
                .ToListAsync();
        }
    }
}
