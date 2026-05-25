namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Token JWT de avaliação ausente, adulterado, expirado ou com scope incorreto.
/// Mapeado para HTTP 410 Gone (link de avaliação expirou).
/// </summary>
public sealed class AvaliacaoTokenInvalidoException(string? detalhe = null)
    : Exception(detalhe ?? "Link de avaliação inválido ou expirado.");
