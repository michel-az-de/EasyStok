namespace EasyStock.Domain.Fiscal;

/// <summary>
/// Regime tributario da empresa emitente. Determina como CST/CSOSN sao
/// preenchidos no XML da NFC-e: Simples Nacional usa CSOSN, demais usam CST.
/// Persistido como string (HasConversion) para legibilidade em consultas SQL.
/// </summary>
public enum RegimeTributario
{
    Simples = 1,
    MicroempreendedorIndividual = 2,
    LucroPresumido = 3,
    LucroReal = 4,
}
