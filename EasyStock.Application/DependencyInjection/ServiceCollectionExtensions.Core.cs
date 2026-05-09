// Camada Core de Negócio
// Registra UseCases relacionados a: Pedidos, Caixa, Lotes, Produtos, Estoque, Listas de Compras

using Microsoft.Extensions.DependencyInjection;
using EasyStock.Application.Services;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.AtualizarStatusPedido;
using EasyStock.Application.UseCases.CancelarPedido;
using EasyStock.Application.UseCases.ObterPedidoDetalhes;
using EasyStock.Application.UseCases.AdicionarItemPedido;
using EasyStock.Application.UseCases.RemoverItemPedido;
using EasyStock.Application.UseCases.RegistrarPagamentoPedido;
using EasyStock.Application.UseCases.RemoverPagamentoPedido;
using EasyStock.Application.UseCases.ListarPedidosCliente;
using EasyStock.Application.UseCases.AbrirCaixa;
using EasyStock.Application.UseCases.FecharCaixa;
using EasyStock.Application.UseCases.RegistrarMovimentoCaixa;
using EasyStock.Application.UseCases.EstornarMovimentoCaixa;
using EasyStock.Application.UseCases.ListarMovimentosCaixa;
using EasyStock.Application.UseCases.ObterCaixaDia;
using EasyStock.Application.UseCases.ListarFechamentosCaixa;
using EasyStock.Application.UseCases.CriarLote;
using EasyStock.Application.UseCases.AdicionarItemLote;
using EasyStock.Application.UseCases.RemoverItemLote;
using EasyStock.Application.UseCases.FinalizarLote;
using EasyStock.Application.UseCases.ListarLotes;
using EasyStock.Application.UseCases.ObterLoteDetalhes;
using EasyStock.Application.UseCases.ConferirEtiqueta;
using EasyStock.Application.UseCases.ListasCompras;
using EasyStock.Application.UseCases.CadastrarProduto;
using EasyStock.Application.UseCases.GerenciarProduto;
using EasyStock.Application.UseCases.GerenciarVariacaoProduto;
using EasyStock.Application.UseCases.GerenciarUploads;
using EasyStock.Application.UseCases.GerenciarCategoria;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Application.UseCases.EstornarSaida;
using EasyStock.Application.UseCases.ReporEstoque;
using EasyStock.Application.UseCases.BuscarEstoqueInteligente;
using EasyStock.Application.UseCases.AnuncioIa;
using EasyStock.Application.UseCases.GerarSugestaoDescricaoAnuncio;
using EasyStock.Application.UseCases.ListarPlanos;
using EasyStock.Application.UseCases.ListarFaturas;
using EasyStock.Application.UseCases.CancelarAssinatura;
using EasyStock.Application.UseCases.AlterarPlano;
using EasyStock.Application.UseCases.Faturas.EmitirFatura;
using EasyStock.Application.UseCases.Faturas.RegistrarPagamentoFatura;
using EasyStock.Application.UseCases.Faturas.CancelarFatura;
using EasyStock.Application.UseCases.Faturas.ListarFaturasCliente;
using EasyStock.Application.UseCases.Faturas.ListarFaturasAdmin;
using EasyStock.Application.UseCases.Faturas.ObterFaturaDetalhe;
using EasyStock.Application.UseCases.Faturas.GerarPdfFatura;
using EasyStock.Application.UseCases.Faturas.ExportarFaturasCsv;
using EasyStock.Application.UseCases.Faturas.MetricasFinanceiras;

namespace EasyStock.Application.DependencyInjection;

/// <summary>
/// Extensão de ServiceCollection para registrar UseCases Core de Negócio.
/// Faz parte da divisão de responsabilidades do ServiceCollectionExtensions.
/// </summary>
public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra todos os UseCases core: Pedidos, Caixa, Lotes, Produtos, Estoque, Listas de Compras.
    /// </summary>
    public static IServiceCollection AddEasyStockCoreUseCases(this IServiceCollection services)
    {
        // Onda P2 — Pedidos do cliente
        services.AddOptions<PedidoEstoqueOptions>();
        services.AddScoped<PedidoEstoqueIntegrationService>();
        services.AddScoped<CriarPedidoUseCase>();
        services.AddScoped<AtualizarStatusPedidoUseCase>();
        services.AddScoped<CancelarPedidoUseCase>();
        services.AddScoped<ObterPedidoDetalhesUseCase>();
        services.AddScoped<AdicionarItemPedidoUseCase>();
        services.AddScoped<RemoverItemPedidoUseCase>();
        services.AddScoped<RegistrarPagamentoPedidoUseCase>();
        services.AddScoped<RemoverPagamentoPedidoUseCase>();
        services.AddScoped<ListarPedidosUseCase>();

        // Onda P3 — Caixa
        services.AddScoped<AbrirCaixaUseCase>();
        services.AddScoped<FecharCaixaUseCase>();
        services.AddScoped<RegistrarMovimentoCaixaUseCase>();
        services.AddScoped<EstornarMovimentoCaixaUseCase>();
        services.AddScoped<ListarMovimentosCaixaUseCase>();
        services.AddScoped<ObterCaixaDiaUseCase>();
        services.AddScoped<ListarFechamentosCaixaUseCase>();

        // Onda P5.A — Lotes
        services.AddScoped<CriarLoteUseCase>();
        services.AddScoped<AdicionarItemLoteUseCase>();
        services.AddScoped<RemoverItemLoteUseCase>();
        services.AddScoped<FinalizarLoteUseCase>();
        services.AddScoped<ListarLotesUseCase>();
        services.AddScoped<ObterLoteDetalhesUseCase>();
        services.AddScoped<ConferirEtiquetaUseCase>();

        // Onda P5.B — Listas de Compras
        services.AddScoped<ListarListasComprasUseCase>();
        services.AddScoped<ObterListaComprasUseCase>();
        services.AddScoped<CriarListaComprasUseCase>();
        services.AddScoped<ArquivarListaComprasUseCase>();
        services.AddScoped<ReabrirListaComprasUseCase>();
        services.AddScoped<AdicionarItemListaComprasUseCase>();
        services.AddScoped<ToggleItemListaComprasUseCase>();
        services.AddScoped<RemoverItemListaComprasUseCase>();

        // Produtos e Categorias
        services.AddScoped<CadastrarProdutoUseCase>();
        services.AddScoped<GerenciarProdutoUseCase>();
        services.AddScoped<GerenciarVariacaoProdutoUseCase>();
        services.AddScoped<GerenciarUploadsUseCase>();
        services.AddScoped<GerenciarCategoriaUseCase>();

        // Estoque
        services.AddScoped<RegistrarEntradaEstoqueUseCase>();
        services.AddScoped<RegistrarSaidaEstoqueUseCase>();
        services.AddScoped<EstornarSaidaUseCase>();
        services.AddScoped<ReporEstoqueUseCase>();
        services.AddScoped<BuscarEstoqueInteligenteUseCase>();

        // IA para Anúncios
        services.AddScoped<GerarSugestaoDescricaoAnuncioUseCase>();
        services.AddScoped<GerarAnuncioStreamingUseCase>();
        services.AddScoped<SalvarRascunhoAnuncioUseCase>();
        services.AddScoped<ListarAnunciosUseCase>();
        services.AddScoped<ExcluirAnuncioUseCase>();
        services.AddScoped<ObterUsoIaUseCase>();

        // Planos e Assinaturas
        services.AddScoped<ListarPlanosUseCase>();
        services.AddScoped<ListarFaturasUseCase>();
        services.AddScoped<CancelarAssinaturaUseCase>();
        services.AddScoped<AlterarPlanoUseCase>();
        services.AddScoped<EasyStock.Application.UseCases.PagarAgora.PagarAgoraUseCase>();

        // Modulo Financeiro (F1+) — Faturas
        services.AddScoped<EmitirFaturaUseCase>();
        services.AddScoped<RegistrarPagamentoFaturaUseCase>();
        services.AddScoped<CancelarFaturaUseCase>();
        services.AddScoped<ListarFaturasClienteUseCase>();
        services.AddScoped<ListarFaturasAdminUseCase>();
        services.AddScoped<ObterFaturaDetalheUseCase>();
        services.AddScoped<GerarPdfFaturaUseCase>();
        services.AddScoped<ExportarFaturasCsvUseCase>();
        services.AddScoped<MetricasFinanceirasUseCase>();

        return services;
    }
}
