using System.Text.RegularExpressions;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #2249: preenche o molde de uma ação do robô (escalada, encaminhamento) com o primeiro nome. Sem nome, o
/// <c>{nome}</c> some com a vírgula e o espaço antes dele: "Oi, {nome}!" vira "Oi!".
/// </summary>
public static partial class MoldeMensagemContato
{
    public const string Nome = "{nome}";

    public static string Preencher(string molde, string? primeiroNome) =>
        string.IsNullOrWhiteSpace(primeiroNome)
            ? SemNome().Replace(molde, string.Empty)
            : molde.Replace(Nome, primeiroNome.Trim(), StringComparison.Ordinal);

    [GeneratedRegex(@",?[ \t]*\{nome\}")]
    private static partial Regex SemNome();
}
