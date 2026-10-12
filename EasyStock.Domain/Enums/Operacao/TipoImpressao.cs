namespace EasyStock.Domain.Enums.Operacao;

/// <summary>O que a <see cref="Entities.Operacao.ImpressaoPendente"/> imprime (S20). Persistido como int.</summary>
public enum TipoImpressao
{
    /// <summary>Canhoto de produção do pedido pago: basta para produzir sem sistema.</summary>
    Canhoto = 1,
}
