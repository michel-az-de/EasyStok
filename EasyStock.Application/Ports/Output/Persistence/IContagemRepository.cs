namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Persistencia da Contagem (Inventario). Os Get* de mutacao retornam entidades
/// RASTREADAS (sem AsNoTracking) para que mutacao + CommitAsync persistam.
/// </summary>
public interface IContagemRepository
{
    /// <summary>Contagem (header), rastreada — para transicoes de estado.</summary>
    Task<Contagem?> GetByIdAsync(Guid empresaId, Guid id);

    /// <summary>Contagem com Itens, rastreada — para Iniciar (materializa membership), Finalizar e Cancelar.</summary>
    Task<Contagem?> GetByIdComItensAsync(Guid empresaId, Guid id);

    /// <summary>Um ItemContagem, rastreado (xmin) — para o autosave do RegistrarItem.</summary>
    Task<ItemContagem?> GetItemAsync(Guid empresaId, Guid itemContagemId);

    Task AddAsync(Contagem contagem);
    Task UpdateAsync(Contagem contagem);

    /// <summary>
    /// Lotes (ItemEstoque) do escopo, para materializar a membership no start. Inclui
    /// Esgotado/qty-0 (permite achar sobra e detectar lote zerado); exclui Descartado.
    /// Todos = todos os lotes da empresa; Categoria = lotes de produtos da categoria;
    /// Loja = lotes da loja. Somente leitura (AsNoTracking).
    /// </summary>
    Task<IReadOnlyList<ItemEstoque>> GetLotesDoEscopoAsync(Guid empresaId, EscopoContagem escopo, Guid? escopoRefId);
}
