namespace EasyStock.Domain.Enums.Pagamentos;

/// <summary>
/// Situação de uma <see cref="Entities.Pagamentos.CobrancaPedido"/> (S11). Persistido como int.
/// </summary>
public enum StatusCobrancaPedido
{
    /// <summary>Link emitido (ou pagamento na entrega combinado), aguardando o dinheiro.</summary>
    Pendente = 1,

    /// <summary>Pagamento confirmado pelo provedor.</summary>
    Paga = 2,

    /// <summary>O link venceu sem pagamento.</summary>
    Expirada = 3,

    /// <summary>Substituída por outra cobrança (troca de forma, pagamento por outra cobrança).</summary>
    Cancelada = 4,

    /// <summary>Paga e depois estornada (S27/S32).</summary>
    Estornada = 5,
}
