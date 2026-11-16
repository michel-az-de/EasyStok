using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.Views;

// #2747: guardas estaticas do conteudo das views legadas. Nao ha harness de JavaScript neste
// projeto, entao estes testes provam a ausencia do padrao defeituoso e a presenca da correcao,
// nao o comportamento no navegador.
public class RazorRevisao2747GuardTests
{
    private static string LerView(string relativo)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EasyStock.Web", "Views", relativo)))
            dir = dir.Parent;
        dir.Should().NotBeNull($"a view {relativo} precisa existir na arvore do repositorio");
        return File.ReadAllText(Path.Combine(dir!.FullName, "EasyStock.Web", "Views", relativo));
    }

    private static string Trecho(string texto, string inicio, string fim)
    {
        var i = texto.IndexOf(inicio, StringComparison.Ordinal);
        i.Should().BeGreaterThanOrEqualTo(0, $"'{inicio}' precisa existir");
        var j = texto.IndexOf(fim, i, StringComparison.Ordinal);
        j.Should().BeGreaterThan(i, $"'{fim}' precisa vir depois de '{inicio}'");
        return texto[i..j];
    }

    // WR2-01
    [Fact]
    public void Imprimir_marcarImpressas_so_afirma_sucesso_com_resposta_ok()
    {
        var fn = Trecho(LerView("Lotes/Imprimir.cshtml"), "window.marcarImpressas", "window.baixarPdf");

        fn.Should().Contain("res.ok", "o status HTTP da resposta precisa ser conferido");
        fn.Should().Contain("catch", "a rejeicao do fetch precisa ser tratada");
        var posOk = fn.IndexOf("res.ok", StringComparison.Ordinal);
        var posSucesso = fn.IndexOf("marcadas como impressas", StringComparison.Ordinal);
        posSucesso.Should().BeGreaterThan(posOk, "o texto de sucesso so pode vir depois da checagem de res.ok");
    }

    // WR2-02
    [Fact]
    public void Modelos_nao_injeta_dado_no_onclick()
    {
        var fn = Trecho(LerView("Etiquetas/Modelos.cshtml"), "function renderLista", "async function renderThumb");

        fn.Should().NotMatchRegex(@"onclick=""(duplicar|excluir|definirPadrao)\(", "nome e id nao podem ir dentro de JS de atributo");
        fn.Should().Contain("data-nome");
        fn.Should().Contain("data-acao");
    }

    [Fact]
    public void Modelos_escapa_aspas_no_atributo_e_delega_o_clique()
    {
        var view = LerView("Etiquetas/Modelos.cshtml");

        view.Should().Contain("function _escAttr");
        var esc = Trecho(view, "function _escAttr", "async function init");
        esc.Should().Contain("&quot;").And.Contain("&#39;");
        view.Should().Contain("modelos-lista').addEventListener('click'");
    }

    // WR2-03
    [Fact]
    public void Relatorios_periodo_padrao_usa_o_dia_da_loja_e_nao_o_UTC()
    {
        var fn = Trecho(LerView("Relatorios/Index.cshtml"), "function reportForm", "paramsJson");

        fn.Should().NotContain("toISOString", "toISOString devolve a data UTC, que vira o dia seguinte apos as 21h em Sao Paulo");
        fn.Should().Contain("timeZone: 'America/Sao_Paulo'");
    }

    // WR2-04
    [Fact]
    public void Pedidos_minimo_do_agendamento_usa_a_hora_da_loja_e_nao_o_UTC()
    {
        var view = LerView("Pedidos/Index.cshtml");

        view.Should().NotMatchRegex(@":min=""[^""]*toISOString", "datetime-local interpreta o min como hora local");
        view.Should().Contain("agendamentoMin");
        Regex.Matches(view, "agendamentoMin").Count.Should().BeGreaterThanOrEqualTo(2, "definicao e uso no :min");
        Trecho(view, "get agendamentoMin", "},").Should().Contain("timeZone: 'America/Sao_Paulo'");
    }
}
