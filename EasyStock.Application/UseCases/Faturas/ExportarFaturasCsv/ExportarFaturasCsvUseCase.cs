using System.Globalization;
using System.Text;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Enums;

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

        var sb = new StringBuilder();
        // BOM UTF-8 — Excel reconhece como UTF-8 sem precisar de import wizard.
        // Adicionado manualmente nos bytes finais (Encoding.UTF8.GetPreamble).
        sb.AppendLine("Numero;EmpresaNome;FaturadoNome;FaturadoDocumento;Origem;Status;DataEmissao;DataVencimento;DataPagamentoTotal;SubTotal;Descontos;Acrescimos;Total;TotalPago;Pendente;Moeda");

        foreach (var f in itens)
        {
            ct.ThrowIfCancellationRequested();
            sb.Append(Csv(f.Numero)).Append(';');
            sb.Append(Csv(f.Empresa?.Nome)).Append(';');
            sb.Append(Csv(f.DadosFaturado?.Nome)).Append(';');
            sb.Append(Csv(f.DadosFaturado?.Documento)).Append(';');
            sb.Append(f.Origem.ToString()).Append(';');
            sb.Append(f.Status.ToString()).Append(';');
            sb.Append(f.DataEmissao.ToString("yyyy-MM-dd HH:mm:ss")).Append(';');
            sb.Append(f.DataVencimento.ToString("yyyy-MM-dd")).Append(';');
            sb.Append(f.DataPagamentoTotal?.ToString("yyyy-MM-dd HH:mm:ss") ?? "").Append(';');
            sb.Append(f.SubTotal.ToString("F2", PtBr)).Append(';');
            sb.Append(f.Descontos.ToString("F2", PtBr)).Append(';');
            sb.Append(f.Acrescimos.ToString("F2", PtBr)).Append(';');
            sb.Append(f.Total.ToString("F2", PtBr)).Append(';');
            sb.Append(f.TotalPago.ToString("F2", PtBr)).Append(';');
            sb.Append(f.Pendente.ToString("F2", PtBr)).Append(';');
            sb.Append(f.Moeda);
            sb.AppendLine();
        }

        // BOM UTF-8 + content
        var bom = Encoding.UTF8.GetPreamble();
        var content = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[bom.Length + content.Length];
        Buffer.BlockCopy(bom, 0, result, 0, bom.Length);
        Buffer.BlockCopy(content, 0, result, bom.Length, content.Length);
        return result;
    }

    /// <summary>Escapa campo CSV: envolve em aspas se contem ; aspa ou newline; duplica aspas internas.</summary>
    private static string Csv(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        if (raw.Contains(';') || raw.Contains('"') || raw.Contains('\n') || raw.Contains('\r'))
        {
            return "\"" + raw.Replace("\"", "\"\"") + "\"";
        }
        return raw;
    }
}
