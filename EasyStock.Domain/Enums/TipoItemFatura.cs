namespace EasyStock.Domain.Enums;

/// <summary>
/// Tipo do <see cref="Entities.FaturaItem"/>. Permite renderer agrupar/destacar
/// (ex: descontos com sinal negativo, taxas em secao separada) e regras
/// fiscais futuras (servico → ISS, produto → ICMS).
/// </summary>
public enum TipoItemFatura
{
    Produto,
    Servico,
    /// <summary>Item recorrente (assinatura mensal, anuidade).</summary>
    Recorrencia,
    /// <summary>Linha de desconto — Subtotal deve ser negativo.</summary>
    Desconto,
    /// <summary>Linha de taxa/acrescimo — Subtotal positivo.</summary>
    Taxa
}
