// Atendimento por WhatsApp (ADR-0050) — configuração por tenant (S08), status da integração
// (S01) e agente/conversa (S02, S06, S07 adicionam mais).

using EasyStock.Application.UseCases.Atendimento;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    /// <summary>Registra os UseCases do módulo de atendimento por WhatsApp.</summary>
    public static IServiceCollection AddEasyStockAtendimentoUseCases(this IServiceCollection services)
    {
        services.AddScoped<ObterConfiguracaoAtendimentoUseCase>();
        services.AddScoped<AtualizarConfiguracaoAtendimentoUseCase>();
        services.AddScoped<ObterStatusIntegracaoWhatsAppUseCase>();

        return services;
    }
}
