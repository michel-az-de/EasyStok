// Camada Helpdesk + FAQ — UseCases para suporte ao cliente e base de conhecimento.
// Tickets seguem o cliente e respeitam empresaId via filtro global.
// FAQ eh base global publica (sem multi-tenant).

using EasyStock.Application.UseCases.Faq;
using EasyStock.Application.UseCases.Faq.Admin;
using EasyStock.Application.UseCases.TicketSuporte;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.DependencyInjection;

/// <summary>
/// Extensao de ServiceCollection para registrar UseCases de Helpdesk e FAQ.
/// Distinto dos services admin que ficam em <c>EasyStock.Api.Services.Helpdesk</c>.
/// </summary>
public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra UseCases do helpdesk e FAQ. Inclui:
    /// - tickets cliente (abrir, listar, responder, avaliar CSAT)
    /// - FAQ publico (buscar, listar categorias, obter item, feedback)
    /// - FAQ admin (CRUD itens e categorias, publicar/arquivar)
    /// </summary>
    public static IServiceCollection AddEasyStockHelpdeskUseCases(this IServiceCollection services)
    {
        // Tickets — cliente
        services.AddScoped<AbrirTicketClienteUseCase>();
        services.AddScoped<ResponderTicketClienteUseCase>();
        services.AddScoped<ListarMeusTicketsUseCase>();
        services.AddScoped<AvaliarTicketClienteUseCase>();

        // FAQ — publico
        services.AddScoped<BuscarFaqUseCase>();
        services.AddScoped<ListarCategoriasFaqUseCase>();
        services.AddScoped<ObterFaqItemUseCase>();
        services.AddScoped<RegistrarFeedbackFaqUseCase>();

        // FAQ — admin
        services.AddScoped<CriarFaqCategoriaUseCase>();
        services.AddScoped<CriarFaqItemUseCase>();
        services.AddScoped<AtualizarFaqItemUseCase>();
        services.AddScoped<PublicarFaqItemUseCase>();
        services.AddScoped<ArquivarFaqItemUseCase>();
        services.AddScoped<ListarFaqAdminUseCase>();

        return services;
    }
}
