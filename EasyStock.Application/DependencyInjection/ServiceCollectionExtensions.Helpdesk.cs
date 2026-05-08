// Camada Helpdesk + FAQ — UseCases para suporte e base de conhecimento.
// FAQ é base global publica (sem multi-tenant). Tickets seguem o cliente
// e respeitam empresaId via filtro global.

using EasyStock.Application.UseCases.Faq;
using EasyStock.Application.UseCases.Faq.Admin;
using EasyStock.Application.UseCases.TicketSuporte;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra UseCases do helpdesk e FAQ. Inclui:
    /// - tickets cliente (abrir, listar, responder)
    /// - FAQ publico (buscar, listar categorias, obter item, feedback)
    /// - FAQ admin (CRUD itens e categorias, publicar/arquivar)
    /// </summary>
    public static IServiceCollection AddEasyStockHelpdeskUseCases(this IServiceCollection services)
    {
        // Tickets — cliente
        services.AddScoped<AbrirTicketClienteUseCase>();
        services.AddScoped<ListarMeusTicketsUseCase>();
        services.AddScoped<ResponderTicketClienteUseCase>();

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
