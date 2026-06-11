using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ListarCardapioAdmin;

// EmpresaId: null = SuperAdmin (acessa qualquer storefront); com valor = escopo do tenant
// (storefront de outra empresa → StorefrontNaoEncontradoException = 404, não vaza existência).
public sealed record ListarCardapioAdminCommand(Guid StorefrontId, Guid? EmpresaId = null) : ICommand;

public sealed record CardapioItemAdminListItem(
    Guid Id,
    Guid? ProdutoId,          // null = item avulso (sem vínculo com ERP)
    string NomeEfetivo,       // NomePublico ?? Produto.Nome
    double OrdemExibicao,
    decimal PrecoEfetivo,
    decimal? PrecoStorefrontOverride,
    bool Visivel,
    bool Disponivel,
    string? Tag,
    string? FotoUrl,
    string? PesoExibicao,
    string? CategoriaTexto);  // categoria de exibição (avulso ou override de vinculado)

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

        // Escopo de tenant (ADR-0031 §3): Admin só enxerga o próprio storefront.
        if (command.EmpresaId is Guid emp && s.EmpresaId != emp)
            throw new StorefrontNaoEncontradoException();

        var items = await cardapioRepository.GetTodosDoStorefrontAsync(command.StorefrontId);
        var itens = items.Select(i => new CardapioItemAdminListItem(
            i.Id,
            i.ProdutoId,
            i.NomeEfetivo() ?? "(sem nome)",
            i.OrdemExibicao,
            i.PrecoEfetivo(),
            i.PrecoStorefront,
            i.Visivel,
            i.Disponivel,
            i.Tag,
            i.FotoUrl,
            i.PesoExibicao,
            i.CategoriaEfetiva())).ToList();

        return new ListarCardapioAdminResult(s.Id, s.Slug, s.TituloPublico, itens);
    }
}
