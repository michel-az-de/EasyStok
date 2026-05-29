namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Read model dedicado ao painel SaaS superadmin. Centraliza as projeções
/// cross-aggregate (Empresa + Assinatura + Plano + contadores) que antes
/// viviam direto no AdminTenantsController.
/// </summary>
public interface IAdminTenantsQueries
{
    Task<(IReadOnlyList<TenantListItem> Items, int Total)> ListarAsync(
        int page, int pageSize, string? search, StatusAssinatura? status);

    Task<TenantDetail?> ObterDetalheAsync(Guid empresaId);

    Task<(IReadOnlyList<TenantAuditLogInfo> Items, int Total)> GetAuditLogsPagedAsync(
        Guid empresaId, int page, int pageSize);
}

public sealed record TenantListItem(
    Guid Id,
    string Nome,
    string? Documento,
    DateTime CriadoEm,
    int TotalUsuarios,
    int TotalLojas,
    string? PlanoNome,
    StatusAssinatura? StatusAssinatura,
    DateTime? DataRenovacao);

public sealed record TenantDetail(
    TenantEmpresaInfo Empresa,
    TenantAssinaturaInfo? Assinatura,
    IReadOnlyList<TenantLojaInfo> Lojas,
    IReadOnlyList<TenantUsuarioInfo> Usuarios,
    IReadOnlyList<TenantAuditLogInfo> AuditLogRecentes);

public sealed record TenantEmpresaInfo(Guid Id, string Nome, string? Documento, DateTime CriadoEm);

public sealed record TenantAssinaturaInfo(
    Guid Id,
    StatusAssinatura Status,
    DateTime DataInicio,
    DateTime? DataFim,
    DateTime? TrialFim,
    bool TrialAtivo,
    string? CupomCodigo,
    decimal? DescontoAplicado,
    TenantPlanoInfo? Plano);

public sealed record TenantPlanoInfo(
    Guid Id, string Nome, decimal PrecoMensal,
    int LimiteLojas, int LimiteUsuarios, int LimiteProdutos);

public sealed record TenantLojaInfo(
    Guid Id,
    string Nome,
    string? Descricao,
    string? Documento,
    string? Endereco,
    string? Telefone,
    bool Ativa,
    DateTime CriadoEm,
    DateTime AlteradoEm);

public sealed record TenantUsuarioInfo(
    Guid Id, string Nome, string Email, bool Ativo,
    DateTime? UltimoAcessoEm, NivelAcesso? NivelAcesso);

public sealed record TenantAuditLogInfo(
    Guid Id, Guid UsuarioId, string Acao, bool Sucesso, string? Detalhes, string? Ip, DateTime DataHora);
