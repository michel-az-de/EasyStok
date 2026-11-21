using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2484: a busca dos cadastros usa ILIKE. Sem escapar o termo, "_" casava qualquer caractere e "%"
/// qualquer texto, e a busca devolvia todos os registros. Cobre os três jeitos de montar o padrão:
/// normalizado sem acento (produto, cliente), variável simples (fornecedor) e direto na expressão (marcas).
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class BuscaComCuringaIlike2484Tests(PostgreSqlDatabaseFixture fixture)
{
    private static Guid Empresa(EasyStockDbContext db)
    {
        var empresaId = Guid.NewGuid();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(new Empresa
        {
            Id = empresaId, Nome = "Busca 2484", Documento = empresaId.ToString("N")[..14],
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
        });
        return empresaId;
    }

    private static Produto Produto(Guid empresaId, Guid categoriaId, string nome, string marca) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = empresaId, CategoriaId = categoriaId, Nome = nome, Marca = marca,
        Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
    };

    private async Task<(EasyStockDbContext Db, Guid EmpresaId)> ProdutosAsync()
    {
        var db = fixture.CreateDbContext();
        var empresaId = Empresa(db);
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Doces", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        db.Categorias.Add(categoria);
        db.Produtos.AddRange(
            Produto(empresaId, categoria.Id, "Bolo de pote", "Casa"),
            Produto(empresaId, categoria.Id, "Kit_festa", "Marca_festa"),
            Produto(empresaId, categoria.Id, "Desconto 10%", "Leve 10%"));
        await db.SaveChangesAsync();
        return (db, empresaId);
    }

    [SkippableTheory]
    [InlineData("_", "Kit_festa")]
    [InlineData("%", "Desconto 10%")]
    public async Task Busca_de_produto_procura_o_curinga_como_caractere(string busca, string esperado)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PG indisponível");
        var (db, empresaId) = await ProdutosAsync();
        await using var _ = db;

        var achados = await new ProdutoRepository(db).SearchAsync(empresaId, busca);

        achados.Select(p => p.Nome).Should().Equal(esperado);
    }

    [SkippableTheory]
    [InlineData("_", "Marca_festa")]
    [InlineData("%", "Leve 10%")]
    public async Task Filtro_de_marcas_procura_o_curinga_como_caractere(string filtro, string esperada)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PG indisponível");
        var (db, empresaId) = await ProdutosAsync();
        await using var _ = db;

        var marcas = await new ProdutoRepository(db).GetMarcasAsync(empresaId, filtro);

        marcas.Should().Equal(esperada);
    }

    [SkippableFact]
    public async Task Busca_de_cliente_por_sublinhado_nao_devolve_todos()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PG indisponível");
        await using var db = fixture.CreateDbContext();
        var empresaId = Empresa(db);
        db.Clientes.AddRange(Cliente.Criar(empresaId, "Maria"), Cliente.Criar(empresaId, "José_Silva"));
        await db.SaveChangesAsync();

        var achados = await new ClienteRepository(db).SearchAsync(empresaId, "_");

        achados.Select(c => c.Nome).Should().Equal("José_Silva");
    }

    [SkippableFact]
    public async Task Busca_de_fornecedor_por_sublinhado_nao_devolve_todos()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PG indisponível");
        await using var db = fixture.CreateDbContext();
        var empresaId = Empresa(db);
        db.Fornecedores.AddRange(Fornecedor.Criar(empresaId, "Atacadão"), Fornecedor.Criar(empresaId, "Doce_Mel"));
        await db.SaveChangesAsync();

        var achados = await new FornecedorRepository(db).SearchAsync(empresaId, "_");

        achados.Select(f => f.Nome).Should().Equal("Doce_Mel");
    }
}
