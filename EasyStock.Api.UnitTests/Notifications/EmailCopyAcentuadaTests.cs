using System.Text.RegularExpressions;
using EasyStock.Api.Data;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// #2850: o e-mail que sai para a pessoa é escrito em português com acento, e o assunto leva a marca do
/// produto como ela aparece no cabeçalho ("EasyStok"). Lê o que o seed publica, não o arquivo solto, porque
/// é o seed que decide qual versão chega ao banco.
/// </summary>
public class EmailCopyAcentuadaTests
{
    private static readonly Regex SemAcento = new(
        @"\b(Ola|voce|Voce|nao|Nao|disponivel|tecnicos|atribuido|solicitacao|acao|proximo|Nivel|Redefinicao|Confirmacao|esta disponivel|esta em atraso|esta a)\b",
        RegexOptions.Compiled);

    private static readonly Regex Tag = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Variavel = new(@"\{\{.*?\}\}", RegexOptions.Compiled | RegexOptions.Singleline);

    private static IEnumerable<(string Codigo, string Texto)> TextosDeEmail() =>
        NotificacoesGlobaisSeed.BuildDefaultTemplates()
            .Where(t => t.Canal == CanalNotificacao.Email)
            .Select(t => (t.Codigo, Texto: t.AssuntoTemplate + " " + Tag.Replace(Variavel.Replace(t.CorpoTemplate, " "), " ")));

    [Fact]
    public void Email_SaiComAcento()
    {
        var achados = TextosDeEmail()
            .SelectMany(e => SemAcento.Matches(e.Texto).Select(m => $"{e.Codigo}: \"{m.Value}\""))
            .ToList();

        achados.Should().BeEmpty("e-mail para a pessoa sai em português com acento (#2850)");
    }

    [Fact]
    public void AssuntoDoEmail_UsaAMarcaDoCabecalho()
    {
        var achados = NotificacoesGlobaisSeed.BuildDefaultTemplates()
            .Where(t => t.Canal == CanalNotificacao.Email && t.AssuntoTemplate.Contains("EasyStock", StringComparison.Ordinal))
            .Select(t => t.Codigo)
            .ToList();

        achados.Should().BeEmpty("o assunto escreve EasyStok, como o cabeçalho do e-mail (#2850)");
    }
}
