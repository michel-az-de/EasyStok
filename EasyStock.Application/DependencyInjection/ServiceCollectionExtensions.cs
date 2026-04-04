using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.BuscarEstoqueInteligente;
using EasyStock.Application.UseCases.CadastrarProduto;
using EasyStock.Application.UseCases.GerenciarCategoria;
using EasyStock.Application.UseCases.GerenciarFornecedor;
using EasyStock.Application.UseCases.GerenciarLoja;
using EasyStock.Application.UseCases.GerenciarUsuario;
using EasyStock.Application.UseCases.GerarSugestaoDescricaoAnuncio;
using EasyStock.Application.UseCases.ListarPlanos;
using EasyStock.Application.UseCases.RegistrarEmpresa;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Application.UseCases.ReporEstoque;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEasyStockApplication(this IServiceCollection services)
    {
        services.AddScoped<AutenticarUsuarioUseCase>();
        services.AddScoped<RegistrarEmpresaUseCase>();
        services.AddScoped<GerenciarUsuarioUseCase>();
        services.AddScoped<GerenciarLojaUseCase>();
        services.AddScoped<GerenciarFornecedorUseCase>();
        services.AddScoped<CadastrarProdutoUseCase>();
        services.AddScoped<RegistrarEntradaEstoqueUseCase>();
        services.AddScoped<RegistrarSaidaEstoqueUseCase>();
        services.AddScoped<ReporEstoqueUseCase>();
        services.AddScoped<BuscarEstoqueInteligenteUseCase>();
        services.AddScoped<GerarSugestaoDescricaoAnuncioUseCase>();
        services.AddScoped<GerenciarCategoriaUseCase>();
        services.AddScoped<ListarPlanosUseCase>();

        return services;
    }
}
