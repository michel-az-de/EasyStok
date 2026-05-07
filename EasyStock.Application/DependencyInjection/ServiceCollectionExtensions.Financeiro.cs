using EasyStock.Application.UseCases.Financeiro.Categorias;
using EasyStock.Application.UseCases.Financeiro.CentrosCusto;
using EasyStock.Application.UseCases.Financeiro.ContasPagar;
using EasyStock.Application.UseCases.Financeiro.ContasReceber;
using EasyStock.Application.UseCases.Financeiro.Dashboard;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.DependencyInjection;

public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra UseCases do modulo Contas a Pagar / Contas a Receber (CAP/CAR).
    /// </summary>
    public static IServiceCollection AddEasyStockFinanceiroUseCases(this IServiceCollection services)
    {
        // Categorias
        services.AddScoped<CriarCategoriaFinanceiraUseCase>();
        services.AddScoped<AtualizarCategoriaFinanceiraUseCase>();
        services.AddScoped<InativarCategoriaFinanceiraUseCase>();
        services.AddScoped<ReativarCategoriaFinanceiraUseCase>();
        services.AddScoped<ListarCategoriasFinanceirasUseCase>();
        services.AddScoped<MoverCategoriaFinanceiraUseCase>();

        // Centros de Custo
        services.AddScoped<CriarCentroCustoUseCase>();
        services.AddScoped<AtualizarCentroCustoUseCase>();
        services.AddScoped<InativarCentroCustoUseCase>();
        services.AddScoped<ReativarCentroCustoUseCase>();
        services.AddScoped<ListarCentrosCustoUseCase>();

        // Contas a Pagar
        services.AddScoped<CriarContaPagarUseCase>();
        services.AddScoped<AtualizarContaPagarUseCase>();
        services.AddScoped<EmitirContaPagarUseCase>();
        services.AddScoped<CancelarContaPagarUseCase>();
        services.AddScoped<AdicionarParcelaContaPagarUseCase>();
        services.AddScoped<RemoverParcelaContaPagarUseCase>();
        services.AddScoped<ListarContasPagarUseCase>();
        services.AddScoped<ObterContaPagarDetalheUseCase>();

        // Contas a Receber
        services.AddScoped<CriarContaReceberUseCase>();
        services.AddScoped<AtualizarContaReceberUseCase>();
        services.AddScoped<EmitirContaReceberUseCase>();
        services.AddScoped<CancelarContaReceberUseCase>();
        services.AddScoped<AdicionarParcelaContaReceberUseCase>();
        services.AddScoped<RemoverParcelaContaReceberUseCase>();
        services.AddScoped<ListarContasReceberUseCase>();
        services.AddScoped<ObterContaReceberDetalheUseCase>();

        // Pagamentos + Pix (Onda 4)
        services.AddScoped<RegistrarPagamentoParcelaPagarUseCase>();
        services.AddScoped<RegistrarPagamentoParcelaReceberUseCase>();
        services.AddScoped<EstornarPagamentoParcelaPagarUseCase>();
        services.AddScoped<EstornarPagamentoParcelaReceberUseCase>();
        services.AddScoped<GerarPixQrParcelaReceberUseCase>();
        services.AddScoped<LimparPixParcelaReceberUseCase>();
        services.AddScoped<ReconciliarPixParcelaReceberUseCase>();

        // Dashboard (Onda 5)
        services.AddScoped<ObterDashboardFinanceiroUseCase>();
        services.AddScoped<ObterFluxoCaixaUseCase>();

        return services;
    }
}
