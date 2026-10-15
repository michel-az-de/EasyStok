using EasyStock.Api.Services.Impressao;
using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Infra.Notifications.Templating;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Services.Impressao;

/// <summary>
/// #1839: o resumo do Encerrar sai no layout "bilhete da casa", em qualquer papel, em vez do
/// <c>window.print()</c> da folha React. Snapshots em <c>Snapshots/</c>; regravar com
/// <c>IMPRESSAO_ATUALIZAR_SNAPSHOT=1</c> e revisar o diff.
/// </summary>
public class ResumoAtendimentoHtmlTests
{
    private const string BaseUrl = "https://api.test";
    private static readonly DateTime Agora = new(2026, 10, 15, 10, 0, 0);
    private static readonly PedidoImpressoCasaLeitura Casa = new("Casa da Baba", null, "casadababa.com.br", "11987654412", "/lojas/a/b/logo/x.png");

    public static TheoryData<string> Papeis => new(ImpressoHtml.Papeis);

    private static ResumoAtendimentoImpresso Exemplo(
        IReadOnlyList<ResumoVendaImpresso>? vendas = null, string? anotacao = "Prefere entrega à tarde", string cliente = "Felipe Azevedo") => new(
        cliente, "WhatsApp · +5511982254398", "AT-0012", new DateTime(2026, 10, 15, 9, 58, 0), "13 d", "20:09 a 10:00",
        10, 13, 8, 263m, 0m, 0m, "Sim", true,
        vendas ?? [new ResumoVendaImpresso("5", "1x Lasanha Bolonhesa Clássica 800 g, 1x Ravioli 500 g", "Pix", 263m, "Entregue")],
        263m, [new ResumoMarcoImpresso("10:19", "Pagamento confirmado")], "Positiva", "Bom", anotacao);

    private static Task<string> Renderizar(ResumoAtendimentoImpresso r, string modelo) =>
        new ResumoAtendimentoHtml(new ScribanRenderer(NullLogger<ScribanRenderer>.Instance)).RenderizarAsync(r, Casa, modelo, BaseUrl, Agora);

    [Theory]
    [InlineData(ImpressoHtml.A4)]
    [InlineData(ImpressoHtml.Cupom80)]
    public async Task Snapshot(string modelo)
    {
        SnapshotImpresso.Conferir($"resumo.{modelo}.html", await Renderizar(Exemplo(), modelo));
    }

    [Theory]
    [MemberData(nameof(Papeis))]
    public async Task Todo_papel_sai_com_o_numero_o_cliente_e_a_receita(string modelo)
    {
        var html = await Renderizar(Exemplo(), modelo);
        html.Should().Contain("Atendimento AT-0012").And.Contain("Felipe Azevedo").And.Contain("R$ 263,00");
        html.Should().Contain("Cliente 10 · Você 13 · Automático 8");
    }

    [Fact]
    public async Task Texto_digitado_sai_escapado()
    {
        var html = await Renderizar(Exemplo(anotacao: "<script>alert(1)</script>", cliente: "Ana & <b>Bia</b>"), ImpressoHtml.A4);
        html.Should().NotContain("<script>").And.NotContain("<b>Bia</b>");
        html.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public async Task Sem_venda_e_sem_anotacao_explica_e_nao_deixa_bloco_vazio()
    {
        var html = await Renderizar(Exemplo(vendas: [], anotacao: "  "), ImpressoHtml.A5);
        html.Should().Contain("Sem pedido com movimento neste atendimento.");
        html.Should().NotContain("<h2>Anotação</h2>");
    }

    [Fact]
    public async Task Termica_sai_com_o_nome_da_casa_em_texto_e_jato_de_tinta_com_o_logo()
    {
        (await Renderizar(Exemplo(), ImpressoHtml.Cupom80)).Should().Contain("<div class=\"wm\">Casa da Baba</div>");
        (await Renderizar(Exemplo(), ImpressoHtml.A4)).Should().Contain("src=\"https://api.test/lojas/a/b/logo/x.png\"");
    }

    [Fact]
    public void Corpo_grande_ou_com_item_nulo_e_recusado()
    {
        ResumoAtendimentoHtml.Valido(Exemplo()).Should().BeTrue();
        ResumoAtendimentoHtml.Valido(Exemplo(anotacao: new string('a', 2001))).Should().BeFalse();
        ResumoAtendimentoHtml.Valido(Exemplo(vendas: [null!])).Should().BeFalse();
        ResumoAtendimentoHtml.Valido(Exemplo(cliente: " ")).Should().BeFalse();
    }
}
