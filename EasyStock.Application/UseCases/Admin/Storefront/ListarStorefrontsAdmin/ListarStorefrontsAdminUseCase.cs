using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Common;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

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

        // N+1 controlado — listagem normalmente ≤ 100 storefronts, COUNT é cheap.
        // Em escala maior, agregamos em 1 query single via GROUP BY.
        var itens = new List<StorefrontAdminListItem>(storefronts.Count);
        foreach (var s in storefronts)
        {
            var count = await cardapioRepository.ContarPorStorefrontAsync(s.Id);
            itens.Add(new StorefrontAdminListItem(
                s.Id,
                s.EmpresaId,
                s.Slug,
                s.TituloPublico,
                s.DominioCustom,
                s.Ativo,
                count,
                s.CriadoEm));
        }

        return new ListarStorefrontsAdminResult(itens, total, command.Skip, command.Take);
    }
}
