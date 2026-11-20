using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>#2962 (E3): o texto dos dois balões e o envio de documento do canal sem suporte.</summary>
public class TextoCobrancaPixTests
{
    private const string Codigo = "00020126580014BR.GOV.BCB.PIX0136chave-da-loja5204000053039865802BR6304ABCD";

    private static ResumoPedidoDto Resumo(int itens = 2) => new(
        "Casa da Baba", "0194A1",
        Enumerable.Range(1, itens).Select(i => new ResumoPedidoItemDto($"Lasanha {i}", 1, 30m, null)).ToList(),
        0m, 30m * itens, "20/11 às 19:30", null);

    private static PixPdfDto Pix(decimal restante = 0m) => new(Codigo, 60m - restante, restante, "CASA DA BABA");

    [Fact]
    public void Legenda_AbreComPedidoELoja_ListaItensTotalEntregaEAFraseDoPix()
    {
        var texto = TextoCobrancaPix.Legenda(Resumo(), Pix());

        var linhas = texto.Split('\n');
        linhas[0].Should().Be("Pedido 0194A1 · Casa da Baba");
        texto.Should().Contain("Lasanha 1").And.Contain("Lasanha 2").And.Contain("Total R$");
        texto.Should().Contain("Entrega 20/11 às 19:30");
        linhas[^1].Should().Be("Pague em até 30 min pelo Pix abaixo");
    }

    [Fact]
    public void Legenda_NuncaLevaOCopiaECola()
    {
        TextoCobrancaPix.Legenda(Resumo(), Pix()).Should().NotContain(Codigo).And.NotContain("BR.GOV.BCB");
    }

    [Fact]
    public void Legenda_ComMuitosItens_CortaEDizQuantosFaltam()
    {
        var texto = TextoCobrancaPix.Legenda(Resumo(itens: 8), Pix());

        texto.Should().Contain("Lasanha 5").And.NotContain("Lasanha 6").And.Contain("e mais 3 itens");
    }

    [Fact]
    public void Legenda_Encomenda_MostraOSinalEORestante()
    {
        var texto = TextoCobrancaPix.Legenda(Resumo(), Pix(restante: 30m));

        texto.Should().Contain("Sinal de R$").And.Contain("restante na entrega");
    }

    [Fact]
    public void BalaoDoCodigo_SoOCodigo()
    {
        TextoCobrancaPix.BalaoDoCodigo(Pix() with { CopiaCola = $"  {Codigo}\n" }).Should().Be(Codigo);
    }

    [Fact]
    public async Task CanalSemDocumento_MandaALegendaComOLinkEmTexto()
    {
        var canal = new CanalSoTexto();

        await ((ICanalMensageria)canal).EnviarDocumentoAsync("contato", "https://x.test/a.pdf", "a.pdf", "Pedido 1");
        await ((ICanalMensageria)canal).EnviarDocumentoAsync("contato", "https://x.test/b.pdf", "b.pdf");

        canal.Textos.Should().Equal("Pedido 1\nhttps://x.test/a.pdf", "https://x.test/b.pdf");
    }

    private sealed class CanalSoTexto : ICanalMensageria
    {
        public List<string> Textos { get; } = [];
        public CanalConversa Canal => CanalConversa.WhatsApp;
        public Task<string> EnviarTextoAsync(string c, string t, CancellationToken ct = default) { Textos.Add(t); return Task.FromResult("id"); }
        public Task<string> EnviarImagemAsync(string c, string u, string? l = null, CancellationToken ct = default) => Task.FromResult("id");
        public Task<string> EnviarBotoesAsync(string c, string corpo, IReadOnlyList<(string Id, string Titulo)> b, CancellationToken ct = default) => Task.FromResult("id");
        public Task<string> EnviarModeloAsync(string c, string n, string i, IReadOnlyList<string> p, CancellationToken ct = default) => Task.FromResult("id");
        public Task MarcarComoLidaAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
    }
}
