using System.Text;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Application.UseCases.ClienteCrm;
using EasyStock.Infra.Async.Pdf;

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
}
