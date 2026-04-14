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
                .Include(u => u.Empresas)
                .Include(u => u.Perfis!)
                .ThenInclude(up => up.Perfil)
                .FirstOrDefaultAsync(u => u.Id == id);

        public Task<Usuario?> GetByEmailAsync(string email) =>
            dbContext.Usuarios
                .Include(u => u.Empresas)
                .Include(u => u.Perfis!)
                    .ThenInclude(up => up.Perfil)
                        .ThenInclude(p => p!.Permissoes)
                .FirstOrDefaultAsync(u => u.Email == email);

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
