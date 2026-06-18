using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Infra.Async.Pdf;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Pdf;

/// <summary>
/// Smoke test do <see cref="FechamentoCaixaExtratoRenderer"/> com QuestPDF real (sem mock):
/// garante que o template compila no pipeline e produz um PDF válido (assinatura "%PDF-").
/// </summary>
public class FechamentoCaixaExtratoRendererSmokeTests
{
    private static FechamentoCaixaExtratoPdfData Extrato(bool comMovimentos = true)
    {
        var movs = comMovimentos
            ? new List<FechamentoCaixaExtratoMovimento>
            {
                new(new DateTime(2026, 6, 17, 8, 0, 0, DateTimeKind.Utc), "abertura", "Saldo inicial", "dinheiro", null, 100m, 100m, false),
                new(new DateTime(2026, 6, 17, 10, 30, 0, DateTimeKind.Utc), "entrada", "Venda balcão", "pix", "venda", 50m, 50m, false),
                new(new DateTime(2026, 6, 17, 14, 0, 0, DateTimeKind.Utc), "saida", "Sangria", "dinheiro", "sangria", 20m, -20m, true),
            }
            : new List<FechamentoCaixaExtratoMovimento>();

        return new FechamentoCaixaExtratoPdfData(
            EmpresaNome: "Casa da Babá",
            EmpresaDocumento: "12345678000190",
            LojaNome: "Loja Centro",
            LojaEndereco: "Rua das Flores, 100 - Centro",
            LogoPng: null,
            Data: new DateOnly(2026, 6, 17),
            SaldoInicial: 100m, TotalVendas: 500m, TotalPagamentosPedidos: 120m,
            TotalEntradasExtras: 50m, TotalSaidasExtras: 20m, SaldoFinal: 750m,
            FechadoPorNome: "Operador X",
            FechadoEm: new DateTime(2026, 6, 17, 18, 0, 0, DateTimeKind.Utc),
            Observacoes: "Conferência ok.",
            Movimentos: movs);
    }

    [Fact]
    public async Task Render_ProduzPdfValidoComAssinaturaCorreta()
    {
        var renderer = new FechamentoCaixaExtratoRenderer();

        var bytes = await renderer.RenderAsync(Extrato());

        bytes.Should().NotBeNullOrEmpty();
        bytes.Length.Should().BeGreaterThan(1000);
        bytes[..5].Should().BeEquivalentTo(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }); // %PDF-
    }

    [Fact]
    public async Task Render_SemMovimentos_NaoLanca()
    {
        var renderer = new FechamentoCaixaExtratoRenderer();

        var bytes = await renderer.RenderAsync(Extrato(comMovimentos: false));

        bytes.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Render_RespeitaCancellationToken()
    {
        var renderer = new FechamentoCaixaExtratoRenderer();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => renderer.RenderAsync(Extrato(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
