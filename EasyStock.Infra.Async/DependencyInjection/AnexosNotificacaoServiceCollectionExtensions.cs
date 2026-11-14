using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Services.Notifications.Anexos;
using EasyStock.Infra.Async.Pdf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyStock.Infra.Async.DependencyInjection;

/// <summary>
/// Anexos dos avisos (#2211): o anexador e os modelos ficam juntos dos renderizadores e só entram no host que também
/// registra o armazenamento (API e Worker). Sem esta chamada o motor envia o texto sem anexo.
/// </summary>
public static class AnexosNotificacaoServiceCollectionExtensions
{
    public static IServiceCollection AddAnexosDeNotificacao(this IServiceCollection services)
    {
        services.TryAddSingleton<IRenderizadorCanhoto, CanhotoDocumentoRenderer>();
        // #2544: o resumo do pedido do Assistente sobe pelo mesmo armazenamento público dos anexos.
        services.TryAddSingleton<IRenderizadorResumoPedido, ResumoPedidoRenderer>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IModeloAnexoNotificacao, ModeloAnexoCanhoto>());
        services.TryAddScoped<IAnexadorMensagem, AnexadorMensagem>();
        return services;
    }
}
