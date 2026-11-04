using System.Text.Json;
using System.Text.Json.Nodes;
using EasyStock.Application.UseCases.ImpressaoLoja;
using EasyStock.Application.Validators;

namespace EasyStock.Application.Tests.UseCases.ImpressaoLoja;

/// <summary>
/// Contrato da regra de impressão da loja (#2199). A fixture fica em <c>docs/contracts</c> para o normalizador do cliente
/// (JS) provar a mesma saída: é a idempotência entre sistemas pedida na auditoria 3, seção 8.8.
/// </summary>
public class RegraImpressaoContratoTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly RegraImpressaoLojaValidator Validator = new();
    private static readonly Lazy<JsonNode> FixtureLida = new(() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(RaizDoRepo(), "docs", "contracts", "impressao-loja.v1.fixture.json")))!);

    public static TheoryData<string> CasosValidos() => Nomes("normalizacao");
    public static TheoryData<string> CasosInvalidos() => Nomes("invalidos");

    [Theory]
    [MemberData(nameof(CasosValidos))]
    public void Normaliza_a_entrada_na_saida_da_fixture(string nome)
    {
        var caso = Caso("normalizacao", nome);
        var entrada = Ler(caso["entrada"]!);

        var validacao = Validator.Validate(entrada);
        validacao.IsValid.Should().BeTrue(string.Join("; ", validacao.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}")));
        var json = RegraImpressaoNormalizador.ParaJson(RegraImpressaoNormalizador.Normalizar(entrada));

        JsonNode.DeepEquals(JsonNode.Parse(json), caso["saida"]).Should().BeTrue($"saiu {json}");
    }

    [Theory]
    [MemberData(nameof(CasosValidos))]
    public void Normalizar_a_saida_de_novo_nao_muda_nada(string nome)
    {
        var caso = Caso("normalizacao", nome);
        var primeira = RegraImpressaoNormalizador.ParaJson(RegraImpressaoNormalizador.Normalizar(Ler(caso["entrada"]!)));

        var segunda = RegraImpressaoNormalizador.ParaJson(RegraImpressaoNormalizador.Normalizar(Ler(caso["saida"]!)));

        segunda.Should().Be(primeira, "o texto canônico é o mesmo byte a byte");
        Validator.Validate(Ler(caso["saida"]!)).IsValid.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(CasosInvalidos))]
    public void Recusa_o_invalido_no_campo_indicado(string nome)
    {
        var caso = Caso("invalidos", nome);
        var campo = caso["campo"]!.GetValue<string>();

        var validacao = Validator.Validate(Ler(caso["entrada"]!));

        validacao.IsValid.Should().BeFalse();
        validacao.Errors.Select(e => e.PropertyName).Should().Contain(campo);
    }

    [Fact]
    public void Texto_canonico_tem_ordem_fixa_de_campos()
    {
        var entrada = Ler(JsonNode.Parse("""{"v":1,"documentos":{"pedido":{"copias":2,"papel":"a5"}}}""")!);

        RegraImpressaoNormalizador.ParaJson(RegraImpressaoNormalizador.Normalizar(entrada)).Should().Be(
            """{"v":1,"documentos":{"pedido":{"papel":"a5","template":null,"orientacao":null,"codigos":null,"copias":2}}}""");
    }

    [Fact]
    public void Le_o_jsonb_reordenado_pelo_banco_de_volta_no_texto_canonico()
    {
        // O jsonb do Postgres guarda as chaves na ordem dele, então o GET sempre normaliza o que leu.
        const string doBanco = """{"v": 1, "documentos": {"pedido": {"copias": 1, "papel": "a4", "codigos": null, "template": null, "orientacao": null}}}""";

        var regra = RegraImpressaoNormalizador.DoJson(doBanco);

        RegraImpressaoNormalizador.ParaJson(regra!).Should().Be(
            """{"v":1,"documentos":{"pedido":{"papel":"a4","template":null,"orientacao":null,"codigos":null,"copias":1}}}""");
        RegraImpressaoNormalizador.DoJson(null).Should().BeNull();
    }

    [Fact]
    public void Normalizador_descarta_o_que_nao_e_do_contrato_sem_lancar()
    {
        // Defesa da leitura: o PUT só grava o que passou no validador, mas o normalizador nunca derruba o GET.
        var entrada = Ler(JsonNode.Parse("""
            {"v":1,"documentos":{"cozinha":{"papel":"cupom-80"},"pedido":{"papel":"carta"},
             "etiqueta-pedido":{"papel":"cupom-58","orientacao":"paisagem","codigos":["ean13","qr"],"copias":99,
                                "template":{"origem":"Loja","id":"3f2b6c1e-9a4d-4b7e-8c21-5d0e6f7a8b90"}}}}
            """)!);

        RegraImpressaoNormalizador.ParaJson(RegraImpressaoNormalizador.Normalizar(entrada)).Should().Be(
            """{"v":1,"documentos":{"etiqueta-pedido":{"papel":"cupom-58","template":null,"orientacao":null,"codigos":["qr"],"copias":10}}}""");
    }

    private static RegraImpressaoLojaEntrada Ler(JsonNode node) =>
        node.Deserialize<RegraImpressaoLojaEntrada>(Web)!;

    private static JsonNode Caso(string lista, string nome) =>
        FixtureLida.Value[lista]!.AsArray().Single(c => c!["nome"]!.GetValue<string>() == nome)!;

    private static TheoryData<string> Nomes(string lista)
    {
        var dados = new TheoryData<string>();
        foreach (var caso in FixtureLida.Value[lista]!.AsArray())
            dados.Add(caso!["nome"]!.GetValue<string>());
        return dados;
    }

    private static string RaizDoRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EasyStok.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }
}
