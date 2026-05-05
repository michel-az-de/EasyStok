// Camada de Clientes
// Registra UseCases relacionados a: CRUD de clientes, endereços, telefones, documentos

using Microsoft.Extensions.DependencyInjection;
using EasyStock.Application.UseCases.CriarCliente;
using EasyStock.Application.UseCases.AtualizarCliente;
using EasyStock.Application.UseCases.ListarClientes;
using EasyStock.Application.UseCases.BuscarCliente;
using EasyStock.Application.UseCases.DesativarCliente;
using EasyStock.Application.UseCases.ReativarCliente;
using EasyStock.Application.UseCases.AdicionarClienteEndereco;
using EasyStock.Application.UseCases.RemoverClienteEndereco;
using EasyStock.Application.UseCases.AdicionarClienteTelefone;
using EasyStock.Application.UseCases.RemoverClienteTelefone;
using EasyStock.Application.UseCases.AdicionarClienteDocumento;
using EasyStock.Application.UseCases.RemoverClienteDocumento;
using EasyStock.Application.UseCases.ObterClienteDetalhes;

namespace EasyStock.Application.DependencyInjection;

/// <summary>
/// Extensão de ServiceCollection para registrar UseCases de Clientes.
/// Faz parte da divisão de responsabilidades do ServiceCollectionExtensions.
/// </summary>
public static partial class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra todos os UseCases de gerenciamento de clientes e seus dados de contato.
    /// </summary>
    public static IServiceCollection AddEasyStockClienteUseCases(this IServiceCollection services)
    {
        // CRUD de clientes
        services.AddScoped<CriarClienteUseCase>();
        services.AddScoped<AtualizarClienteUseCase>();
        services.AddScoped<ListarClientesUseCase>();
        services.AddScoped<BuscarClienteUseCase>();
        services.AddScoped<ObterClienteDetalhesUseCase>();
        services.AddScoped<DesativarClienteUseCase>();
        services.AddScoped<ReativarClienteUseCase>();

        // Gerenciamento de endereços
        services.AddScoped<AdicionarClienteEnderecoUseCase>();
        services.AddScoped<RemoverClienteEnderecoUseCase>();

        // Gerenciamento de telefones
        services.AddScoped<AdicionarClienteTelefoneUseCase>();
        services.AddScoped<RemoverClienteTelefoneUseCase>();

        // Gerenciamento de documentos (CPF, CNPJ, RG, etc)
        services.AddScoped<AdicionarClienteDocumentoUseCase>();
        services.AddScoped<RemoverClienteDocumentoUseCase>();

        return services;
    }
}
