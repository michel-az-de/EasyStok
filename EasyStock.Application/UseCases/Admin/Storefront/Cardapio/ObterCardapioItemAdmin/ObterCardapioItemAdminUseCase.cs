using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ObterCardapioItemAdmin;

// EmpresaId: null = SuperAdmin (qualquer storefront); com valor = escopo do tenant
// (item de outra empresa → CardapioItemNaoEncontradoException = 404, não vaza existência).
public sealed record ObterCardapioItemAdminCommand(Guid StorefrontId, Guid ItemId, Guid? EmpresaId = null) : ICommand;

/// <summary>
/// Detalhe COMPLETO de um item para a tela de edição. Diferente do
/// <c>CardapioItemAdminListItem</c> (listagem enxuta, compartilhado com o Admin),
/// este record carrega também os campos de "detalhes para o cliente" — por isso
/// existe um GET-by-id dedicado: o prefill da edição não depende da projeção de
/// listagem (sem acoplamento, 404 natural; ADR-0031 §3).
/// </summary>
/// <summary>Opção do item guarda-chuva para o prefill da edição (ADR-0035).</summary>
public sealed record CardapioItemVariacaoAdmin(
    Guid Id,
    string Rotulo,
    decimal PrecoStorefront,
    bool Disponivel,
    bool EhPadrao,
    string? PesoExibicao,
    string? Sku,
    double OrdemExibicao);

public sealed record CardapioItemDetalheAdmin(
    Guid Id,
    Guid? ProdutoId,
    string? NomePublico,
    string NomeEfetivo,
    double OrdemExibicao,
    decimal PrecoEfetivo,
    decimal? PrecoStorefront,
    bool Visivel,
    bool Disponivel,
    string? Tag,
    string? FotoUrl,
    string? PesoExibicao,
    string? CategoriaTexto,
    string? DescricaoPublica,
    string? Ingredientes,
    string? Alergenos,
    string? SugestaoMolho,
    string? TempoPreparo,
    string FiltrosJson,
    Guid? SecaoId,                                     // ADR-0035: seção do item
    IReadOnlyList<CardapioItemVariacaoAdmin> Opcoes);  // ADR-0035: opções (vazio = preço único)

public class ObterCardapioItemAdminUseCase(ICardapioItemRepository cardapioRepository)
    : IUseCase<ObterCardapioItemAdminCommand, CardapioItemDetalheAdmin>
{
    public async Task<CardapioItemDetalheAdmin> ExecuteAsync(ObterCardapioItemAdminCommand command)
    {
        var item = await cardapioRepository.GetByIdAndScopeAsync(command.StorefrontId, command.ItemId, command.EmpresaId)
            ?? throw new CardapioItemNaoEncontradoException(command.StorefrontId, command.ItemId);

        return new CardapioItemDetalheAdmin(
            item.Id,
            item.ProdutoId,
            item.NomePublico,
            item.NomeEfetivo() ?? "(sem nome)",
            item.OrdemExibicao,
            item.PrecoEfetivo(),
            item.PrecoStorefront,
            item.Visivel,
            item.Disponivel,
            item.Tag,
            item.FotoUrl,
            item.PesoExibicao,
            item.CategoriaTexto,
            item.DescricaoPublica,
            item.Ingredientes,
            item.Alergenos,
            item.SugestaoMolho,
            item.TempoPreparo,
            item.FiltrosJson,
            item.SecaoId,
            item.Variacoes
                .OrderBy(v => v.OrdemExibicao).ThenBy(v => v.Id)
                .Select(v => new CardapioItemVariacaoAdmin(
                    v.Id, v.Rotulo, v.PrecoStorefront, v.Disponivel, v.EhPadrao,
                    v.PesoExibicao, v.Sku?.Value, v.OrdemExibicao))
                .ToList());
    }
}
