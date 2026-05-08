namespace EasyStock.Domain.ValueObjects.Fiscal;

/// <summary>
/// Representa CST (regime normal, 2-3 dígitos) ou CSOSN (Simples Nacional, 3-4 dígitos).
/// O comprimento determina o tipo automaticamente: ≥3 dígitos com primeiro caractere
/// indicando origem CSOSN.
/// </summary>
public sealed record CSTouCSOSN
{
    public string Valor { get; }

    public bool ESimplesNacional { get; }

    private CSTouCSOSN(string valor, bool csosn)
    {
        Valor = valor;
        ESimplesNacional = csosn;
    }

    public static CSTouCSOSN ParaSimples(string csosn)
    {
        if (string.IsNullOrWhiteSpace(csosn))
            throw new ArgumentException("CSOSN é obrigatório.", nameof(csosn));

        var s = new string(csosn.Where(char.IsDigit).ToArray());
        if (s.Length is < 3 or > 4)
            throw new ArgumentException($"CSOSN deve ter 3 ou 4 dígitos. Recebido: {s.Length}.", nameof(csosn));

        return new CSTouCSOSN(s, csosn: true);
    }

    public static CSTouCSOSN ParaRegimeNormal(string cst)
    {
        if (string.IsNullOrWhiteSpace(cst))
            throw new ArgumentException("CST é obrigatório.", nameof(cst));

        var s = new string(cst.Where(char.IsDigit).ToArray());
        if (s.Length is < 2 or > 3)
            throw new ArgumentException($"CST deve ter 2 ou 3 dígitos. Recebido: {s.Length}.", nameof(cst));

        return new CSTouCSOSN(s, csosn: false);
    }

    public static CSTouCSOSN Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Valor obrigatório.", nameof(raw));

        var s = new string(raw.Where(char.IsDigit).ToArray());
        return s.Length switch
        {
            2 or 3 when !PareceCsosn(s) => new CSTouCSOSN(s, csosn: false),
            3 or 4 => new CSTouCSOSN(s, csosn: true),
            _ => throw new ArgumentException($"Comprimento inválido para CST/CSOSN: {s.Length}.", nameof(raw)),
        };
    }

    // CSOSN começa com 1xx (100-102), 2xx (201-202), 4xx (400, 500) ou 5xx (500).
    // CST ICMS de 3 dígitos (ex: "010") começa com 0 — não colide.
    private static bool PareceCsosn(string s) => s.Length >= 3 && s[0] is '1' or '2' or '4' or '5';

    public override string ToString() => Valor;
}
