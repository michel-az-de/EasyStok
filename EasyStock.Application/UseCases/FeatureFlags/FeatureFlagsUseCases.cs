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

public sealed record FeatureDoTenantDto(
    string Feature, string Descricao, bool Ativo, DateTime? AlteradoEm, string? AlteradoPor);

/// <summary>
/// O que a aba "Features" do back-office mostra: o <b>catálogo inteiro</b> de features que o
/// produto reconhece, cada uma com seu estado atual — e não apenas as linhas já gravadas.
///
/// <para>
/// A diferença importa: a tela só oferece o botão de ligar/desligar para o que vem nesta
/// lista. Devolvendo só o que está salvo, um tenant novo (que não tem linha nenhuma) veria a
/// aba vazia e não teria como ativar módulo algum. Features salvas fora do catálogo — de
/// versões anteriores — continuam aparecendo, para não sumirem sem ninguém notar.
/// </para>
/// </summary>
public sealed class ListarFeaturesDoTenantUseCase(ITenantFeatureFlagRepository repo)
{
    public async Task<IReadOnlyList<FeatureDoTenantDto>> ExecuteAsync(
        ListarFeaturesDoTenantQuery q, CancellationToken ct = default)
    {
        var salvas = await repo.ListarPorEmpresaAsync(q.EmpresaId, ct);
        var porNome = salvas.ToDictionary(f => f.Feature, StringComparer.OrdinalIgnoreCase);

        var doCatalogo = FeatureCatalogo.Conhecidas.Select(c =>
            porNome.TryGetValue(c.Nome, out var salva)
                ? new FeatureDoTenantDto(c.Nome, c.Descricao, salva.Ativo, salva.AlteradoEm, salva.AlteradoPor)
                : new FeatureDoTenantDto(c.Nome, c.Descricao, false, null, null));

        var foraDoCatalogo = salvas
            .Where(s => !FeatureCatalogo.Conhecidas.Any(c => string.Equals(c.Nome, s.Feature, StringComparison.OrdinalIgnoreCase)))
            .Select(s => new FeatureDoTenantDto(s.Feature, "Feature fora do catálogo atual.", s.Ativo, s.AlteradoEm, s.AlteradoPor));

        return [.. doCatalogo, .. foraDoCatalogo];
    }
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

    /// <summary>Features conhecidas, na ordem em que aparecem no back-office.</summary>
    public static IReadOnlyList<FeatureConhecida> Conhecidas { get; } =
    [
        new(ModuloComercial, "Propostas, contratos e catálogo de serviços (B2B)."),
        new(ModuloCrm, "Clientes PJ, pipeline e oportunidades (B2B)."),
    ];

    /// <summary>
    /// Aceita apenas letras, números, ponto, hífen e underscore — mesmo formato que o
    /// back-office já valida antes de enviar.
    /// </summary>
    public static bool NomeValido(string feature) =>
        !string.IsNullOrWhiteSpace(feature)
        && feature.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
}

/// <summary>Feature que o produto reconhece, com a descrição que o back-office exibe.</summary>
public sealed record FeatureConhecida(string Nome, string Descricao);
