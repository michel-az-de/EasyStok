namespace EasyStock.Domain.Enums.Ia;

/// <summary>#2957: como terminou uma chamada de IA.</summary>
public enum DesfechoChamadaIa
{
    Ok = 1,
    Erro = 2,

    /// <summary>HTTP 402: o provedor está sem saldo.</summary>
    SemSaldo402 = 3,

    /// <summary>HTTP 429: limite de taxa do provedor.</summary>
    Limite429 = 4,

    /// <summary>O orçamento do mês da loja já foi gasto; a chamada nem saiu.</summary>
    CotaEstourada = 5,
}
