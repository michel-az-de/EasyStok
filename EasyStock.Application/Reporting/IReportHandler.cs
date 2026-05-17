using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Reporting;

/// <summary>
/// Contrato de execução de um relatório tipado.
/// TParams é o record de parâmetros; TRow é o record imutável de saída.
/// </summary>
public interface IReportHandler<TParams, TRow>
    where TParams : class
    where TRow    : class
{
    /// <summary>
    /// Retorna o esquema de colunas derivado do TRow.
    /// Pode usar reflection + atributos sobre as propriedades do record.
    /// </summary>
    ReportSchema GetSchema(TParams parametros);

    /// <summary>
    /// Valida os parâmetros de negócio (ex: datas válidas, lojaId pertence ao tenant).
    /// Lança <see cref="ArgumentException"/> ou <see cref="EasyStock.Application.UseCases.Common.UseCaseValidationException"/>
    /// em caso de violação.
    /// </summary>
    Task ValidateAsync(TParams parametros, CancellationToken ct);

    /// <summary>
    /// Produz as linhas de dados em streaming.
    /// O caller nunca materializa a coleção inteira em memória.
    /// </summary>
    IAsyncEnumerable<TRow> StreamAsync(TParams parametros, CancellationToken ct);

    /// <summary>
    /// Deserializa o JSON de parâmetros armazenado em <see cref="Domain.Reporting.ReportRun"/>.
    /// </summary>
    TParams DeserializeParams(string paramsJson);

    /// <summary>
    /// Hook fraco para uso futuro (Power BI / endpoint /data síncrono).
    /// Implementação default: não suportado.
    /// </summary>
    IQueryable<TRow> BuildQueryable(TParams parametros) =>
        throw new NotSupportedException($"BuildQueryable não implementado em {GetType().Name}.");
}
