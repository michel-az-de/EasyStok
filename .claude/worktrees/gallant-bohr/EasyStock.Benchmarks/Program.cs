using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

BenchmarkRunner.Run<ProdutoRepositoryBenchmarks>();

[MemoryDiagnoser]
public class ProdutoRepositoryBenchmarks
{
    private ProdutoRepository _repository;
    private Guid _empresaId = Guid.NewGuid();

    [GlobalSetup]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseInMemoryDatabase("Benchmarks")
            .Options;

        var context = new EasyStockDbContext(options);
        var cache = new MemoryCache(new MemoryCacheOptions());
        _repository = new ProdutoRepository(context, cache);

        // Seed data
        for (int i = 0; i < 1000; i++)
        {
            context.Produtos.Add(new EasyStock.Domain.Entities.Produto
            {
                Id = Guid.NewGuid(),
                EmpresaId = _empresaId,
                Nome = $"Produto {i}",
                Tipo = EasyStock.Domain.Enums.TipoProduto.Fisico,
                Status = EasyStock.Domain.Enums.StatusProduto.Ativo,
                CriadoEm = DateTime.UtcNow
            });
        }
        context.SaveChanges();
    }

    [Benchmark]
    public async Task GetProdutosPaginadosAsync()
    {
        await _repository.GetProdutosPaginadosAsync(_empresaId, 1, 20);
    }
}
