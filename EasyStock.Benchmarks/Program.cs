using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

BenchmarkRunner.Run<ProdutoRepositoryBenchmarks>();

[MemoryDiagnoser]
public class ProdutoRepositoryBenchmarks
{
    private ProdutoRepository _repository = null!;
    private Guid _empresaId = Guid.NewGuid();

    [GlobalSetup]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseInMemoryDatabase("Benchmarks")
            .Options;

        var context = new EasyStockDbContext(options);
        _repository = new ProdutoRepository(context);

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
