using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Domain.Enums.Fiscal;

namespace EasyStock.Application.Ports.Output.Persistence;

public interface INotaFiscalRepository
{
    Task<NotaFiscal?> ObterPorIdAsync(Guid empresaId, Guid id, CancellationToken ct);
    Task<NotaFiscal?> ObterPorIdComItensAsync(Guid empresaId, Guid id, CancellationToken ct);
    Task<NotaFiscal?> ObterPorIdempotencyKeyAsync(Guid empresaId, string key, CancellationToken ct);

    /// <summary>
    /// Busca por chave de acesso ignorando o filtro multi-tenant. Necessário
    /// para o webhook do Focus, que chega sem contexto de tenant. O caller
    /// deve usar <see cref="EasyStock.Domain.Entities.Fiscal.NotaFiscal.EmpresaId"/>
    /// para qualquer mutação subsequente.
    /// </summary>
    Task<NotaFiscal?> ObterPorChaveAsync(string chaveAcesso, CancellationToken ct);

    Task<(IReadOnlyList<NotaFiscal> Items, int Total)> ListarAsync(
        Guid empresaId,
        Guid? lojaId,
        DateTime? desdeUtc,
        DateTime? ateUtc,
        StatusNotaFiscal? status,
        string? chaveAcesso,
        int pagina,
        int tamanhoPagina,
        CancellationToken ct);

    /// <summary>
    /// Lista notas em contingência com idade <24h, para o job de
    /// retransmissão. Ignora filtro de tenant — job roda cross-tenant.
    /// </summary>
    Task<IReadOnlyList<NotaFiscal>> ListarEmContingenciaAsync(int limit, CancellationToken ct);

    Task<IReadOnlyList<int>> ListarNumerosUsadosAsync(
        Guid empresaId, Guid lojaId, ModeloDocumentoFiscal modelo, int serie,
        int de, int ate, CancellationToken ct);

    Task AdicionarAsync(NotaFiscal nota, CancellationToken ct);
    Task AtualizarAsync(NotaFiscal nota, CancellationToken ct);

    Task AdicionarInutilizacaoAsync(NotaFiscalInutilizacao inut, CancellationToken ct);
    Task AtualizarInutilizacaoAsync(NotaFiscalInutilizacao inut, CancellationToken ct);
    Task<NotaFiscalInutilizacao?> ObterInutilizacaoPorIdAsync(Guid empresaId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<NotaFiscalInutilizacao>> ListarInutilizacoesAsync(
        Guid empresaId, Guid? lojaId, int? ano, CancellationToken ct);
}
