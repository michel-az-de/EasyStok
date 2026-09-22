// Atendimento por WhatsApp (ADR-0050) — agente, conversa e integracao com a Meta Cloud API.

using EasyStock.Application.UseCases.Atendimento;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    /// <summary>Registra os UseCases do modulo de atendimento por WhatsApp.</summary>
    public static IServiceCollection AddEasyStockAtendimentoUseCases(this IServiceCollection services)
    {
        services.AddScoped<ObterStatusIntegracaoWhatsAppUseCase>();

        return services;
    }
}
