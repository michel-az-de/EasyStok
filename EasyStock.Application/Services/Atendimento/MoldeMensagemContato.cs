using System.Text.RegularExpressions;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #2249: preenche o molde de uma ação do robô (escalada, encaminhamento) com o primeiro nome, sem diferenciar
/// maiúscula de <c>{nome}</c>. Sem nome, o marcador some com a pontuação ao redor: "Oi, {nome}!" vira "Oi!" e
/// "{nome}, vou passar" vira "Vou passar".
/// </summary>
public static partial class MoldeMensagemContato
{
    public const string Nome = "{nome}";

    public static string Preencher(string molde, string? primeiroNome)
    {
        if (!string.IsNullOrWhiteSpace(primeiroNome))
            return Marcador().Replace(molde, primeiroNome.Trim());

        var semNome = InicioSolto().Replace(SemNome().Replace(molde, string.Empty), string.Empty);
        return semNome.Length == 0 ? semNome : char.ToUpperInvariant(semNome[0]) + semNome[1..];
    }

    [GeneratedRegex(@"\{nome\}", RegexOptions.IgnoreCase)]
    private static partial Regex Marcador();

    [GeneratedRegex(@",?[ \t]*\{nome\}", RegexOptions.IgnoreCase)]
    private static partial Regex SemNome();

    [GeneratedRegex(@"^[\s,]+")]
    private static partial Regex InicioSolto();
}
