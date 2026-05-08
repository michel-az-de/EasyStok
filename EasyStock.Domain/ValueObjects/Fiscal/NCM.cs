using System;
using System.Linq;

namespace EasyStock.Domain.ValueObjects.Fiscal;

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
