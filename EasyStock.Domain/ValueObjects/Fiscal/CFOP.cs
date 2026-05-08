using System;
using System.Linq;

namespace EasyStock.Domain.ValueObjects.Fiscal;

public sealed record CFOP
{
    public string Valor { get; }

    public bool DentroDoEstado => Valor[0] == '5';
    public bool ForaDoEstado => Valor[0] == '6';
    public bool ParaExterior => Valor[0] == '7';

    private CFOP(string valor)
    {
        Valor = valor;
    }

    public static CFOP Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("CFOP é obrigatório.", nameof(raw));

        var s = new string(raw.Where(char.IsDigit).ToArray());
        if (s.Length != 4)
            throw new ArgumentException($"CFOP deve ter 4 dígitos. Recebido: {s.Length}.", nameof(raw));

        if (s[0] != '5' && s[0] != '6' && s[0] != '7')
            throw new ArgumentException($"CFOP de saída deve começar com 5, 6 ou 7. Recebido: {s[0]}.", nameof(raw));

        return new CFOP(s);
    }

    public static CFOP VendaIntraEstado() => new("5102");
    public static CFOP VendaInterEstado() => new("6102");

    public override string ToString() => Valor;
}
