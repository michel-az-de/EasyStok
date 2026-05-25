namespace EasyStock.Domain.Enums;

/// <summary>
/// Origem da <see cref="Entities.Fatura"/> — identifica o agregado que motivou
/// a emissao. <see cref="Entities.Fatura.OrigemRefId"/> aponta para a entidade
/// correspondente.
/// </summary>
public enum OrigemFatura
{
    /// <summary>Auto-gerada pelo CobrancaAssinaturaJob — OrigemRefId = AssinaturaEmpresa.Id.</summary>
    Assinatura,

    /// <summary>Auto-gerada a partir de Pedido do ERP — OrigemRefId = Pedido.Id.</summary>
    Pedido,

    /// <summary>Emitida manualmente pelo admin — OrigemRefId = null.</summary>
    Avulsa
}
