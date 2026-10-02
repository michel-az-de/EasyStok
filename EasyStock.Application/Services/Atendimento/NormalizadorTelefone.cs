using System.Text.RegularExpressions;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Normalização de telefone para E.164, compartilhada pelo OTP do storefront e pelo atendimento por
/// WhatsApp (S05). O E.164 normalizado é a entrada de <c>ClienteOtp.CalcularTelefoneHash</c>: os dois
/// fluxos precisam produzir a mesma string para o mesmo número, senão o hash não casa.
/// </summary>
public static class NormalizadorTelefone
{
    /// <summary>
    /// Celular BR sem o nono dígito, como a Meta ainda entrega o <c>wa_id</c> de números antigos:
    /// 55 + DDD + 8 dígitos começando em 6 a 9.
    /// </summary>
    private static readonly Regex CelularBrSemNonoDigitoRegex =
        new(@"^55([1-9][0-9])([6-9]\d{7})$", RegexOptions.Compiled);

    /// <summary>Celular BR com o nono dígito: 55 + DDD + 9 + 8 dígitos começando em 6 a 9.</summary>
    private static readonly Regex CelularBrComNonoDigitoRegex =
        new(@"^55([1-9][0-9])9([6-9]\d{7})$", RegexOptions.Compiled);

    /// <summary>
    /// Aceita <c>"+5511997573992"</c>, <c>"(11) 99757-3992"</c>, <c>"11 99757-3992"</c> e
    /// <c>"+55 11 9 9757 3992"</c> e <c>"5511997573992"</c>. Lança <see cref="TelefoneInvalidoException"/> quando o resultado
    /// não é E.164 BR.
    /// </summary>
    public static string NormalizarE164Br(string telefone) => TelefoneE164.From(telefone).Value;

    /// <summary>
    /// Números E.164 a procurar para um <c>wa_id</c> da Meta (dígitos, sem <c>+</c>), o canônico
    /// primeiro. Celular BR sem o nono dígito gera também a forma com o nono, que é a canônica
    /// (é como o cliente digita no cadastro e no OTP).
    /// </summary>
    public static IReadOnlyList<string> CandidatosDeWaId(string waId)
    {
        var digitos = new string((waId ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digitos.Length == 0)
            throw new TelefoneInvalidoException();

        var semNono = CelularBrSemNonoDigitoRegex.Match(digitos);
        if (semNono.Success)
            return [$"+55{semNono.Groups[1].Value}9{semNono.Groups[2].Value}", "+" + digitos];

        return ["+" + digitos];
    }

    /// <summary>
    /// Grafias do mesmo contato do WhatsApp (dígitos, sem <c>+</c>), a informada primeiro. Celular BR
    /// ganha a outra forma do nono dígito: a conversa guarda o <c>wa_id</c> como a Meta entrega
    /// (<c>551197573992</c>, sem o 9, em números antigos) e o cadastro guarda com ele
    /// (<c>5511997573992</c>). Qualquer outro número volta sozinho (#1290).
    /// </summary>
    public static IReadOnlyList<string> VariantesContatoWhatsApp(string contato)
    {
        var semNono = CelularBrSemNonoDigitoRegex.Match(contato);
        if (semNono.Success)
            return [contato, $"55{semNono.Groups[1].Value}9{semNono.Groups[2].Value}"];

        var comNono = CelularBrComNonoDigitoRegex.Match(contato);
        if (comNono.Success)
            return [contato, $"55{comNono.Groups[1].Value}{comNono.Groups[2].Value}"];

        return [contato];
    }
}
