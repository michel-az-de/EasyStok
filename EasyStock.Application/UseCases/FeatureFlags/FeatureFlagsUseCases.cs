namespace EasyStock.Application.UseCases.FeatureFlags;

// ── Tenant: quais módulos ESTA empresa enxerga ──────────────────────────────
public sealed record ObterFeaturesAtivasQuery(Guid EmpresaId);

/// <summary>
/// Features ativas do tenant, para o produto decidir o que mostrar (ADR-0048). O
/// <c>EmpresaId</c> vem da claim do usuário, resolvida no controller — nunca do cliente.
/// </summary>
public sealed class ObterFeaturesAtivasUseCase(ITenantFeatureFlagRepository repo)
{
    public async Task<IReadOnlyList<string>> ExecuteAsync(ObterFeaturesAtivasQuery q, CancellationToken ct = default)
        => await repo.ListarAtivasAsync(q.EmpresaId, ct);
}

// ── Admin: visão de administração de um tenant ──────────────────────────────
public sealed record ListarFeaturesDoTenantQuery(Guid EmpresaId);

/// <summary>
/// Todas as flags do tenant (ligadas e desligadas), com a auditoria de quem mexeu — é o que
/// a aba "Features" do back-office mostra.
/// </summary>
public sealed class ListarFeaturesDoTenantUseCase(ITenantFeatureFlagRepository repo)
{
    public async Task<IReadOnlyList<TenantFeatureFlagItem>> ExecuteAsync(
        ListarFeaturesDoTenantQuery q, CancellationToken ct = default)
        => await repo.ListarPorEmpresaAsync(q.EmpresaId, ct);
}

// ── Admin: ligar/desligar ───────────────────────────────────────────────────
public sealed record DefinirFeatureDoTenantCommand(Guid EmpresaId, string Feature, bool Ativo, string AlteradoPor);

/// <summary>
/// Liga ou desliga uma feature do tenant (cria a linha se ainda não existir), registrando o
/// e-mail de quem alterou. Alterar módulo de cliente sem deixar rastro não é opção.
/// </summary>
public sealed class DefinirFeatureDoTenantUseCase(ITenantFeatureFlagRepository repo)
{
    public async Task<TenantFeatureFlagItem> ExecuteAsync(
        DefinirFeatureDoTenantCommand cmd, CancellationToken ct = default)
    {
        if (cmd.EmpresaId == Guid.Empty)
            throw new UseCaseValidationException("Empresa nao informada.");

        var feature = (cmd.Feature ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(feature))
            throw new UseCaseValidationException("Feature nao informada.");

        // A coluna tem 50 caracteres; recusar aqui dá mensagem melhor que erro do banco.
        if (feature.Length > 50)
            throw new UseCaseValidationException("Nome de feature muito longo (maximo 50 caracteres).");

        if (!FeatureCatalogo.NomeValido(feature))
            throw new UseCaseValidationException("Nome de feature invalido: use apenas letras, numeros, ponto, hifen ou underscore.");

        return await repo.DefinirAsync(cmd.EmpresaId, feature, cmd.Ativo, cmd.AlteradoPor, ct);
    }
}

/// <summary>
/// Catálogo das features que o produto reconhece. Serve para o back-office oferecer os nomes
/// certos em vez de campo livre — flag com nome errado nunca liga nada e ninguém descobre por
/// quê, porque o sistema simplesmente não pergunta por ela.
/// </summary>
public static class FeatureCatalogo
{
    /// <summary>Módulos B2B da FMA (ADR-0048), desligados por padrão para os demais tenants.</summary>
    public const string ModuloComercial = "modulo.comercial";
    public const string ModuloCrm = "modulo.crm";

    /// <summary>Nomes conhecidos, na ordem em que aparecem no back-office.</summary>
    public static IReadOnlyList<string> Conhecidas { get; } = [ModuloComercial, ModuloCrm];

    /// <summary>
    /// Aceita apenas letras, números, ponto, hífen e underscore — mesmo formato que o
    /// back-office já valida antes de enviar.
    /// </summary>
    public static bool NomeValido(string feature) =>
        !string.IsNullOrWhiteSpace(feature)
        && feature.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
}
