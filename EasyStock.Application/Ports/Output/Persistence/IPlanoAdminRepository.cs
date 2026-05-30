namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Persistência dos planos no contexto Admin SaaS (CRUD + contagem de tenants).
/// Dedicado ao back-office — distinto do IPlanoRepository (storefront/assinatura) —
/// para não acoplar contextos. Encapsula o que vivia no AdminPlanosController (F7).
/// </summary>
public interface IPlanoAdminRepository
{
    Task<IReadOnlyList<PlanoAdminItem>> ListarComTenantsAsync(CancellationToken ct = default);

    Task<PlanoResumo> CriarAsync(NovoPlano dados, CancellationToken ct = default);

    /// <summary>Patch parcial. Retorna null se o plano não existir.</summary>
    Task<PlanoResumo?> AtualizarAsync(Guid id, PatchPlano patch, CancellationToken ct = default);

    /// <summary>Alterna o flag Ativo. Retorna null se o plano não existir.</summary>
    Task<PlanoAtivoResultado?> AlternarAtivoAsync(Guid id, CancellationToken ct = default);
}

public sealed record PlanoAdminItem(
    Guid Id,
    string Nome,
    string? Descricao,
    int LimiteLojas,
    int LimiteUsuarios,
    int LimiteProdutos,
    int LimiteGeracoesIaMensais,
    decimal PrecoMensal,
    bool Ativo,
    DateTime CriadoEm,
    int TotalTenants);

public sealed record NovoPlano(
    string Nome,
    string? Descricao,
    int LimiteLojas,
    int LimiteUsuarios,
    int LimiteProdutos,
    int LimiteGeracoesIaMensais,
    decimal PrecoMensal);

public sealed record PatchPlano(
    string? Nome,
    string? Descricao,
    int? LimiteLojas,
    int? LimiteUsuarios,
    int? LimiteProdutos,
    int? LimiteGeracoesIaMensais,
    decimal? PrecoMensal);

public sealed record PlanoResumo(Guid Id, string Nome);

public sealed record PlanoAtivoResultado(Guid Id, bool Ativo);
