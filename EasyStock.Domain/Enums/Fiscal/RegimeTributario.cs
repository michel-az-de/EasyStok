namespace EasyStock.Domain.Enums.Fiscal;

/// <summary>
/// Campo CRT (Código de Regime Tributário) do emitente na NF-e/NFC-e.
/// Determina qual tabela CST (Simples vs regime normal) é aplicável
/// nos itens.
/// </summary>
public enum RegimeTributario : byte
{
    SimplesNacional = 1,
    SimplesNacionalExcessoSubLimite = 2,
    RegimeNormal = 3,
    SimplesNacionalMEI = 4,
}
