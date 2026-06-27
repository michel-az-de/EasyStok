using EasyStock.Application.Ports.Output.Persistence.Storefront;

namespace EasyStock.Application.UseCases.Admin.Storefront.ListarStorefrontsAdmin;

public sealed record ListarStorefrontsAdminCommand(
    int Skip,
    int Take,
    string? BuscaSlug,
    bool? Ativo) : ICommand;

/// <summary>Item resumido da listagem cross-tenant — sem dados pesados (logo binário etc).</summary>
public sealed record StorefrontAdminListItem(
    Guid Id,
    Guid EmpresaId,
    string Slug,
    string TituloPublico,
    string? DominioCustom,
    bool Ativo,
    int CardapioCount,
    DateTime CriadoEm);

public sealed record ListarStorefrontsAdminResult(
    IReadOnlyList<StorefrontAdminListItem> Itens,
    int Total,
    int Skip,
    int Take);

/// <summary>
/// Lista storefronts CROSS-TENANT para o painel super-admin (TASK-EZ-ADMIN-001).
/// Sem filtro por EmpresaId — admin vê tudo. Counts de cardápio carregados sob
/// demanda para não dependerem de join pesado.
/// </summary>
public class ListarStorefrontsAdminUseCase(
    IStorefrontRepository storefrontRepository,
    ICardapioItemRepository cardapioRepository)
    : IUseCase<ListarStorefrontsAdminCommand, ListarStorefrontsAdminResult>
{
    public async Task<ListarStorefrontsAdminResult> ExecuteAsync(ListarStorefrontsAdminCommand command)
    {
        var (storefronts, total) = await storefrontRepository.ListarAdminAsync(
            command.Skip,
            command.Take,
            command.BuscaSlug,
            command.Ativo);

        // Counts de cardápio em 1 query (GROUP BY) em vez de N COUNTs (era N+1).
        // Storefronts sem item não vêm no dicionário → default 0 no lookup.
        var ids = storefronts.Select(s => s.Id).ToList();
        var counts = await cardapioRepository.ContarPorStorefrontsAsync(ids);

        var itens = new List<StorefrontAdminListItem>(storefronts.Count);
        foreach (var s in storefronts)
        {
            itens.Add(new StorefrontAdminListItem(
                s.Id,
                s.EmpresaId,
                s.Slug,
                s.TituloPublico,
                s.DominioCustom,
                s.Ativo,
                counts.GetValueOrDefault(s.Id, 0),
                s.CriadoEm));
        }

        return new ListarStorefrontsAdminResult(itens, total, command.Skip, command.Take);
    }
}
