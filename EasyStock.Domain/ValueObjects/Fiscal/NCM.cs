namespace EasyStock.Domain.ValueObjects.Fiscal;

/// <summary>
/// Nomenclatura Comum do Mercosul (NCM): 8 dígitos numéricos obrigatórios
/// na NF-e/NFC-e conforme tabela TIPI. Strips de separadores no parse
/// (ex: "1905.90.20" → "19059020").
/// </summary>
public sealed record NCM
{
    public string Valor { get; }

    private NCM(string valor)
    {
        Valor = valor;
    }

    public static NCM Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("NCM é obrigatório.", nameof(raw));

        var s = new string(raw.Where(char.IsDigit).ToArray());
        if (s.Length != 8)
            throw new ArgumentException($"NCM deve ter 8 dígitos. Recebido: {s.Length}.", nameof(raw));

        return new NCM(s);
    }

    public override string ToString() => Valor;
}
