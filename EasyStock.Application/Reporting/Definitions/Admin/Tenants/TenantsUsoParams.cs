namespace EasyStock.Application.Reporting.Definitions.Admin.Tenants;

/// <summary>Parâmetros do relatório de uso de tenants — Admin SaaS.</summary>
public sealed record TenantsUsoParams(
    /// <summary>Filtra por status de assinatura. Nulo = todos.</summary>
    string? StatusAssinatura = null,
    /// <summary>Filtra por plano. Nulo = todos.</summary>
    string? Plano = null);
