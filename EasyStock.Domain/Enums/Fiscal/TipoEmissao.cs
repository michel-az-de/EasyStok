namespace EasyStock.Domain.Enums.Fiscal;

/// <summary>
/// Campo tpEmis do XML da NF-e/NFC-e (layout 4.00). <see cref="Normal"/>
/// é o fluxo padrão online; <see cref="OfflineNFCe"/> (9) é ativado ao
/// entrar em contingência e alterado de volta após autorização pos-contingencia.
/// </summary>
public enum TipoEmissao : byte
{
    Normal = 1,
    Epec = 4,
    OfflineNFCe = 9,
}
