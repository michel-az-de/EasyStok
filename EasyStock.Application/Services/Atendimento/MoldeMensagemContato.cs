using System.Text.RegularExpressions;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #2249: preenche o molde de uma ação do robô (escalada, encaminhamento) com o primeiro nome, sem diferenciar
/// maiúscula de <c>{nome}</c>. Sem nome, o marcador some com a pontuação ao redor: "Oi, {nome}!" vira "Oi!" e
/// "{nome}, vou passar" vira "Vou passar". S68 (#2597): <c>{dona}</c> vira o nome da dona ou, sem ele, "a equipe".
/// </summary>
public static partial class MoldeMensagemContato
{
    public const string Nome = "{nome}";
    public const string Dona = "{dona}";
    public const string DonaSemNome = "a equipe";

    public static string Preencher(string molde, string? primeiroNome, string? nomeDona = null)
    {
        var dona = string.IsNullOrWhiteSpace(nomeDona) ? DonaSemNome : nomeDona.Trim();
        var comDona = MarcadorDona().Replace(molde, dona.Replace("$", "$$", StringComparison.Ordinal));
        var preenchido = string.IsNullOrWhiteSpace(primeiroNome)
            ? InicioSolto().Replace(SemNome().Replace(comDona, string.Empty), string.Empty)
            : Marcador().Replace(comDona, primeiroNome.Trim().Replace("$", "$$", StringComparison.Ordinal));
        return preenchido.Length == 0 ? preenchido : char.ToUpperInvariant(preenchido[0]) + preenchido[1..];
    }

    [GeneratedRegex(@"\{dona\}", RegexOptions.IgnoreCase)]
    private static partial Regex MarcadorDona();

    [GeneratedRegex(@"\{nome\}", RegexOptions.IgnoreCase)]
    private static partial Regex Marcador();

    [GeneratedRegex(@",?[ \t]*\{nome\}", RegexOptions.IgnoreCase)]
    private static partial Regex SemNome();

    [GeneratedRegex(@"^[\s,]+")]
    private static partial Regex InicioSolto();
}
