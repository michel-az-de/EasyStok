namespace EasyStock.Application.UseCases.Caixa;

/// <summary>
/// Dinheiro físico da gaveta num dia de caixa (#1474, #1521). O <c>SaldoEsperado</c> soma tudo que
/// entrou, Pix e cartão também; a gaveta é só dinheiro. É a MESMA conta do console
/// (<c>gavetaDoDia</c> em <c>EasyStock.Console/src/dominio/caixa.js</c>), para a tela e a validação
/// do servidor não divergirem:
///   saldo inicial + entradas em dinheiro − saídas em dinheiro + vendas e pagamentos em dinheiro.
/// Lançamento estornado não conta. Movimento sem método é do caixa físico (nasceu antes de o método
/// existir); venda ou pagamento sem método fica fora, porque ninguém sabe onde caiu.
/// </summary>
public static class GavetaCaixa
{
    public const string Dinheiro = "dinheiro";

    public static bool EhDinheiro(string? metodo) =>
        string.Equals(metodo?.Trim(), Dinheiro, StringComparison.OrdinalIgnoreCase);

    public static decimal DinheiroNaGaveta(CaixaDiaResult dia)
    {
        ArgumentNullException.ThrowIfNull(dia);
        var gaveta = dia.SaldoInicial;

        foreach (var m in dia.Movimentos)
        {
            if (m.EstornadoEm is not null) continue;
            if (!string.IsNullOrWhiteSpace(m.Metodo) && !EhDinheiro(m.Metodo)) continue;
            if (m.Tipo == "entrada") gaveta += m.Valor;
            else if (m.Tipo == "saida") gaveta -= m.Valor;
        }

        foreach (var linha in dia.LinhasExtras ?? [])
            if (EhDinheiro(linha.Metodo)) gaveta += linha.Valor;

        return gaveta;
    }

    /// <summary>
    /// Teto de uma saída em dinheiro (#1521): a gaveta mais as vendas e pagamentos SEM método, que
    /// podem ser dinheiro (legado ou PWA antigo). Só o que se sabe que é Pix ou cartão fica fora, para
    /// a validação barrar o impossível sem travar uma sangria legítima.
    /// </summary>
    public static decimal TetoSaidaEmDinheiro(CaixaDiaResult dia) =>
        DinheiroNaGaveta(dia) + (dia.LinhasExtras ?? []).Where(l => string.IsNullOrWhiteSpace(l.Metodo)).Sum(l => l.Valor);
}
