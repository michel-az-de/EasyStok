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

    public static decimal DinheiroNaGaveta(CaixaDiaResult dia) => DinheiroNaJanela(dia, null, null, comSaldoInicial: true);

    /// <summary>
    /// #2084: a mesma conta, só com o que aconteceu em [<paramref name="desdeUtc"/>, <paramref name="ateUtc"/>).
    /// Separa, numa sessão esquecida, o dia da abertura do que entrou hoje (D1), e no dia fechado o que entrou
    /// depois de fechar (B4). O saldo inicial é da abertura e entra só quando pedido.
    /// </summary>
    public static decimal DinheiroNaJanela(CaixaDiaResult dia, DateTime? desdeUtc, DateTime? ateUtc, bool comSaldoInicial)
    {
        ArgumentNullException.ThrowIfNull(dia);
        bool NaJanela(DateTime t) => (desdeUtc is null || t >= desdeUtc) && (ateUtc is null || t < ateUtc);
        var gaveta = comSaldoInicial ? dia.SaldoInicial : 0m;

        foreach (var m in dia.Movimentos)
        {
            if (m.EstornadoEm is not null || !NaJanela(m.DataMovimento)) continue;
            if (!string.IsNullOrWhiteSpace(m.Metodo) && !EhDinheiro(m.Metodo)) continue;
            if (m.Tipo == "entrada") gaveta += m.Valor;
            else if (m.Tipo == "saida") gaveta -= m.Valor;
        }

        foreach (var linha in dia.LinhasExtras ?? [])
            if (EhDinheiro(linha.Metodo) && NaJanela(linha.Hora)) gaveta += linha.Valor;

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
