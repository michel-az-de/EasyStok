using System.Text.RegularExpressions;

namespace EasyStock.Domain.ValueObjects;

/// <summary>
/// CPF normalizado (apenas dígitos) com validação de dígito verificador.
///
/// <para>
/// Difere do <see cref="Cnpj"/> — que valida apenas o formato (11 ou 14 dígitos) porque o
/// campo <c>Documento</c> é genérico e tolera estrangeiro/legado. Aqui o algoritmo de
/// dígito verificador (módulo 11) é aplicado, e sequências de dígitos repetidos
/// (ex.: <c>"00000000000"</c>, <c>"11111111111"</c>) são rejeitadas — passam na conta mas
/// não são CPFs válidos.
/// </para>
///
/// <para>
/// Use quando o documento "tem forma de CPF" (11 dígitos). Documentos de outros comprimentos
/// (CNPJ, passaporte estrangeiro, legado) ficam fora deste VO por decisão de tolerância
/// (ver <c>DocumentoValidator</c> / <c>CriarClienteUseCase</c>).
/// </para>
/// </summary>
public sealed record Cpf
{
    public string Value { get; }

    private Cpf(string value) => Value = value;

    /// <summary>Cria um <see cref="Cpf"/> validado (dígito verificador) a partir do documento.</summary>
    public static Cpf From(string documento)
    {
        if (string.IsNullOrWhiteSpace(documento))
            throw new ArgumentException("CPF não pode ser vazio.", nameof(documento));

        var digits = SomenteDigitos(documento);
        if (!EhValido(digits))
            throw new ArgumentException(
                $"CPF '{documento}' inválido (dígito verificador).", nameof(documento));

        return new Cpf(digits);
    }

    /// <summary>Versão não-lançante: retorna null quando vazio ou inválido.</summary>
    public static Cpf? TryFrom(string? documento)
    {
        if (string.IsNullOrWhiteSpace(documento)) return null;
        try { return From(documento); }
        catch { return null; }
    }

    /// <summary>
    /// True se o documento for um CPF válido: 11 dígitos, não todos iguais, e ambos os
    /// dígitos verificadores conferem. Aceita entrada com ou sem máscara.
    /// </summary>
    public static bool EhValido(string? documento)
    {
        var d = SomenteDigitos(documento);
        if (d.Length != 11) return false;
        if (TodosIguais(d)) return false;

        return d[9] == DigitoVerificador(d, 9, 10)
            && d[10] == DigitoVerificador(d, 10, 11);
    }

    /// <summary>True quando o documento, só dígitos, tem 11 caracteres (forma de CPF).</summary>
    public static bool TemFormaDeCpf(string? documento) => SomenteDigitos(documento).Length == 11;

    /// <summary>Formata para exibição: 000.000.000-00.</summary>
    public string Formatado() => $"{Value[..3]}.{Value[3..6]}.{Value[6..9]}-{Value[9..]}";

    public static implicit operator string(Cpf c) => c.Value;

    public override string ToString() => Value;

    private static string SomenteDigitos(string? s) =>
        string.IsNullOrEmpty(s) ? string.Empty : Regex.Replace(s, @"\D", "");

    private static bool TodosIguais(string d)
    {
        for (var i = 1; i < d.Length; i++)
            if (d[i] != d[0]) return false;
        return true;
    }

    /// <summary>
    /// Dígito verificador (módulo 11) sobre os primeiros <paramref name="ate"/> dígitos,
    /// com peso decrescente a partir de <paramref name="pesoInicial"/>. Resto &lt; 2 → 0.
    /// </summary>
    private static char DigitoVerificador(string d, int ate, int pesoInicial)
    {
        var soma = 0;
        var peso = pesoInicial;
        for (var i = 0; i < ate; i++)
            soma += (d[i] - '0') * peso--;
        var resto = soma % 11;
        var dv = resto < 2 ? 0 : 11 - resto;
        return (char)('0' + dv);
    }
}
