using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ListarCardapioAdmin;

public sealed record ListarCardapioAdminCommand(Guid StorefrontId) : ICommand;

public sealed record CardapioItemAdminListItem(
    Guid Id,
    Guid ProdutoId,
    string ProdutoNome,
    double OrdemExibicao,
    decimal PrecoEfetivo,
    decimal? PrecoStorefrontOverride,
    bool Visivel,
    bool Disponivel,
    string? Tag,
    string? FotoUrl,
    string? PesoExibicao);

public sealed record ListarCardapioAdminResult(
    Guid StorefrontId,
    string StorefrontSlug,
    string StorefrontTitulo,
    IReadOnlyList<CardapioItemAdminListItem> Itens);

/// <summary>
/// Lista TODOS os items do cardápio (visíveis E ocultos) para o painel admin.
/// Difere do listar público (TASK-EZ-MENU-001) que só retorna visíveis+disponíveis.
/// </summary>
public class ListarCardapioAdminUseCase(
    IStorefrontRepository storefrontRepository,
    ICardapioItemRepository cardapioRepository)
    : IUseCase<ListarCardapioAdminCommand, ListarCardapioAdminResult>
{
    public async Task<ListarCardapioAdminResult> ExecuteAsync(ListarCardapioAdminCommand command)
    {
        var s = await storefrontRepository.GetByIdAsync(command.StorefrontId)
            ?? throw new StorefrontNaoEncontradoException();

        var items = await cardapioRepository.GetTodosDoStorefrontAsync(command.StorefrontId);
        var itens = items.Select(i => new CardapioItemAdminListItem(
            i.Id,
            i.ProdutoId,
            i.Produto?.Nome ?? "(produto removido)",
            i.OrdemExibicao,
            i.PrecoEfetivo(),
            i.PrecoStorefront,
            i.Visivel,
            i.Disponivel,
            i.Tag,
            i.FotoUrl,
            i.PesoExibicao)).ToList();

        return new ListarCardapioAdminResult(s.Id, s.Slug, s.TituloPublico, itens);
    }
}
