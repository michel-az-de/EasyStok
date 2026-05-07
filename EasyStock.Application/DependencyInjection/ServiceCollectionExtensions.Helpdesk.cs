// Camada Helpdesk
// Registra UseCases relacionados a tickets do cliente (PWA / mobile):
// abertura, resposta, listagem, avaliacao CSAT.

using EasyStock.Application.UseCases.TicketSuporte;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.DependencyInjection;

/// <summary>
/// Extensão de ServiceCollection para registrar UseCases de Helpdesk (fluxo cliente).
/// Distinto dos services admin que ficam em <c>EasyStock.Api.Services.Helpdesk</c>.
/// </summary>
public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddEasyStockHelpdeskUseCases(this IServiceCollection services)
    {
        services.AddScoped<AbrirTicketClienteUseCase>();
        services.AddScoped<ResponderTicketClienteUseCase>();
        services.AddScoped<ListarMeusTicketsUseCase>();
        services.AddScoped<AvaliarTicketClienteUseCase>();
        return services;
    }
}
