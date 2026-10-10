using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ToggleDisponibilidadeCardapioItemAdmin;
using EasyStock.Application.UseCases.Inventario.Desacertos;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Comanda;

public sealed record DefinirDisponibilidadeItemInput(Guid EmpresaId, Guid CardapioItemId, bool Disponivel);

public sealed record DisponibilidadeItemResult(Guid CardapioItemId, bool Disponivel);

public sealed record AjustarSaldoItemInput(
    Guid EmpresaId, Guid UsuarioId, Guid CardapioItemId, Guid? LojaId, decimal QuantidadeContada, string Motivo);

/// <summary>
/// Cardápio do dia pelo console (#1241, F11, S45/S17). O operador do balcão liga e desliga o item
/// hoje e ajusta o saldo contado, pelo <c>cardapio_item_id</c> que a comanda conhece. O produto do
/// saldo é resolvido aqui, na vitrine da empresa logada: o console nunca recebe nem manda produtoId.
/// <para>
/// Disponibilidade é por valor (idempotente): o mesmo clique repetido não inverte de volta. Reusa o
/// toggle da vitrine para manter o aviso a quem esperava o item. Saldo reusa o ajuste rápido (S22).
/// </para>
/// </summary>
public sealed class CardapioDoDiaComandaUseCase(
    IStorefrontRepository storefrontRepository,
    ICardapioItemRepository cardapioRepository,
    ToggleDisponibilidadeCardapioItemAdminUseCase toggleDisponivel,
    AjustarSaldoRapidoUseCase ajustarSaldo)
{
    public async Task<DisponibilidadeItemResult> DefinirDisponibilidadeAsync(
        DefinirDisponibilidadeItemInput input, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        var (storefrontId, item) = await ItemDaVitrineAsync(input.EmpresaId, input.CardapioItemId, ct);
        if (item.Disponivel == input.Disponivel)
            return new DisponibilidadeItemResult(item.Id, item.Disponivel);

        var r = await toggleDisponivel.ExecuteAsync(
            new ToggleDisponibilidadeCardapioItemAdminCommand(storefrontId, item.Id, input.EmpresaId));
        return new DisponibilidadeItemResult(r.ItemId, r.DisponivelAgora);
    }

    public async Task<AjustarSaldoRapidoResult> AjustarSaldoAsync(AjustarSaldoItemInput input, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(input.EmpresaId);
        var (_, item) = await ItemDaVitrineAsync(input.EmpresaId, input.CardapioItemId, ct);
        if (item.ProdutoId is not { } produtoId)
            throw new UseCaseValidationException("Este item não controla saldo: não está ligado a um produto do estoque.");

        return await ajustarSaldo.ExecuteAsync(new AjustarSaldoRapidoInput(
            input.EmpresaId, input.UsuarioId, produtoId, input.LojaId, input.QuantidadeContada, input.Motivo), ct);
    }

    private async Task<(Guid StorefrontId, Domain.Entities.Storefront.CardapioItem Item)> ItemDaVitrineAsync(
        Guid empresaId, Guid itemId, CancellationToken ct)
    {
        var storefront = await storefrontRepository.GetByEmpresaAsync(empresaId, ct);
        if (storefront is null || !storefront.Ativo)
            throw new StorefrontNaoEncontradoException();
        var item = await cardapioRepository.GetByIdAndScopeAsync(storefront.Id, itemId, empresaId, ct)
            ?? throw new CardapioItemNaoEncontradoException(storefront.Id, itemId);
        return (storefront.Id, item);
    }
}
