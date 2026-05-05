// Camada de Fornecedores e Pedidos de Fornecedor
// Registra UseCases relacionados a: CRUD de fornecedores, pedidos de compra, recebimento

using Microsoft.Extensions.DependencyInjection;
using EasyStock.Application.UseCases.CriarFornecedor;
using EasyStock.Application.UseCases.AtualizarFornecedor;
using EasyStock.Application.UseCases.DesativarFornecedor;
using EasyStock.Application.UseCases.ReativarFornecedor;
using EasyStock.Application.UseCases.ListarFornecedores;
using EasyStock.Application.UseCases.Fornecedor;
using EasyStock.Application.UseCases.ObterHistoricoAlteracoesFornecedor;
using EasyStock.Application.UseCases.Pedido;

namespace EasyStock.Application.DependencyInjection;

/// <summary>
/// Extensão de ServiceCollection para registrar UseCases de Fornecedores e Pedidos de Fornecedor.
/// Faz parte da divisão de responsabilidades do ServiceCollectionExtensions.
/// </summary>
public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra todos os UseCases de gerenciamento de fornecedores e pedidos de compra.
    /// </summary>
    public static IServiceCollection AddEasyStockFornecedorUseCases(this IServiceCollection services)
    {
        // CRUD de fornecedores
        services.AddScoped<CriarFornecedorUseCase>();
        services.AddScoped<AtualizarFornecedorUseCase>();
        services.AddScoped<DesativarFornecedorUseCase>();
        services.AddScoped<ReativarFornecedorUseCase>();
        services.AddScoped<ListarFornecedoresUseCase>();

        // Detalhes e estatísticas de fornecedor
        services.AddScoped<ObterFornecedorDetalheUseCase>();
        services.AddScoped<ObterHistoricoFornecedorUseCase>();
        services.AddScoped<ObterEstatisticasFornecedorUseCase>();

        // Pedidos para fornecedor (Onda P1)
        services.AddScoped<ListarPedidosAbertosUseCase>();
        services.AddScoped<CriarPedidoFornecedorUseCase>();
        services.AddScoped<ReceberPedidoFornecedorUseCase>();
        services.AddScoped<CancelarPedidoFornecedorUseCase>();

        // Auditoria de alterações
        services.AddScoped<ObterHistoricoAlteracoesFornecedorUseCase>();

        return services;
    }
}
