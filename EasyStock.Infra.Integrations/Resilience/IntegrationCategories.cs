namespace EasyStock.Infra.Integrations.Resilience;

/// <summary>
/// Identificadores das categorias de integração externa. Servem de chave
/// pros pipelines Polly registrados via
/// <see cref="DependencyInjection.IntegrationsServiceCollectionExtensions.AddEasyStockIntegrationResilience"/>
/// e pros resolvers de provider keyed-DI.
///
/// <para>
/// Mantenha como string constants (não enum) — pipelines do
/// <c>Microsoft.Extensions.Resilience</c> são chaveados por string.
/// </para>
/// </summary>
public static class IntegrationCategories
{
    public const string Payments = "payments";
    public const string Fiscal = "fiscal";
    public const string Marketplace = "marketplace";
    public const string Logistics = "logistics";

    public static readonly IReadOnlyCollection<string> All = new[]
    {
        Payments, Fiscal, Marketplace, Logistics,
    };
}
