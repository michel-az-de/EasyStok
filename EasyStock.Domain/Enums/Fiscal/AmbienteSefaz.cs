namespace EasyStock.Domain.Enums.Fiscal;

/// <summary>
/// Campo tpAmb do XML da NF-e/NFC-e. <see cref="Producao"/> (1) emite
/// documentos com valor fiscal real; <see cref="Homologacao"/> (2) é
/// ambiente de testes — notas não têm valor legal.
/// </summary>
public enum AmbienteSefaz : byte
{
    Producao = 1,
    Homologacao = 2,
}
