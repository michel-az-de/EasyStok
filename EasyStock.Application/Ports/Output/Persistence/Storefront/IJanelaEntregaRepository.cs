using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Ports.Output.Persistence.Storefront;

public interface IJanelaEntregaRepository
{
    Task<JanelaEntrega?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<JanelaEntrega>> GetAtivasDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default);

    /// <summary>Todas, inclusive as inativas, para o cadastro da loja (S45).</summary>
    Task<IReadOnlyList<JanelaEntrega>> GetTodasDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default);
    Task AddAsync(JanelaEntrega janela, CancellationToken ct = default);
    Task UpdateAsync(JanelaEntrega janela, CancellationToken ct = default);

    /// <summary>#1440: a janela já tem vaga (ativa ou liberada) ou bloqueio apontando para ela.</summary>
    Task<bool> TemUsoAsync(Guid janelaId, CancellationToken ct = default);

    /// <summary>#1440: remove a janela sem uso (o chamador confere <see cref="TemUsoAsync"/> antes).</summary>
    Task RemoveAsync(JanelaEntrega janela, CancellationToken ct = default);
}
