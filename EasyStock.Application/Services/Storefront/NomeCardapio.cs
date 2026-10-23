using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Services.Storefront;

/// <summary>
/// Nome do item do cardápio como o cliente vê na vitrine. Saiu de <c>ListarCardapioPublicoUseCase</c> para o
/// resumo do pedido na conversa mostrar o item igual ao cardápio (#1474). A regra mora no domínio
/// (<see cref="NomeDeExibicao"/>, #1991-5), de onde o <see cref="CardapioItem.NomeEfetivo"/> também a usa.
/// </summary>
public static class NomeCardapio
{
    /// <inheritdoc cref="NomeDeExibicao.De"/>
    public static string? Exibicao(string? s) => NomeDeExibicao.De(s);
}
