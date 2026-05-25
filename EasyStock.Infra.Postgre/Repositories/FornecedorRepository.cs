using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class FornecedorRepository(EasyStockDbContext dbContext) : IFornecedorRepository
    {
        public Task<Fornecedor?> GetByIdAsync(Guid id) =>
            dbContext.Fornecedores.FirstOrDefaultAsync(f => f.Id == id);

        public Task<Fornecedor?> GetByIdAsync(Guid empresaId, Guid id) =>
            dbContext.Fornecedores.FirstOrDefaultAsync(f => f.EmpresaId == empresaId && f.Id == id);

        public async Task<(IEnumerable<Fornecedor>, int total)> GetByEmpresaAsync(
            Guid empresaId, int page, int pageSize,
            bool? ativo = null, string? search = null,
            string? sort = "criadoem", string? order = "desc")
        {
            var query = dbContext.Fornecedores
                .AsNoTracking()
                .Where(f => f.EmpresaId == empresaId);

            if (ativo.HasValue)
                query = query.Where(f => f.Ativo == ativo.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var termo = search.Trim();
                query = query.Where(f =>
                    EF.Functions.ILike(f.Nome, $"%{termo}%") ||
                    (f.Documento != null && EF.Functions.ILike(f.Documento, $"%{termo}%")) ||
                    (f.Email != null && EF.Functions.ILike(f.Email, $"%{termo}%")) ||
                    (f.Contato != null && EF.Functions.ILike(f.Contato, $"%{termo}%")));
            }

            var total = await query.CountAsync();

            var desc = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);
            query = sort?.ToLowerInvariant() switch
            {
                "criadoem" => desc ? query.OrderByDescending(f => f.CriadoEm) : query.OrderBy(f => f.CriadoEm),
                _ => desc ? query.OrderByDescending(f => f.Nome) : query.OrderBy(f => f.Nome),
            };

            var fornecedores = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (fornecedores, total);
        }

        public async Task<IEnumerable<Fornecedor>> SearchAsync(Guid empresaId, string termo, int maxResults = 20)
        {
            termo = termo.Trim();
            if (string.IsNullOrWhiteSpace(termo)) return [];

            var pattern = $"%{termo}%";

            return await dbContext.Fornecedores
                .AsNoTracking()
                .Where(f => f.EmpresaId == empresaId && f.Ativo &&
                    (EF.Functions.ILike(f.Nome, pattern) ||
                     (f.Documento != null && EF.Functions.ILike(f.Documento, pattern)) ||
                     (f.Email != null && EF.Functions.ILike(f.Email, pattern)) ||
                     (f.Contato != null && EF.Functions.ILike(f.Contato, pattern))))
                .OrderBy(f => f.Nome)
                .Take(maxResults)
                .ToListAsync();
        }

        public Task AddAsync(Fornecedor fornecedor) =>
            dbContext.Fornecedores.AddAsync(fornecedor).AsTask();

        public Task UpdateAsync(Fornecedor fornecedor)
        {
            dbContext.Fornecedores.Update(fornecedor);
            return Task.CompletedTask;
        }

        // ── Audit (Onda P4) ───────────────────────────────────────
        public Task AddAlteracaoAsync(FornecedorAlteracao alteracao)
        {
            dbContext.FornecedorAlteracoes.Add(alteracao);
            return Task.CompletedTask;
        }

        public async Task<IEnumerable<FornecedorAlteracao>> GetAlteracoesAsync(Guid fornecedorId, int max = 200) =>
            await dbContext.FornecedorAlteracoes.AsNoTracking()
                .Where(a => a.FornecedorId == fornecedorId)
                .OrderByDescending(a => a.AlteradoEm)
                .Take(max)
                .ToListAsync();
    }
}
