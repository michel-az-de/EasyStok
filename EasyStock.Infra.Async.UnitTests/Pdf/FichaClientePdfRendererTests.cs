using System.Text;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Application.UseCases.ClienteCrm;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Async.Pdf;
using UglyToad.PdfPig;

namespace EasyStock.Infra.Async.UnitTests.Pdf;

public sealed class FichaClientePdfRendererTests
{
    private static readonly DateTime Consulta = new(2026, 10, 29, 15, 0, 0, DateTimeKind.Utc);
    private static DossieClienteDto Dados(bool extensa = false) => new(
        new(Guid.Parse("a1111111-1111-4111-8111-111111111111"), "Mária André", "11999999999", "maria@example.test", null, null),
        [], [new("Sem cebola", "teste", Consulta)],
        Enumerable.Range(0, extensa ? 20 : 1).Select(i => new ClienteNotaResult(Guid.NewGuid(),
            extensa ? string.Join(" ", Enumerable.Repeat("Observação longa para conferir paginação legível.", 70))
                : "<script>alert('texto privado')</script> · Prefere retirar no sábado.",
            "Ana", null, null, Consulta)).ToArray(),
        Enumerable.Range(0, extensa ? 2 : 1).Select(_ => new PedidoResumoCliente(Guid.NewGuid(), "entregue", Consulta, 38,
            [new("Ravióli", 1)])).ToArray(), null, Consulta, extensa ? 2 : 1, false, null, [], [], null,
        TotalGasto: extensa ? 76 : 38);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeraPdfRealComTextoFotoEPaginacaoSemHtml(bool extensa)
    {
        // Imagem PNG válida gerada pelo encoder já usado no produto; apenas fixture de imagem.
        var imagem = DocumentoEntradaPdfRenderer.GerarQrPng("Foto de teste da ficha");
        var bytes = await new FichaClientePdfRenderer().RenderAsync(Dados(extensa), imagem, Consulta);
        var estrutura = Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-", estrutura);
        Assert.EndsWith("%%EOF", estrutura.TrimEnd());
        Assert.Contains("/Subtype /Image", estrutura);
        Assert.DoesNotContain("/JavaScript", estrutura);
        var pasta = Environment.GetEnvironmentVariable("EASYSTOK_FICHA_PDF_EVIDENCIAS");
        if (!string.IsNullOrWhiteSpace(pasta))
        {
            Directory.CreateDirectory(pasta);
            await File.WriteAllBytesAsync(Path.Combine(pasta, extensa ? "ficha-extensa.pdf" : "ficha-cliente.pdf"), bytes);
        }
    }

    [Fact]
    public async Task FotosRetangularesCabemNaFichaSemFalhaDeLayout()
    {
        // PNGs sintéticos 3×2 e 2×3 exercitam as duas orientações sem dependência de encoder.
        foreach (var png in new[] {
            "iVBORw0KGgoAAAANSUhEUgAAAAMAAAACCAIAAAASFvFNAAAAFElEQVR4nGOs8jFnAAMmCMXAwAAAEkkBAal7cjEAAAAASUVORK5CYII=",
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAADCAIAAAA2iEnWAAAAFElEQVR4nGOs8jFnYGBgYgADKAUAE14BAylcnS4AAAAASUVORK5CYII=" })
        {
            var bytes = await new FichaClientePdfRenderer().RenderAsync(Dados(), Convert.FromBase64String(png), Consulta);
            Assert.StartsWith("%PDF-", Encoding.Latin1.GetString(bytes));
            Assert.Contains("/Subtype /Image", Encoding.Latin1.GetString(bytes));
        }
    }

    [Fact]
    public async Task CancelamentoNaoGeraDocumento()
    {
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new FichaClientePdfRenderer().RenderAsync(Dados(), null, Consulta, cancelamento.Token));
    }

    // ---- #3387: o texto que sai no papel (extraído do PDF), não só a estrutura ----

    private static readonly Guid ClienteId = Guid.Parse("a1111111-1111-4111-8111-111111111111");

    private static PedidoResumoCliente Ped(string status, int diasAtras, decimal total, params (string, decimal)[] itens) =>
        new(Guid.NewGuid(), status, Consulta.AddDays(-diasAtras), total, itens.Select(i => new ItemPedidoResumo(i.Item1, i.Item2)).ToList());

    /// <summary>Cliente sem telefone nem e-mail, 25 pedidos no total (10 listados, um cancelado), ritmo opcional.</summary>
    private static DossieClienteDto Ficha(RitmoDeCompraDossie? ritmo, bool bloqueado = false) => new(
        new(ClienteId, "Mária André", null, null, null, bloqueado ? "Chargeback em outubro" : null),
        [], [new("sem_gluten", "dona", Consulta)],
        [new(Guid.NewGuid(), "Prefere retirar no sábado.", "Ana", null, null, Consulta)],
        new[] { Ped("cancelado", 1, 50, ("Brigadeiro", 2)) }
            .Concat(Enumerable.Range(2, 9).Select(i => Ped(i % 2 == 0 ? "aguardando_pagamento" : "saiu_para_entrega", i, 38, ("Ravióli", 1)))).ToList(),
        new("Ravióli", 7, 9), Consulta.AddDays(-2), 25, bloqueado, new(true, true, Consulta), [],
        [new(Guid.NewGuid(), CanalConversa.WhatsApp, SituacaoConversa.Automatica, Consulta, Consulta)], null,
        TotalGasto: 950, Ritmo: ritmo, PrimeiraCompraEm: new DateTime(2025, 3, 10, 15, 0, 0, DateTimeKind.Utc));

    private static async Task<string> TextoDe(DossieClienteDto dossie)
    {
        var bytes = await new FichaClientePdfRenderer().RenderAsync(dossie, null, Consulta);
        using var pdf = PdfDocument.Open(bytes);
        var texto = string.Join(" ", pdf.GetPages().Select(p => p.Text));
        return System.Text.RegularExpressions.Regex.Replace(texto, @"\s+", " ");
    }

    [Fact]
    public async Task TextoDoPapelTemAcentosRotulosELegendaDaLista()
    {
        var texto = await TextoDe(Ficha(null));
        Assert.Contains("não informado", texto);
        foreach (var rotulo in new[] { "Compras", "Total comprado", "Ticket médio", "Última compra", "Contato e entrega", "Notas internas", "Pedidos recentes" })
            Assert.Contains(rotulo, texto);
        Assert.Contains("Mostrando os 10 mais recentes de 25 pedidos", texto);
        Assert.Contains("Cliente desde março de 2025", texto);
        Assert.Contains("há 2 dias", texto);
    }

    [Fact]
    public async Task FraseDoRitmoSoApareceComRitmo()
    {
        Assert.Contains("Pede a cada ~12 dias (mediana das últimas 8 compras)", await TextoDe(Ficha(new(12, 8))));
        Assert.DoesNotContain("Pede a cada", await TextoDe(Ficha(null)));
    }

    [Fact]
    public async Task PedidoCanceladoDizCanceladoEStatusNaoVazaCodigo()
    {
        var texto = await TextoDe(Ficha(null));
        Assert.Contains("Cancelado", texto);
        Assert.Contains("Aguardando pagamento", texto);
        Assert.Contains("Saiu para entrega", texto);
        Assert.DoesNotContain("_", texto.Replace("sem_gluten", ""));
    }

    [Fact]
    public async Task CabecalhoNaoMostraOGuid()
    {
        var texto = await TextoDe(Ficha(null));
        Assert.DoesNotContain(ClienteId.ToString(), texto);
        Assert.DoesNotContain("a1111111", texto);
    }

    [Fact]
    public async Task CadastroBloqueadoGanhaCaixaDeAtencaoComMotivo()
    {
        var texto = await TextoDe(Ficha(null, bloqueado: true));
        Assert.Contains("Atenção", texto);
        Assert.Contains("Cadastro bloqueado: Chargeback em outubro", texto);
        Assert.DoesNotContain("Atenção", await TextoDe(Ficha(null)));
    }

    [Fact]
    public async Task SemCaixaDeAtencaoQuandoNaoHaNadaParaAvisar_TagsFicamNoContato()
    {
        Assert.Contains("Tags: sem_gluten", await TextoDe(Ficha(null)));
    }
}
