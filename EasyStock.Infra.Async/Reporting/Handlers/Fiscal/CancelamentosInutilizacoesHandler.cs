using System.Runtime.CompilerServices;
using System.Text.Json;
using EasyStock.Application.Reporting;
using EasyStock.Application.Reporting.Definitions.Fiscal.CancelamentosInutilizacoes;
using EasyStock.Domain.Fiscal;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Async.Reporting.Handlers.Fiscal;

/// <summary>
/// Handler do relatório "Cancelamentos e inutilizações" — Fase 2.
/// Retorna eventos de cancelamento e inutilização de NFC-e no período informado.
/// O escopo de tenant é garantido via JOIN com <see cref="NfeDocumento"/> que possui EmpresaId.
/// </summary>
public sealed class CancelamentosInutilizacoesHandler(
    EasyStockDbContext db,
    ITenantScopedQueryBuilder tenantQuery)
    : IReportHandler<CancelamentosInutilizacoesParams, CancelamentosInutilizacoesRow>
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Tipos de evento relevantes para este relatório.
    private static readonly string[] _tiposRelevantes = ["cancelado", "inutilizado"];

    public ReportSchema GetSchema(CancelamentosInutilizacoesParams parametros)
    {
        var competencia = $"{parametros.De:yyyy-MM}";
        return new ReportSchema(
            title: "Cancelamentos e Inutilizações (NFC-e)",
            fileNameBase: $"nfce-cancelamentos_{competencia}",
            columns:
            [
                new("OcorridoEm",       "Data/Hora",              0, "dd/MM/yyyy HH:mm:ss"),
                new("TipoEvento",       "Tipo",                   1),
                new("Numero",           "Número",                 2),
                new("Serie",            "Série",                  3),
                new("ChaveAcesso",      "Chave de Acesso",        4),
                new("TotalNota",        "Total (R$)",             5, "0.00"),
                new("ProtocoloEvento",  "Protocolo SEFAZ",        6),
                new("UsuarioNome",      "Usuário",                7),
                new("Origem",           "Origem",                 8),
            ]);
    }

    public async Task ValidateAsync(CancelamentosInutilizacoesParams parametros, CancellationToken ct)
    {
        if (parametros.De > parametros.Ate)
            throw new ArgumentException(
                "A data final deve ser igual ou posterior à inicial.",
                nameof(parametros.Ate));

        if (parametros.Ate.DayNumber - parametros.De.DayNumber > 366)
            throw new ArgumentException(
                "Para períodos maiores que 12 meses, divida em gerações mensais.",
                nameof(parametros.Ate));
    }

    public async IAsyncEnumerable<CancelamentosInutilizacoesRow> StreamAsync(
        CancelamentosInutilizacoesParams parametros,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var de = parametros.De.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var ate = parametros.Ate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Unspecified);

        // JOIN com NfeDocumento garante escopo de tenant (NfeEvento não tem EmpresaId diretamente).
        var eventos = (
            from evt in db.NfeEventos
            join doc in tenantQuery.Query<NfeDocumento>()
                on evt.NfeDocumentoId equals doc.Id
            where _tiposRelevantes.Contains(evt.Tipo)
                  && evt.OcorridoEm >= de && evt.OcorridoEm <= ate
            orderby evt.OcorridoEm
            select new
            {
                evt.OcorridoEm,
                evt.Tipo,
                evt.ProtocoloEvento,
                evt.UsuarioNome,
                evt.Origem,
                doc.Numero,
                doc.Serie,
                doc.ChaveAcesso,
                TotalNota = doc.TotalNota.Valor,
            }
        ).AsNoTracking().AsAsyncEnumerable();

        await foreach (var e in eventos.WithCancellation(ct))
        {
            yield return new CancelamentosInutilizacoesRow(
                OcorridoEm: e.OcorridoEm,
                TipoEvento: CapitalizarTipo(e.Tipo),
                Numero: e.Numero,
                Serie: e.Serie,
                ChaveAcesso: e.ChaveAcesso,
                TotalNota: e.TotalNota,
                ProtocoloEvento: e.ProtocoloEvento,
                UsuarioNome: e.UsuarioNome,
                Origem: e.Origem);
        }
    }

    public CancelamentosInutilizacoesParams DeserializeParams(string paramsJson) =>
        JsonSerializer.Deserialize<CancelamentosInutilizacoesParams>(paramsJson, _jsonOptions)
            ?? throw new InvalidOperationException("Falha ao deserializar CancelamentosInutilizacoesParams.");

    private static string CapitalizarTipo(string tipo) => tipo switch
    {
        "cancelado" => "Cancelamento",
        "inutilizado" => "Inutilização",
        _ => tipo,
    };
}
