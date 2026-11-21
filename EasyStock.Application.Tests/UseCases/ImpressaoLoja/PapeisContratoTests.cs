using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EasyStock.Application.UseCases.ImpressaoLoja;

namespace EasyStock.Application.Tests.UseCases.ImpressaoLoja;

/// <summary>
/// Catálogo único de papéis (#2247, impressão v3, G1 item 6). O contrato <c>docs/contracts/papeis.v1.json</c> é gerado do
/// catálogo do app (<c>pwa/impressao-papeis.js</c>); aqui o servidor e a folha do bilhete provam que dizem o mesmo.
/// </summary>
public class PapeisContratoTests
{
    private static readonly Lazy<JsonNode> Contrato = new(() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(RaizDoRepo(), "docs", "contracts", "papeis.v1.json")))!);

    private static readonly Lazy<string> Bilhete = new(() =>
        File.ReadAllText(Path.Combine(RaizDoRepo(), "EasyStock.Api", "Data", "Templates", "Impressao", "bilhete.css")));

    private static IEnumerable<JsonNode> Loja => Contrato.Value["loja"]!.AsArray().Select(p => p!);

    [Fact]
    public void Papeis_da_regra_da_loja_sao_os_do_contrato_na_mesma_ordem()
    {
        RegraImpressaoCatalogo.Papeis.Should().Equal(Loja.Select(p => p["id"]!.GetValue<string>()));
    }

    [Fact]
    public void Papel_da_loja_aponta_para_um_papel_do_aparelho_que_existe()
    {
        var aparelho = Contrato.Value["aparelho"]!.AsArray().Select(p => p!["id"]!.GetValue<string>()).ToHashSet();

        foreach (var papel in Loja.Where(p => p["aparelho"] is not null))
            aparelho.Should().Contain(papel["aparelho"]!.GetValue<string>(), papel["id"]!.GetValue<string>());
    }

    [Fact]
    public void Folha_do_bilhete_tem_a_largura_e_a_altura_do_papel_e_cabe_na_area_util()
    {
        var comFolha = Loja.Where(p => p["folha"] is not null).ToList();
        comFolha.Should().NotBeEmpty();

        foreach (var papel in comFolha)
        {
            var id = papel["id"]!.GetValue<string>();
            var regra = RegraDaFolha(papel["folha"]!.GetValue<string>());
            var largura = papel["larguraMm"]!.GetValue<decimal>();

            Mm(regra, "--w").Should().Be(largura, $"{id}: --w da folha");
            if (papel["alturaMm"] is { } altura)
                Mm(regra, "min-height").Should().Be(altura.GetValue<decimal>(), $"{id}: min-height da folha");

            var conteudo = largura - 2 * MargemLateral(regra);
            conteudo.Should().BeLessThanOrEqualTo(papel["utilMm"]!.GetValue<decimal>(), $"{id}: o texto não pode sair da área que a impressora imprime");
        }
    }

    private static string RegraDaFolha(string classe)
    {
        var m = Regex.Match(Bilhete.Value, $@"\.im\.{Regex.Escape(classe)}\{{([^}}]*)\}}");
        m.Success.Should().BeTrue($"bilhete.css sem .im.{classe}");
        return m.Groups[1].Value;
    }

    private static decimal Mm(string regra, string propriedade)
    {
        var m = Regex.Match(regra, $@"(?:^|;){Regex.Escape(propriedade)}:([\d.]+)mm");
        m.Success.Should().BeTrue($"sem {propriedade} em mm");
        return decimal.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>Lado do atalho <c>--mg</c> do CSS: 1 valor vale para todos; com 2 ou mais, o segundo é o lateral.</summary>
    private static decimal MargemLateral(string regra)
    {
        var m = Regex.Match(regra, @"(?:^|;)--mg:([^;]+)");
        m.Success.Should().BeTrue("sem --mg");
        var lados = m.Groups[1].Value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return decimal.Parse((lados.Length == 1 ? lados[0] : lados[1]).Replace("mm", ""), CultureInfo.InvariantCulture);
    }

    private static string RaizDoRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EasyStok.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }
}
