namespace EasyStock.Domain.Enums.Fiscal;

/// <summary>
/// Ciclo de vida de uma <see cref="EasyStock.Domain.Entities.Fiscal.NotaFiscalInutilizacao"/>.
/// Após autorizada ou rejeitada não há retorno — estados terminais.
/// </summary>
public enum StatusInutilizacao
{
    EmAndamento,
    Autorizada,
    Rejeitada,
}
