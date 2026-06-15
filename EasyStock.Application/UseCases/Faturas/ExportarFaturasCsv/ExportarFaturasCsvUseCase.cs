using System.Globalization;

namespace EasyStock.Application.UseCases.Faturas.ExportarFaturasCsv;

public sealed record ExportarFaturasCsvCommand(
    Guid? EmpresaId = null,
    StatusFatura? Status = null,
    OrigemFatura? Origem = null,
    DateTime? VencimentoDe = null,
    DateTime? VencimentoAte = null,
    decimal? ValorMin = null,
    decimal? ValorMax = null,
    string? Busca = null,
    int LimiteMaximo = 10000
);

/// <summary>
/// Exporta faturas filtradas como CSV (UTF-8, separador <c>;</c> para
/// compatibilidade Excel pt-BR). Retorna conteudo completo em memoria —
/// sem streaming por enquanto. Limite default 10k linhas; acima disso o
/// caller deve paginar manualmente.
///
/// <para>
/// Colunas: <c>Numero, EmpresaNome, FaturadoNome, FaturadoDocumento,
/// Origem, Status, DataEmissao, DataVencimento, DataPagamentoTotal,
/// SubTotal, Descontos, Acrescimos, Total, TotalPago, Pendente, Moeda</c>.
/// </para>
/// </summary>
public class ExportarFaturasCsvUseCase(IFaturaRepository repo)
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public async Task<byte[]> ExecuteAsync(ExportarFaturasCsvCommand cmd, CancellationToken ct = default)
    {
        // Clamp positivo + teto 10k. LimiteMaximo <= 0 viraria pageSize <= 0 e quebraria
        // a paginacao do repo (Take(0) retornaria vazio mesmo havendo dados).
        var pageSize = Math.Clamp(cmd.LimiteMaximo, 1, 10000);
        var (itens, _) = await repo.ListarAdminAsync(
            cmd.EmpresaId,
            cmd.Status,
            cmd.Origem,
            cmd.VencimentoDe,
            cmd.VencimentoAte,
            cmd.ValorMin,
            cmd.ValorMax,
            cmd.Busca,
            page: 1,
            pageSize: pageSize,
            ct);

        var headers = new[]
        {
            "Numero", "EmpresaNome", "FaturadoNome", "FaturadoDocumento", "Origem", "Status",
            "DataEmissao", "DataVencimento", "DataPagamentoTotal", "SubTotal", "Descontos",
            "Acrescimos", "Total", "TotalPago", "Pendente", "Moeda"
        };

        var rows = itens.Select(f =>
        {
            ct.ThrowIfCancellationRequested();
            return new[]
            {
                f.Numero ?? "",
                f.Empresa?.Nome ?? "",
                f.DadosFaturado?.Nome ?? "",
                f.DadosFaturado?.Documento ?? "",
                f.Origem.ToString(),
                f.Status.ToString(),
                f.DataEmissao.ToString("yyyy-MM-dd HH:mm:ss"),
                f.DataVencimento.ToString("yyyy-MM-dd"),
                f.DataPagamentoTotal?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                f.SubTotal.ToString("F2", PtBr),
                f.Descontos.ToString("F2", PtBr),
                f.Acrescimos.ToString("F2", PtBr),
                f.Total.ToString("F2", PtBr),
                f.TotalPago.ToString("F2", PtBr),
                f.Pendente.ToString("F2", PtBr),
                f.Moeda ?? ""
            };
        });

        // CSV central (#612): BOM UTF-8, separador ';', anti-injecao de formula + quoting RFC-4180.
        return Csv.Build(headers, rows);
    }
}
