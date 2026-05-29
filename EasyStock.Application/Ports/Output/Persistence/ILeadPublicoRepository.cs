namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Acesso a leads capturados na landing publica. Sem multi-tenant — o lead
/// existe antes (ou independente) de virar Empresa.
/// </summary>
public interface ILeadPublicoRepository
{
    Task AddAsync(LeadPublico lead, CancellationToken ct = default);
    Task<LeadPublico?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task UpdateAsync(LeadPublico lead, CancellationToken ct = default);

    /// <summary>Conta quantos leads foram criados de um dado IP nas ultimas N horas (anti-spam).</summary>
    Task<int> ContarPorIpRecenteAsync(string ip, TimeSpan janela, CancellationToken ct = default);

    Task<(IReadOnlyList<LeadPublico> Items, int Total)> ListarPaginadoAsync(
        OrigemLead? origem = null,
        bool? processado = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);
}
