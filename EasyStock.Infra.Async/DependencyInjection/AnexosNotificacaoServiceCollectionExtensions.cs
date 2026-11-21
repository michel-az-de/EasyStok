using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Notifications.Anexos;
using EasyStock.Infra.Async.Pdf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EasyStock.Infra.Async.DependencyInjection;

/// <summary>
/// Anexos dos avisos (#2211): o anexador e os modelos ficam juntos dos renderizadores e só entram no host que também
/// registra o armazenamento (API e Worker). Sem esta chamada o motor envia o texto sem anexo.
/// </summary>
public static class AnexosNotificacaoServiceCollectionExtensions
{
    public static IServiceCollection AddAnexosDeNotificacao(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.TryAddSingleton<IRenderizadorCanhoto, CanhotoDocumentoRenderer>();
        // #2544: o resumo do pedido do Assistente sobe pelo mesmo armazenamento público dos anexos.
        services.TryAddSingleton<IRenderizadorResumoPedido, ResumoPedidoRenderer>();
        // #2962: PDF do pedido e comprovante; o publicador só existe onde há armazenamento (sem ele a cobrança segue em texto).
        services.TryAddSingleton<IRenderizadorPdfPedido, PdfPedidoRenderer>();
        services.TryAddScoped<PublicadorDocumentoPedido>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IModeloAnexoNotificacao, ModeloAnexoCanhoto>());
        services.TryAddScoped<IAnexadorMensagem, AnexadorMensagem>();
        // #3017: o PNG do impresso só existe onde há a URL do Gotenberg; 8 s e sem retry, porque a cliente espera o Pix.
        if (!string.IsNullOrWhiteSpace(configuration?[GotenbergRenderizadorHtml.ChaveUrl]))
            services.AddHttpClient<IRenderizadorHtml, GotenbergRenderizadorHtml>(c => c.Timeout = TimeSpan.FromSeconds(8));
        return services;
    }
}
