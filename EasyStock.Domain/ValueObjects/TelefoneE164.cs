using System.Text;
using System.Text.RegularExpressions;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Domain.ValueObjects;

/// <summary>
/// Telefone estrito em E.164 do Brasil: <c>+55</c> + DDD (2 dígitos) + número (8 ou 9 dígitos), no máximo 14
/// caracteres. É a única regra de E.164 BR do sistema (N4): o <c>NormalizadorTelefone</c> delega para cá. Aceita a
/// entrada comum (<c>(11) 99757-3992</c>, <c>5511997573992</c>) e guarda sempre a forma canônica com <c>+</c>. O
/// <see cref="Telefone"/> frouxo (de 7 a 15 dígitos) não muda.
/// </summary>
public sealed record TelefoneE164
{
    /// <summary>Tamanho máximo da coluna: <c>+55</c> + DDD + 9 dígitos são 14 caracteres, e a coluna tem folga.</summary>
    public const int TamanhoMaximo = 16;

    private static readonly Regex E164Br = new(@"^\+55[1-9][0-9]\d{8,9}$", RegexOptions.Compiled);

    public string Value { get; }

    private TelefoneE164(string value) => Value = value;

    /// <summary>Normaliza e valida. Lança <see cref="TelefoneInvalidoException"/> quando não é E.164 BR.</summary>
    public static TelefoneE164 From(string? telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone))
            throw new TelefoneInvalidoException();

        // Mantém apenas dígitos e o '+' inicial. Espaços, hífens, parênteses e pontos somem.
        var span = telefone.Trim();
        var digitos = new StringBuilder(span.Length);
        var primeiro = true;
        foreach (var c in span)
        {
            if (primeiro && c == '+')
                digitos.Append('+');
            else if (char.IsDigit(c))
                digitos.Append(c);
            else if (c is not (' ' or '(' or ')' or '-' or '.'))
                throw new TelefoneInvalidoException();
            primeiro = false;
        }

        var normalizado = digitos.ToString();

        // Sem prefixo: DDD + número BR puro (10 ou 11 dígitos) ganha o +55; "55" + DDD + número (12 ou 13 dígitos,
        // como o VO Telefone grava o que foi digitado sem '+') ganha só o '+'. Os comprimentos não se cruzam:
        // número nacional nunca passa de 11 dígitos (#1290).
        if (!normalizado.StartsWith('+'))
        {
            if (normalizado.Length is 10 or 11)
                normalizado = "+55" + normalizado;
            else if (normalizado.Length is 12 or 13 && normalizado.StartsWith("55", StringComparison.Ordinal))
                normalizado = "+" + normalizado;
            else
                throw new TelefoneInvalidoException();
        }

        if (!E164Br.IsMatch(normalizado))
            throw new TelefoneInvalidoException();

        return new TelefoneE164(normalizado);
    }

    public static TelefoneE164? TryFrom(string? telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone)) return null;
        try { return From(telefone); }
        catch (TelefoneInvalidoException) { return null; }
    }

    public static implicit operator string(TelefoneE164 t) => t.Value;

    public override string ToString() => Value;
}
