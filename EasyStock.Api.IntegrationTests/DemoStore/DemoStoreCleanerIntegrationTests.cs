using EasyStock.Api.Data.DemoStore;
using EasyStock.Application.Demo;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EasyStock.Api.IntegrationTests.DemoStore;

/// <summary>
/// Prova do contrato de segurança do <see cref="DemoStoreCleaner"/> e do
/// <see cref="DemoStoreSeeder"/> contra Postgres real (closes #432).
///
/// Invariante verificada:
///   "limpar remove exatamente as linhas do manifesto e preserva qualquer
///   dado real — inclusive categorias demo referenciadas por produto real."
///
/// Requer: <c>EASYSTOCK_IT_PG</c> = connection string válida.
///   Exemplo: Host=localhost;Port=5432;Database=easystok_demo;Username=easystok;Password=easystok
///
/// Sem a variável os testes são pulados (nunca passam vazios — falso-verde).
/// A empresa de teste tem Id determinístico (estável entre runs).
/// </summary>
public sealed class DemoStoreCleanerIntegrationTests : IAsyncLifetime
{
    private DbContextOptions<EasyStockDbContext>? _options;
    private bool _isAvailable;

    // Id fixo derivado do manifesto — colisão com dado real é impossível
    // (usuário nunca escolhe PKs; o Guid.Empty não pertence a nenhum tenant real).
    private static readonly Guid EmpresaTestId =
        DemoManifest.Id(Guid.Empty, "integration-test-demo-store");

    // ─── IAsyncLifetime ────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        var connString = Environment.GetEnvironmentVariable("EASYSTOCK_IT_PG");
        if (string.IsNullOrWhiteSpace(connString))
        {
            _isAvailable = false;
            return;
        }

        _options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(connString, o => o.CommandTimeout(60))
            .AddInterceptors(new SetTenantOnConnectionInterceptor())
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        // Garante empresa de teste (idempotente — sem duplicata em re-runs).
        // Empresas NÃO têm RLS (sem coluna EmpresaId), então não precisa de bypass.
        await using var db = CriarDb();
        var existe = await db.Empresas.AnyAsync(e => e.Id == EmpresaTestId);
        if (!existe)
        {
            db.Empresas.Add(new Empresa
            {
                Id = EmpresaTestId,
                Nome = "IT Demo Store (teste automatizado)",
                Documento = "00.000.000/0001-IT",
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        _isAvailable = true;
    }

    public async Task DisposeAsync()
    {
        if (!_isAvailable || _options is null) return;

        // Limpeza defensiva em ordem FK-safe (produtos → categorias → empresa).
        await using var db = CriarDb();
        db.SetMobileTenantContext(EmpresaTestId);

        await db.Produtos.ExecuteDeleteAsync();
        await db.Categorias.ExecuteDeleteAsync();

        // Empresa não tem RLS nem query filter — delete direto por Id.
        await db.Empresas
            .Where(e => e.Id == EmpresaTestId)
            .ExecuteDeleteAsync();
    }

    // ─── Testes ────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Carregar_cria_4_categorias_e_12_produtos_na_primeira_carga()
    {
        Skip.IfNot(_isAvailable, "EASYSTOCK_IT_PG não configurada — teste pulado.");
        await LimparDemoAsync(); // garante ponto de partida limpo

        await using var db = CriarDb();
        db.SetMobileTenantContext(EmpresaTestId);

        var criados = await new DemoStoreSeeder(db)
            .CarregarAsync(EmpresaTestId, DateTime.UtcNow);

        criados.Should().Be(16, "4 categorias + 12 produtos devem ser criados na primeira carga");

        (await db.Categorias.CountAsync()).Should().Be(4);
        (await db.Produtos.CountAsync()).Should().Be(12);
    }

    [SkippableFact]
    public async Task Carregar_e_idempotente_segunda_carga_cria_zero_linhas()
    {
        Skip.IfNot(_isAvailable, "EASYSTOCK_IT_PG não configurada — teste pulado.");
        await LimparDemoAsync();

        await using var db = CriarDb();
        db.SetMobileTenantContext(EmpresaTestId);
        var seeder = new DemoStoreSeeder(db);

        await seeder.CarregarAsync(EmpresaTestId, DateTime.UtcNow);
        var criados2 = await seeder.CarregarAsync(EmpresaTestId, DateTime.UtcNow);

        criados2.Should().Be(0, "segunda carga não cria nenhuma linha nova (idempotente)");
    }

    [SkippableFact]
    public async Task Limpar_remove_todo_demo_quando_nenhum_dado_real_existe()
    {
        Skip.IfNot(_isAvailable, "EASYSTOCK_IT_PG não configurada — teste pulado.");
        await LimparDemoAsync();

        await using var db = CriarDb();
        db.SetMobileTenantContext(EmpresaTestId);

        await new DemoStoreSeeder(db).CarregarAsync(EmpresaTestId, DateTime.UtcNow);
        var resultado = await new DemoStoreCleaner(db).LimparAsync(EmpresaTestId);

        resultado.ProdutosRemovidos.Should().Be(12);
        resultado.CategoriasRemovidas.Should().Be(4);
        resultado.ProdutosPreservados.Should().Be(0);

        // Nenhuma linha demo deve restar
        var demoCatIds = DemoManifest.Categorias
            .Select(c => DemoManifest.CategoriaId(EmpresaTestId, c.Slot)).ToHashSet();
        var demoProdIds = DemoManifest.Produtos
            .Select(p => DemoManifest.ProdutoId(EmpresaTestId, p.Slot)).ToHashSet();

        (await db.Categorias.Where(c => demoCatIds.Contains(c.Id)).CountAsync())
            .Should().Be(0, "todas as categorias demo devem ter sido removidas");
        (await db.Produtos.Where(p => demoProdIds.Contains(p.Id)).CountAsync())
            .Should().Be(0, "todos os produtos demo devem ter sido removidos");
    }

    [SkippableFact]
    public async Task Limpar_preserva_categoria_demo_referenciada_por_produto_real()
    {
        Skip.IfNot(_isAvailable, "EASYSTOCK_IT_PG não configurada — teste pulado.");
        await LimparDemoAsync();

        await using var db = CriarDb();
        db.SetMobileTenantContext(EmpresaTestId);

        await new DemoStoreSeeder(db).CarregarAsync(EmpresaTestId, DateTime.UtcNow);

        // Produto real (Id fora do manifesto) aponta para categoria demo slot 1 ("Bebidas").
        // Após "limpar", essa categoria deve sobreviver — tem referência viva.
        var catDemoId = DemoManifest.CategoriaId(EmpresaTestId, 1);
        var produtoRealId = Guid.NewGuid(); // Id fora do manifesto = "dado real"

        db.Produtos.Add(new Produto
        {
            Id         = produtoRealId,
            EmpresaId  = EmpresaTestId,
            CategoriaId = catDemoId,
            Nome       = "Produto Real IT (não demo)",
            Tipo       = TipoProduto.Fisico,
            Status     = StatusProduto.Ativo,
            CriadoEm  = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var resultado = await new DemoStoreCleaner(db).LimparAsync(EmpresaTestId);

        // Produto real deve existir
        (await db.Produtos.FirstOrDefaultAsync(p => p.Id == produtoRealId))
            .Should().NotBeNull("produto real (Id fora do manifesto) nunca deve ser apagado");

        // Categoria demo referenciada pelo produto real deve existir
        (await db.Categorias.FirstOrDefaultAsync(c => c.Id == catDemoId))
            .Should().NotBeNull("categoria demo com produto real apontando para ela deve ser preservada");

        // Apenas as 3 categorias sem produto real devem ter sido removidas
        resultado.CategoriasRemovidas.Should().Be(3,
            "Bebidas é preservada porque o produto real a referencia; Mercearia, Limpeza e Padaria vão");

        // Todos os 12 produtos demo devem ter sido removidos
        // (o produto real não é demo — tem Id fora do manifesto)
        resultado.ProdutosRemovidos.Should().Be(12,
            "nenhum produto demo sobrevive quando não há ItemEstoque/Venda/Movimento real sobre eles");
    }

    [SkippableFact]
    public async Task Limpar_em_loja_vazia_e_no_op_sem_erros()
    {
        Skip.IfNot(_isAvailable, "EASYSTOCK_IT_PG não configurada — teste pulado.");
        await LimparDemoAsync();

        // Nenhum dado demo foi carregado — limpar deve retornar tudo zerado sem lançar.
        await using var db = CriarDb();
        db.SetMobileTenantContext(EmpresaTestId);

        var resultado = await new DemoStoreCleaner(db).LimparAsync(EmpresaTestId);

        resultado.ProdutosRemovidos.Should().Be(0);
        resultado.CategoriasRemovidas.Should().Be(0);
        resultado.ProdutosPreservados.Should().Be(0);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private EasyStockDbContext CriarDb()
    {
        if (_options is null) throw new InvalidOperationException("DbContext options não inicializadas.");
        return new EasyStockDbContext(_options);
    }

    /// <summary>
    /// Remove todos os produtos e categorias do tenant de teste.
    /// Ponto de partida limpo para cada cenário de teste.
    /// </summary>
    private async Task LimparDemoAsync()
    {
        await using var db = CriarDb();
        db.SetMobileTenantContext(EmpresaTestId);
        // FK: produtos referenciam categorias → apaga produtos primeiro.
        await db.Produtos.ExecuteDeleteAsync();
        await db.Categorias.ExecuteDeleteAsync();
    }
}
