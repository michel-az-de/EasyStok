using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Ports.Output.Persistence.Storefront;

public interface IFreteZonaRepository
{
    Task<FreteZona?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<FreteZona>> GetAtivasDoStorefrontOrdenadasAsync(Guid storefrontId, CancellationToken ct = default);
    Task<IReadOnlyList<FreteZona>> GetTodasDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default);
    Task AddAsync(FreteZona zona, CancellationToken ct = default);
    Task UpdateAsync(FreteZona zona, CancellationToken ct = default);

    /// <summary>
    /// Retorna a primeira <see cref="FreteZona"/> ativa do storefront cuja
    /// cobertura case com o CEP informado (zona tipo <c>cep_range</c>) ou com
    /// o bairro normalizado (zona tipo <c>bairros_lista</c>).
    ///
    /// <para>
    /// Ordem de avaliação é <c>Ordem</c> ascendente, depois <c>Id</c> — admin
    /// é responsável por evitar sobreposição. CEP deve estar normalizado para
    /// 8 dígitos; bairro deve estar normalizado lowercase sem acentos (string
    /// vazia quando o cliente só forneceu CEP).
    /// </para>
    ///
    /// <para>
    /// Retorna <see langword="null"/> quando nenhuma zona cobre.
    /// </para>
    /// </summary>
    Task<FreteZona?> BuscarZonaPorCepAsync(
        Guid storefrontId,
        string cep,
        string bairroNormalizado,
        CancellationToken ct = default);
}
