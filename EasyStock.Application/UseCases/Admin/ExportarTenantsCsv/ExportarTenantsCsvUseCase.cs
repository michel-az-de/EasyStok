using System.Globalization;

namespace EasyStock.Application.UseCases.Admin.ExportarTenantsCsv;

/// <summary>
/// Comando da exportação de clientes (tenants). <see cref="Ids"/> não vazio tem
/// precedência sobre <see cref="Search"/>/<see cref="Status"/> (modo "exportar selecionados").
/// </summary>
public sealed record ExportarTenantsCsvCommand(
    string? Search = null,
    StatusAssinatura? Status = null,
    IReadOnlyList<Guid>? Ids = null,
    int Limite = 10000);

/// <summary>
/// Exporta clientes filtrados como CSV (UTF-8 BOM, separador <c>;</c>) via <see cref="Csv"/>.
/// Datas em UTC ISO e número F2 pt-BR — mesma convenção do export de Faturas.
/// Colunas: <c>Nome;Documento;Plano;PrecoMensal;Status;Usuarios;Lojas;CriadoEm;DataRenovacao</c>.
/// </summary>
public class ExportarTenantsCsvUseCase(IAdminTenantsQueries queries)
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private static readonly IReadOnlyList<string> Header = new[]
    {
        "Nome", "Documento", "Plano", "PrecoMensal", "Status",
        "Usuarios", "Lojas", "CriadoEm", "DataRenovacao"
    };

    public async Task<byte[]> ExecuteAsync(ExportarTenantsCsvCommand cmd, CancellationToken ct = default)
    {
        var rows = await queries.ListarParaExportarAsync(
            new TenantExportFiltro(cmd.Search, cmd.Status, cmd.Ids, cmd.Limite), ct);

        var linhas = rows.Select(r => (IReadOnlyList<string>)new[]
        {
            r.Nome,
            r.Documento ?? "",
            r.PlanoNome ?? "",
            r.PrecoMensal?.ToString("F2", PtBr) ?? "",
            r.Status?.ToString() ?? "",
            r.TotalUsuarios.ToString(CultureInfo.InvariantCulture),
            r.TotalLojas.ToString(CultureInfo.InvariantCulture),
            r.CriadoEm.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            r.DataRenovacao?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? ""
        });

        return Csv.Build(Header, linhas);
    }
}
