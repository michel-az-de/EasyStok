using System.Globalization;
using EasyStock.Application.Reporting;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Async.Reporting.Exporters;
using FluentAssertions;
using MiniExcelLibs;

namespace EasyStock.Infra.Async.UnitTests.Reporting.Exporters;

/// <summary>
/// Caracteriza o contrato de saída XLSX do <see cref="ExcelExporter"/>: contagem de
/// linhas, rótulos de cabeçalho na ordem do schema, formatação por tipo (data/decimal/
/// bool/null em pt-BR) — lidos de volta via <c>MiniExcel.Query</c>.
///
/// Rede de regressão para #364: verde ANTES e DEPOIS de mover a serialização para uma
/// thread dedicada — prova que só muda ONDE o pull bloqueante roda, não O QUE é gerado.
/// </summary>
public class ExcelExporterCharacterizationTests
{
    private sealed class LinhaFake
    {
        public DateTime DataVenda { get; init; }
        public string?  Loja      { get; init; }
        public decimal  Total     { get; init; }
        public bool     Pago      { get; init; }
    }

    private static ReportSchema Schema() => new(
        title: "Vendas",
        fileNameBase: "vendas-teste",
        columns:
        [
            new("DataVenda", "Data/Hora",     0, "dd/MM/yyyy HH:mm:ss"),
            new("Loja",      "Loja",          1),
            new("Total",     "Total (R$)",    2, "0.00"),
            new("Pago",      "Pago",          3),
        ]);

    private static async IAsyncEnumerable<LinhaFake> Rows(IEnumerable<LinhaFake> items)
    {
        foreach (var it in items)
        {
            await Task.Yield(); // cruza de verdade a fronteira sync-over-async do Read()
            yield return it;
        }
    }

    [Fact]
    public async Task WriteAsync_GeraXlsx_ComContagem_Headers_E_FormatacaoPorTipo()
    {
        var linhas = new[]
        {
            new LinhaFake { DataVenda = new DateTime(2026, 5, 31, 14, 30, 0), Loja = "Centro", Total = 1234.50m, Pago = true },
            new LinhaFake { DataVenda = new DateTime(2026, 1, 2, 9, 5, 0),    Loja = null,     Total = 0m,       Pago = false },
        };

        using var output = new MemoryStream();
        await new ExcelExporter().WriteAsync(
            Rows(linhas), Schema(), output, new ReportExportOptions(), CancellationToken.None);

        output.Position = 0;
        var rows = MiniExcel.Query(output, useHeaderRow: true).Cast<IDictionary<string, object>>().ToList();

        // contagem (alimenta MarkSucceeded(..., rowCount) no ReportRunner)
        rows.Should().HaveCount(2);

        // headers = HeaderLabel na ordem do schema
        rows[0].Keys.Should().ContainInOrder("Data/Hora", "Loja", "Total (R$)", "Pago");

        // formatação por tipo, linha 0
        rows[0]["Data/Hora"].Should().Be("31/05/2026 14:30:00");
        rows[0]["Loja"].Should().Be("Centro");
        Convert.ToDecimal(rows[0]["Total (R$)"], CultureInfo.InvariantCulture).Should().Be(1234.50m);
        rows[0]["Pago"].Should().Be("Sim");

        // null → célula vazia; bool false → "Não"
        (rows[1]["Loja"] is null or "").Should().BeTrue();
        rows[1]["Pago"].Should().Be("Não");
    }
}
