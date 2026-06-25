namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Token JWT de acompanhamento de pedido guest (issue #680) ausente,
/// adulterado, expirado ou com scope incorreto. Mapeado para HTTP 404
/// (anti-enumeracao — guest sem token valido nao consegue listar pedidos).
/// </summary>
public sealed class AcompanhamentoTokenInvalidoException(string? detalhe = null)
    : Exception(detalhe ?? "Link de acompanhamento invalido ou expirado.");
