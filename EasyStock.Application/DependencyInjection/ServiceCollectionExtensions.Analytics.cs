// Camada Analytics e Inteligência
// Registra UseCases relacionados a: Dashboard, Projeções, Alertas, Margem, Inteligência

using Microsoft.Extensions.DependencyInjection;
using EasyStock.Application.UseCases.Analytics.Dashboard;
using EasyStock.Application.UseCases.Analytics.DashboardFull;
using EasyStock.Application.UseCases.Analytics.Projecoes;
using EasyStock.Application.UseCases.Analytics.Reposicao;
using EasyStock.Application.UseCases.Analytics.Sazonalidade;
using EasyStock.Application.UseCases.Analytics.Alertas;
using EasyStock.Application.UseCases.Analytics.AlertasDias;
using EasyStock.Application.UseCases.Analytics.Receita;
using EasyStock.Application.UseCases.Analytics.Margem;
using EasyStock.Application.UseCases.Analytics.Movimentacoes;
using EasyStock.Application.UseCases.Analytics.Validade;
using EasyStock.Application.UseCases.Analytics.Parados;
using EasyStock.Application.UseCases.Analytics.ReceitaCusto;
using EasyStock.Application.UseCases.Analytics.VendasPorCanal;
using EasyStock.Application.UseCases.Analytics.DashboardExtras;
using EasyStock.Application.UseCases.Inteligencia.Board;
using EasyStock.Application.UseCases.Inteligencia.ProjecaoRuptura;
using EasyStock.Application.UseCases.Inteligencia.Rotatividade;
using EasyStock.Application.UseCases.Inteligencia.Sazonalidade;
using EasyStock.Application.UseCases.Inteligencia.EstoqueBaixo;
using EasyStock.Application.UseCases.Inteligencia.ProximoVencimento;
using EasyStock.Application.UseCases.Inteligencia.ItensParados;
using EasyStock.Application.UseCases.Inteligencia.SugestaoReposicao;

namespace EasyStock.Application.DependencyInjection;

/// <summary>
/// Extensão de ServiceCollection para registrar UseCases de Analytics e Inteligência.
/// Faz parte da divisão de responsabilidades do ServiceCollectionExtensions.
/// </summary>
public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra todos os UseCases de Analytics (Dashboard, Alertas, Métricas) e Inteligência (Sugestões, Projeções).
    /// </summary>
    public static IServiceCollection AddEasyStockAnalyticsUseCases(this IServiceCollection services)
    {
        // Analytics — Dashboard e KPIs
        services.AddScoped<GetDashboardUseCase>();
        services.AddScoped<GetDashboardFullUseCase>();
        services.AddScoped<CalcularProjecoesUseCase>();
        services.AddScoped<CalcularReposicaoUseCase>();
        services.AddScoped<CalcularSazonalidadeUseCase>();
        services.AddScoped<ObterAlertasUseCase>();
        services.AddScoped<ObterDiasAlertaValidadeUseCase>();
        services.AddScoped<CalcularReceitaUseCase>();
        services.AddScoped<CalcularMargemUseCase>();
        services.AddScoped<ObterMovimentacoesUseCase>();
        services.AddScoped<ObterValidadeUseCase>();
        services.AddScoped<ObterParadosUseCase>();
        services.AddScoped<ObterDiasAlertaParadoUseCase>();
        services.AddScoped<ObterVendasPorCanalUseCase>();
        services.AddScoped<GetReceitaCustoUseCase>();
        services.AddScoped<GetDashboardExtrasUseCase>();

        // Inteligência — Recomendações baseadas em IA/ML
        services.AddScoped<GetBoardUseCase>();
        services.AddScoped<CalcularProjecaoRupturaUseCase>();
        services.AddScoped<CalcularRotatividadeUseCase>();
        services.AddScoped<CalcularSazonalidadeInteligenciaUseCase>();
        services.AddScoped<ObterEstoqueBaixoUseCase>();
        services.AddScoped<ObterProximoVencimentoUseCase>();
        services.AddScoped<ObterItensParadosUseCase>();
        services.AddScoped<ObterSugestaoReposicaoUseCase>();

        return services;
    }
}
