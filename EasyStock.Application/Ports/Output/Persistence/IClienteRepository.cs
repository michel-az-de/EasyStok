namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IClienteRepository
    {
        // ── Cliente raiz ──────────────────────────────────────────
        Task<Cliente?> GetByIdAsync(Guid id);
        Task<Cliente?> GetByIdAsync(Guid empresaId, Guid id);

        /// <summary>
        /// Vários clientes da empresa em uma consulta (#1564), sem as coleções. Somente leitura.
        /// Id que não existe ou é de outra empresa não vem na lista.
        /// </summary>
        Task<IReadOnlyList<Cliente>> ListarPorIdsAsync(Guid empresaId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

        /// <summary>Carrega cliente com todas as coleções (endereços, telefones, etc) — pra tela de detalhe.</summary>
        Task<Cliente?> GetByIdWithDetailsAsync(Guid empresaId, Guid id);

        Task<(int OrderCount, DateTime? LastOrderAt)> ObterMetricasPedidosAsync(Guid empresaId, Guid clienteId);

        Task<(IEnumerable<Cliente> items, int total)> GetByEmpresaAsync(
            Guid empresaId,
            int page,
            int pageSize,
            bool? ativo = null,
            string? search = null,
            string? sort = "nome",
            string? order = "asc",
            string? relacionamento = null);

        /// <summary>
        /// #3244: a lista de Cadastros com a relação de cada cliente (compras, gasto, ocorrências, notas, tags, risco)
        /// e o gasto da carteira inteira da empresa, em poucas consultas em lote, sem consulta por linha.
        /// </summary>
        Task<ListaDeClientes> ListarComRelacaoAsync(
            Guid empresaId,
            int page,
            int pageSize,
            bool? ativo = null,
            string? search = null,
            string? sort = "nome",
            string? order = "asc",
            string? relacionamento = null,
            CancellationToken ct = default);

        Task<IEnumerable<Cliente>> SearchAsync(Guid empresaId, string termo, int maxResults = 20, bool incluirInativos = false);
        Task<Cliente?> FindByTelefoneAsync(Guid empresaId, string telefone);
        Task<bool> TemTelefoneCompartilhadoAsync(Guid empresaId, IReadOnlyList<string> telefones,
            IReadOnlyList<string> hashes, CancellationToken ct = default);

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

    /// <summary>#3244: uma linha da lista. Compra é a <c>RegraDeCompra</c>; risco é o predicado de <c>relacionamento=recompra</c>.</summary>
    public sealed record ClienteRelacao(
        Cliente Cliente,
        int Compras,
        decimal Gasto,
        DateTime? PrimeiraCompraEm,
        DateTime? UltimaCompraEm,
        int OcorrenciasAbertas,
        int OcorrenciasTotal,
        int NotasTotal,
        string? UltimaNota,
        IReadOnlyList<string> Tags,
        bool EmRisco)
    {
        public const int TamanhoDaNotaCurta = 80;
        public const int MaximoDeTags = 3;
    }

    public sealed record ListaDeClientes(IReadOnlyList<ClienteRelacao> Itens, int Total, decimal GastoTotalDaCarteira);
}
