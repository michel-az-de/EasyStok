namespace EasyStock.Domain.Enums.Operacao;

/// <summary>Situação de uma <see cref="Entities.Operacao.ImpressaoPendente"/> (S20). Persistido como int.</summary>
public enum StatusImpressao
{
    /// <summary>Na fila, esperando o consumidor (bridge, impressora com polling ou aba do console).</summary>
    Pendente = 1,

    /// <summary>O consumidor confirmou que o papel saiu.</summary>
    Impressa = 2,

    /// <summary>O consumidor desistiu (sem papel, impressora fora); a dona reimprime pelo console.</summary>
    Falhou = 3,
}
