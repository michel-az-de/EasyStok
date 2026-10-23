using System.Data.Common;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Helpers;

/// <summary>
/// Conta os comandos SQL que o EF manda ao banco durante as requisições cujo caminho começa pelo prefixo
/// medido (#1725). Jobs do host rodam sem <see cref="HttpContext"/> e ficam fora da conta. O SET e o RESET
/// do tenant por conexão (#1725-9) não passam pelo EF e também não entram.
/// </summary>
public sealed class ContadorDeComandosSql(IHttpContextAccessor http) : DbCommandInterceptor
{
    private int _comandos;
    private string? _prefixo;

    public int Comandos => Volatile.Read(ref _comandos);

    /// <summary>Zera e passa a contar só as requisições sob <paramref name="prefixo"/>.</summary>
    public void Medir(string prefixo)
    {
        Volatile.Write(ref _prefixo, prefixo);
        Interlocked.Exchange(ref _comandos, 0);
    }

    /// <summary>Faz a requisição e devolve quantos comandos ela mandou ao banco.</summary>
    public async Task<int> ContarAsync(string prefixo, Func<Task> requisicao)
    {
        Medir(prefixo);
        await requisicao();
        return Comandos;
    }

    /// <summary>Instala o contador como interceptor do DbContext da API.</summary>
    public static WebApplicationFactory<Program> Instalar(WebApplicationFactory<Program> factory) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.AddSingleton<ContadorDeComandosSql>();
            services.ConfigureDbContext<EasyStockDbContext>((sp, options) =>
                options.AddInterceptors(sp.GetRequiredService<ContadorDeComandosSql>()));
        }));

    private void Contar()
    {
        if (Volatile.Read(ref _prefixo) is { } prefixo && http.HttpContext?.Request.Path.StartsWithSegments(prefixo) == true)
            Interlocked.Increment(ref _comandos);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Contar();
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Contar();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Contar();
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Contar();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Contar();
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Contar();
        return ValueTask.FromResult(result);
    }
}
