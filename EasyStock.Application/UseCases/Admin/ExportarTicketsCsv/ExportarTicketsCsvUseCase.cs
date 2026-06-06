using System.Globalization;

namespace EasyStock.Application.UseCases.Admin.ExportarTicketsCsv;

/// <summary>
/// Comando da exportação de tickets. Espelha os filtros de GetTickets; <see cref="Ids"/>
/// não vazio tem precedência (modo "exportar selecionados").
/// </summary>
public sealed record ExportarTicketsCsvCommand(
    TicketStatus? Status = null,
    TicketPrioridade? Prioridade = null,
    NivelAtendimento? Nivel = null,
    TicketCategoria? Categoria = null,
    Guid? EmpresaId = null,
    Guid? AtendenteId = null,
    string? SlaStatus = null,
    string? Search = null,
    IReadOnlyList<Guid>? Ids = null,
    int Limite = 10000);

/// <summary>
/// Exporta tickets filtrados como CSV (UTF-8 BOM, separador <c>;</c>) via <see cref="Csv"/>.
/// Datas UTC ISO; enums via ToString; SLA como Sim/Nao; campos nulos viram vazio.
/// Colunas das de SLA usam os bools armazenados — mesma fonte do filtro <c>slaStatus</c>.
/// </summary>
public class ExportarTicketsCsvUseCase(IAdminTicketRepository repo)
{
    private static readonly IReadOnlyList<string> Header = new[]
    {
        "Titulo", "Empresa", "Categoria", "Prioridade", "Nivel", "Status", "Atendente",
        "CriadoEm", "PrazoResposta", "PrazoResolucao", "SlaRespostaViolado",
        "SlaResolucaoViolado", "ResolvidoEm", "NotaCsat"
    };

    public async Task<byte[]> ExecuteAsync(ExportarTicketsCsvCommand cmd, CancellationToken ct = default)
    {
        var rows = await repo.ListarParaExportarAsync(
            new AdminTicketExportFiltro(cmd.Status, cmd.Prioridade, cmd.Nivel, cmd.Categoria,
                cmd.EmpresaId, cmd.AtendenteId, cmd.SlaStatus, cmd.Search, cmd.Ids, cmd.Limite), ct);

        var linhas = rows.Select(r => (IReadOnlyList<string>)new[]
        {
            r.Titulo,
            r.EmpresaNome ?? "",
            r.Categoria.ToString(),
            r.Prioridade.ToString(),
            r.Nivel.ToString(),
            r.Status.ToString(),
            r.AtendenteNome ?? "",
            r.CriadoEm.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            r.PrazoResposta?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "",
            r.PrazoResolucao?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "",
            r.SlaRespostaViolado ? "Sim" : "Nao",
            r.SlaResolucaoViolado ? "Sim" : "Nao",
            r.ResolvidoEm?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "",
            r.NotaCsat?.ToString(CultureInfo.InvariantCulture) ?? ""
        });

        return Csv.Build(Header, linhas);
    }
}
