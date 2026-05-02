using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IClienteRepository
    {
        // ── Cliente raiz ──────────────────────────────────────────
        Task<Cliente?> GetByIdAsync(Guid id);
        Task<Cliente?> GetByIdAsync(Guid empresaId, Guid id);

        /// <summary>Carrega cliente com todas as coleções (endereços, telefones, etc) — pra tela de detalhe.</summary>
        Task<Cliente?> GetByIdWithDetailsAsync(Guid empresaId, Guid id);

        Task<(IEnumerable<Cliente> items, int total)> GetByEmpresaAsync(
            Guid empresaId,
            int page,
            int pageSize,
            bool? ativo = null,
            string? search = null,
            string? sort = "nome",
            string? order = "asc");

        Task<IEnumerable<Cliente>> SearchAsync(Guid empresaId, string termo, int maxResults = 20);
        Task<Cliente?> FindByDocumentoAsync(Guid empresaId, string documento);
        Task<Cliente?> FindByTelefoneAsync(Guid empresaId, string telefone);

        Task AddAsync(Cliente cliente);
        Task UpdateAsync(Cliente cliente);

        // ── Sub-recursos (1:N) ────────────────────────────────────
        Task AddEnderecoAsync(ClienteEndereco endereco);
        Task<bool> RemoveEnderecoAsync(Guid empresaId, Guid clienteId, Guid enderecoId);
        Task AddTelefoneAsync(ClienteTelefone telefone);
        Task<bool> RemoveTelefoneAsync(Guid empresaId, Guid clienteId, Guid telefoneId);
        Task AddDocumentoAsync(ClienteDocumento documento);
        Task<bool> RemoveDocumentoAsync(Guid empresaId, Guid clienteId, Guid documentoId);

        // ── Audit ─────────────────────────────────────────────────
        Task AddAlteracaoAsync(ClienteAlteracao alteracao);
        Task<IEnumerable<ClienteAlteracao>> GetAlteracoesAsync(Guid clienteId, int max = 100);
    }
}
