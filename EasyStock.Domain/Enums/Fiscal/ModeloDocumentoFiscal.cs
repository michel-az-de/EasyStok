namespace EasyStock.Domain.Enums.Fiscal;

/// <summary>
/// Campo mod do XML de NF-e/NFC-e. <see cref="NFe"/> (55) para nota fiscal
/// eletrônica tradicional; <see cref="NFCe"/> (65) para nota fiscal ao
/// consumidor eletrônica (PDV/caixa).
/// </summary>
public enum ModeloDocumentoFiscal : short
{
    NFe = 55,
    NFCe = 65,
}
