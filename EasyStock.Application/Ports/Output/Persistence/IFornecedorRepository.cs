namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IFornecedorRepository
    {
        Task<Fornecedor?> GetByIdAsync(Guid id);
        Task<Fornecedor?> GetByIdAsync(Guid empresaId, Guid id);
        Task<(IEnumerable<Fornecedor>, int total)> GetByEmpresaAsync(Guid empresaId, int page, int pageSize, bool? ativo = null, string? search = null, string? sort = "nome", string? order = "asc");
        Task<IEnumerable<Fornecedor>> SearchAsync(Guid empresaId, string termo, int maxResults = 20);
        Task AddAsync(Fornecedor fornecedor);
        Task UpdateAsync(Fornecedor fornecedor);

        // ── Audit (Onda P4) ───────────────────────────────────────
        Task AddAlteracaoAsync(FornecedorAlteracao alteracao);
        Task<IEnumerable<FornecedorAlteracao>> GetAlteracoesAsync(Guid fornecedorId, int max = 200);
    }
}
