using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class ClienteRepository(EasyStockDbContext db) : IClienteRepository
    {
        public Task<Cliente?> GetByIdAsync(Guid id) =>
            db.Clientes.FirstOrDefaultAsync(c => c.Id == id);

        public Task<Cliente?> GetByIdAsync(Guid empresaId, Guid id) =>
            db.Clientes.FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Id == id);

        public Task<Cliente?> GetByIdWithDetailsAsync(Guid empresaId, Guid id) =>
            db.Clientes
                .Include(c => c.Enderecos)
                .Include(c => c.Telefones)
                .Include(c => c.Documentos)
                .FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Id == id);

        public async Task<(IEnumerable<Cliente> items, int total)> GetByEmpresaAsync(
            Guid empresaId, int page, int pageSize,
            bool? ativo = null, string? search = null,
            string? sort = "nome", string? order = "asc")
        {
            var query = db.Clientes.AsNoTracking()
                .Where(c => c.EmpresaId == empresaId);

            if (ativo.HasValue) query = query.Where(c => c.Ativo == ativo.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var termo = search.Trim();
                query = query.Where(c =>
                    EF.Functions.ILike(c.Nome, $"%{termo}%") ||
                    (c.Documento != null && EF.Functions.ILike(c.Documento, $"%{termo}%")) ||
                    (c.Telefone != null && EF.Functions.ILike(c.Telefone, $"%{termo}%")) ||
                    (c.Email != null && EF.Functions.ILike(c.Email, $"%{termo}%")) ||
                    (c.Apt != null && EF.Functions.ILike(c.Apt, $"%{termo}%")));
            }

            var total = await query.CountAsync();
            var desc = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);

            query = sort?.ToLowerInvariant() switch
            {
                "criadoem"    => desc ? query.OrderByDescending(c => c.CriadoEm)    : query.OrderBy(c => c.CriadoEm),
                "lastorderat" => desc ? query.OrderByDescending(c => c.LastOrderAt) : query.OrderBy(c => c.LastOrderAt),
                "ordercount"  => desc ? query.OrderByDescending(c => c.OrderCount)  : query.OrderBy(c => c.OrderCount),
                _             => desc ? query.OrderByDescending(c => c.Nome)        : query.OrderBy(c => c.Nome),
            };

            var clientes = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return (clientes, total);
        }

        public async Task<IEnumerable<Cliente>> SearchAsync(Guid empresaId, string termo, int maxResults = 20)
        {
            termo = termo.Trim();
            if (string.IsNullOrWhiteSpace(termo)) return [];

            var pattern = $"%{termo}%";
            return await db.Clientes.AsNoTracking()
                .Where(c => c.EmpresaId == empresaId && c.Ativo &&
                    (EF.Functions.ILike(c.Nome, pattern) ||
                     (c.Telefone != null && EF.Functions.ILike(c.Telefone, pattern)) ||
                     (c.Documento != null && EF.Functions.ILike(c.Documento, pattern))))
                .OrderBy(c => c.Nome)
                .Take(maxResults)
                .ToListAsync();
        }

        public Task<Cliente?> FindByDocumentoAsync(Guid empresaId, string documento) =>
            db.Clientes.FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Documento == documento);

        public Task<Cliente?> FindByTelefoneAsync(Guid empresaId, string telefone) =>
            db.Clientes.FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Telefone == telefone);

        public Task AddAsync(Cliente cliente) { db.Clientes.Add(cliente); return Task.CompletedTask; }
        public Task UpdateAsync(Cliente cliente) { db.Clientes.Update(cliente); return Task.CompletedTask; }

        // ── Sub-recursos ──────────────────────────────────────────
        public Task AddEnderecoAsync(ClienteEndereco e) { db.Set<ClienteEndereco>().Add(e); return Task.CompletedTask; }
        public Task RemoveEnderecoAsync(Guid id) =>
            db.Set<ClienteEndereco>().Where(e => e.Id == id).ExecuteDeleteAsync();

        public Task AddTelefoneAsync(ClienteTelefone t) { db.Set<ClienteTelefone>().Add(t); return Task.CompletedTask; }
        public Task RemoveTelefoneAsync(Guid id) =>
            db.Set<ClienteTelefone>().Where(t => t.Id == id).ExecuteDeleteAsync();

        public Task AddDocumentoAsync(ClienteDocumento d) { db.Set<ClienteDocumento>().Add(d); return Task.CompletedTask; }
        public Task RemoveDocumentoAsync(Guid id) =>
            db.Set<ClienteDocumento>().Where(d => d.Id == id).ExecuteDeleteAsync();

        public Task AddAlteracaoAsync(ClienteAlteracao a) { db.Set<ClienteAlteracao>().Add(a); return Task.CompletedTask; }

        public async Task<IEnumerable<ClienteAlteracao>> GetAlteracoesAsync(Guid clienteId, int max = 100) =>
            await db.Set<ClienteAlteracao>().AsNoTracking()
                .Where(a => a.ClienteId == clienteId)
                .OrderByDescending(a => a.AlteradoEm)
                .Take(max).ToListAsync();
    }
}
