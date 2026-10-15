using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

public class ItemEstoqueRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task SearchAsync_deve_buscar_item_por_codigo_chave_variacao_e_descricao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        await using var context = fixture.CreateDbContext();
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        context.SetMobileTenantContext(empresaId);

        context.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa A", Documento = "555", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        context.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Audio", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        context.Produtos.Add(new Produto
        {
            Id = produtoId,
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Galaxy Buds FE",
            Tipo = TipoProduto.Fisico,
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });
        context.ItensEstoque.Add(new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            CodigoInterno = "CAP3426",
            CodigoMarketplace = "ML-ABC",
            ChavePesquisa = "CAP3426 BUDS-FE GALAXY BUDS FE",
            VariacaoDescricao = "Grafite",
            Cor = "Grafite",
            Tamanho = "Unico",
            DescricaoAnuncio = "Fone bluetooth buds fe grafite",
            QuantidadeInicial = Quantidade.From(10),
            QuantidadeAtual = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(250m),
            Status = StatusItemEstoque.Ok,
            EntradaEm = DateTime.UtcNow,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        var repository = new ItemEstoqueRepository(context);

        (await repository.SearchAsync(empresaId, "CAP3426")).Should().ContainSingle();
        (await repository.SearchAsync(empresaId, "ML-ABC")).Should().ContainSingle();
        (await repository.SearchAsync(empresaId, "grafite")).Should().ContainSingle();
        (await repository.SearchAsync(empresaId, "bluetooth")).Should().ContainSingle();
    }

    [SkippableFact]
    public async Task SearchAsync_deve_retornar_ordem_deterministica_em_chamadas_consecutivas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        await using var context = fixture.CreateDbContext();
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        context.SetMobileTenantContext(empresaId);

        context.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Paginacao", Documento = "777", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        context.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Eletronicos", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        context.Produtos.Add(new Produto { Id = produtoId, EmpresaId = empresaId, CategoriaId = categoriaId, Nome = "Produto Test", Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });

        for (var i = 1; i <= 5; i++)
        {
            context.ItensEstoque.Add(new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ProdutoId = produtoId,
                CodigoInterno = $"SKU-{i:D3}",
                ChavePesquisa = $"sku teste item {i:D3}",
                QuantidadeInicial = Quantidade.From(i),
                QuantidadeAtual = Quantidade.From(i),
                CustoUnitario = Dinheiro.FromDecimal(10m * i),
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync();

        var repository = new ItemEstoqueRepository(context);

        var primeira = (await repository.SearchAsync(empresaId, "sku teste", maxResults: 3)).Select(i => i.CodigoInterno).ToList();
        var segunda  = (await repository.SearchAsync(empresaId, "sku teste", maxResults: 3)).Select(i => i.CodigoInterno).ToList();

        primeira.Should().HaveCount(3);
        primeira.Should().Equal(segunda, "chamadas consecutivas devem retornar a mesma ordem determinística");
    }

    [SkippableFact]
    public async Task Deve_persistir_value_objects_do_item_estoque()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        await using (var context = fixture.CreateDbContext())
        {
            context.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa A", Documento = "666", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            context.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Audio", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            context.Produtos.Add(new Produto
            {
                Id = produtoId,
                EmpresaId = empresaId,
                CategoriaId = categoriaId,
                Nome = "Galaxy Buds FE",
                Tipo = TipoProduto.Fisico,
                Status = StatusProduto.Ativo,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            context.ItensEstoque.Add(new ItemEstoque
            {
                Id = itemId,
                EmpresaId = empresaId,
                ProdutoId = produtoId,
                CodigoLote = CodigoLote.From("LOTE-01"),
                DimensoesReais = Dimensoes.From(0.3m, 10.5m, 5.2m, 8.1m),
                QuantidadeInicial = Quantidade.From(12),
                QuantidadeAtual = Quantidade.From(7),
                CustoUnitario = Dinheiro.FromDecimal(250m),
                PrecoVendaSugerido = Dinheiro.FromDecimal(399.90m),
                ValidadeEm = Validade.From(new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc)),
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        await using (var context = fixture.CreateDbContext())
        {
            context.SetMobileTenantContext(empresaId);
            var repository = new ItemEstoqueRepository(context);
            var item = await repository.GetByIdAsync(itemId);

            item.Should().NotBeNull();
            item!.CodigoLote!.Value.Should().Be("LOTE-01");
            item.DimensoesReais!.Largura.Should().Be(10.5m);
            item.QuantidadeInicial.Value.Should().Be(12);
            item.QuantidadeAtual.Value.Should().Be(7);
            item.CustoUnitario.Valor.Should().Be(250m);
            item.PrecoVendaSugerido!.Valor.Should().Be(399.90m);
            item.ValidadeEm!.DataValidade.Should().Be(new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc));
        }
    }

    [SkippableFact]
    public async Task GetItensEstoquePaginadosAsync_deve_respeitar_tenant_por_empresaId()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        await using var context = fixture.CreateDbContext();
        var empresaA = new Empresa { Id = Guid.NewGuid(), Nome = "Empresa A", Documento = "111", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        var empresaB = new Empresa { Id = Guid.NewGuid(), Nome = "Empresa B", Documento = "222", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        var categoriaA = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresaA.Id, Nome = "Audio", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        var produtoA = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaA.Id,
            CategoriaId = categoriaA.Id,
            Nome = "Produto A",
            Tipo = TipoProduto.Fisico,
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };

        var categoriaB = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresaB.Id, Nome = "Audio B", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        var produtoB = new Produto { Id = Guid.NewGuid(), EmpresaId = empresaB.Id, CategoriaId = categoriaB.Id, Nome = "Produto B", Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        context.Empresas.AddRange(empresaA, empresaB);
        context.Categorias.AddRange(categoriaA, categoriaB);
        context.Produtos.AddRange(produtoA, produtoB);
        context.ItensEstoque.AddRange(
            new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaA.Id,
                ProdutoId = produtoA.Id,
                QuantidadeInicial = Quantidade.From(10),
                QuantidadeAtual = Quantidade.From(10),
                CustoUnitario = Dinheiro.FromDecimal(10m),
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            },
            new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaA.Id,
                ProdutoId = produtoA.Id,
                QuantidadeInicial = Quantidade.From(5),
                QuantidadeAtual = Quantidade.From(5),
                CustoUnitario = Dinheiro.FromDecimal(10m),
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            },
            new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaB.Id,
                ProdutoId = produtoB.Id,
                QuantidadeInicial = Quantidade.From(20),
                QuantidadeAtual = Quantidade.From(20),
                CustoUnitario = Dinheiro.FromDecimal(10m),
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });

        await context.SaveChangesAsync();

        var repository = new ItemEstoqueRepository(context);

        context.SetMobileTenantContext(empresaA.Id);
        var (itensA, totalA) = await repository.GetItensEstoquePaginadosAsync(empresaA.Id, 1, 10);
        context.SetMobileTenantContext(empresaB.Id);
        var (itensB, totalB) = await repository.GetItensEstoquePaginadosAsync(empresaB.Id, 1, 10);

        itensA.Should().HaveCount(2);
        itensA.Should().AllSatisfy(i => i.EmpresaId.Should().Be(empresaA.Id));
        totalA.Should().Be(2);

        itensB.Should().HaveCount(1);
        itensB.Should().AllSatisfy(i => i.EmpresaId.Should().Be(empresaB.Id));
        totalB.Should().Be(1);
    }

    [SkippableFact]
    public async Task GetItensEstoquePaginadosAsync_status_vencendo_retorna_so_lotes_na_janela_com_saldo_e_nao_vencidos()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        await using var context = fixture.CreateDbContext();
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        context.SetMobileTenantContext(empresaId);

        context.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa V", Documento = "999", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        context.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Pereciveis", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        context.Produtos.Add(new Produto
        {
            Id = produtoId,
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Queijo Fresco",
            Tipo = TipoProduto.Fisico,
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });

        var idVencendo = Guid.NewGuid();
        context.ItensEstoque.AddRange(
            // Vence em 3 dias, com saldo, nao vencido -> DEVE aparecer
            new ItemEstoque
            {
                Id = idVencendo,
                EmpresaId = empresaId,
                ProdutoId = produtoId,
                QuantidadeInicial = Quantidade.From(10),
                QuantidadeAtual = Quantidade.From(10),
                CustoUnitario = Dinheiro.FromDecimal(10m),
                ValidadeEm = Validade.From(DateTime.UtcNow.AddDays(3)),
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            },
            // Vence em 20 dias (fora da janela de 7) -> NAO aparece
            new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ProdutoId = produtoId,
                QuantidadeInicial = Quantidade.From(10),
                QuantidadeAtual = Quantidade.From(10),
                CustoUnitario = Dinheiro.FromDecimal(10m),
                ValidadeEm = Validade.From(DateTime.UtcNow.AddDays(20)),
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            },
            // Ja vencido (Status Vencido) -> NAO aparece (proativo: so o que ainda vai vencer)
            new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ProdutoId = produtoId,
                QuantidadeInicial = Quantidade.From(5),
                QuantidadeAtual = Quantidade.From(5),
                CustoUnitario = Dinheiro.FromDecimal(10m),
                ValidadeEm = Validade.From(DateTime.UtcNow.AddDays(-2)),
                Status = StatusItemEstoque.Vencido,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            },
            // Vence em 2 dias, mas sem saldo (qty 0) -> NAO aparece
            new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ProdutoId = produtoId,
                QuantidadeInicial = Quantidade.From(8),
                QuantidadeAtual = Quantidade.From(0),
                CustoUnitario = Dinheiro.FromDecimal(10m),
                ValidadeEm = Validade.From(DateTime.UtcNow.AddDays(2)),
                Status = StatusItemEstoque.Esgotado,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            },
            // Sem validade -> NAO aparece
            new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ProdutoId = produtoId,
                QuantidadeInicial = Quantidade.From(10),
                QuantidadeAtual = Quantidade.From(10),
                CustoUnitario = Dinheiro.FromDecimal(10m),
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });

        await context.SaveChangesAsync();

        var repository = new ItemEstoqueRepository(context);
        var (itens, total) = await repository.GetItensEstoquePaginadosAsync(empresaId, 1, 20, status: "vencendo");

        total.Should().Be(1);
        itens.Should().ContainSingle().Which.Id.Should().Be(idVencendo);
    }

    [SkippableFact]
    public async Task GetItensEstoquePaginadosAsync_termo_casa_por_sku_nome_e_ignora_outros_produtos()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        await using var context = fixture.CreateDbContext();
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoAlvo = Guid.NewGuid();
        var produtoOutro = Guid.NewGuid();
        context.SetMobileTenantContext(empresaId);

        context.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Busca", Documento = "111", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        context.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Geral", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        context.Produtos.AddRange(
            new Produto
            {
                Id = produtoAlvo,
                EmpresaId = empresaId,
                CategoriaId = categoriaId,
                Nome = "Caixa Organizadora",
                SkuBase = CodigoSku.From("ZFZW"),
                Tipo = TipoProduto.Fisico,
                Status = StatusProduto.Ativo,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            },
            new Produto
            {
                Id = produtoOutro,
                EmpresaId = empresaId,
                CategoriaId = categoriaId,
                Nome = "Vassoura",
                SkuBase = CodigoSku.From("VSSR"),
                Tipo = TipoProduto.Fisico,
                Status = StatusProduto.Ativo,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });

        var entrada = DateTime.UtcNow;
        // 2 lotes do produto alvo com a MESMA EntradaEm -> exercita o tie-breaker por Id (#454)
        context.ItensEstoque.AddRange(
            new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ProdutoId = produtoAlvo,
                QuantidadeInicial = Quantidade.From(10),
                QuantidadeAtual = Quantidade.From(10),
                CustoUnitario = Dinheiro.FromDecimal(10m),
                ChavePesquisa = "ZFZW CAIXA ORGANIZADORA",
                Status = StatusItemEstoque.Ok,
                EntradaEm = entrada,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            },
            new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ProdutoId = produtoAlvo,
                QuantidadeInicial = Quantidade.From(5),
                QuantidadeAtual = Quantidade.From(5),
                CustoUnitario = Dinheiro.FromDecimal(10m),
                ChavePesquisa = "ZFZW CAIXA ORGANIZADORA",
                Status = StatusItemEstoque.Ok,
                EntradaEm = entrada,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            },
            // Lote de OUTRO produto -> nao casa "ZFZW"
            new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ProdutoId = produtoOutro,
                QuantidadeInicial = Quantidade.From(7),
                QuantidadeAtual = Quantidade.From(7),
                CustoUnitario = Dinheiro.FromDecimal(10m),
                ChavePesquisa = "VSSR VASSOURA",
                Status = StatusItemEstoque.Ok,
                EntradaEm = entrada,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });

        await context.SaveChangesAsync();

        var repository = new ItemEstoqueRepository(context);

        // SKU base exato e case-insensitive (o termo minusculo normaliza para o SKU armazenado)
        var (porSku, totalSku) = await repository.GetItensEstoquePaginadosAsync(empresaId, 1, 20, termo: "zfzw");
        totalSku.Should().Be(2);
        porSku.Should().OnlyContain(i => i.ProdutoId == produtoAlvo);

        // Nome parcial (ILIKE)
        var (_, totalNome) = await repository.GetItensEstoquePaginadosAsync(empresaId, 1, 20, termo: "organiz");
        totalNome.Should().Be(2);

        // Termo que nao casa nada
        var (vazio, totalVazio) = await repository.GetItensEstoquePaginadosAsync(empresaId, 1, 20, termo: "naoexiste");
        totalVazio.Should().Be(0);
        vazio.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task GetLotesDisponiveisParaSaidaAsync_por_padrao_exclui_lote_vencido()
    {
        // #983 (D1): antes do fix, a query FEFO ordenava o lote vencido ANTES do valido
        // (ValidadeEm mais antigo primeiro) sem filtrar — o caller batia nele primeiro e
        // a saida inteira caia com ItemEstoqueVencidoException, mesmo havendo saldo valido
        // suficiente. Por padrao (incluirVencidos=false) o vencido nao deve nem aparecer.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        await using var context = fixture.CreateDbContext();
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        context.SetMobileTenantContext(empresaId);

        context.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Vencido", Documento = "111", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        context.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Perecivel", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        context.Produtos.Add(new Produto
        {
            Id = produtoId,
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Queijo Fresco",
            Tipo = TipoProduto.Fisico,
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });

        var idValido = Guid.NewGuid();
        context.ItensEstoque.AddRange(
            // Vencido ha 5 dias — ValidadeEm mais antigo, viria primeiro no FEFO se nao filtrado
            new ItemEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = empresaId,
                ProdutoId = produtoId,
                QuantidadeInicial = Quantidade.From(10),
                QuantidadeAtual = Quantidade.From(10),
                CustoUnitario = Dinheiro.FromDecimal(10m),
                ValidadeEm = Validade.From(DateTime.UtcNow.AddDays(-5)),
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow.AddDays(-10),
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            },
            // Valido, vence em 30 dias
            new ItemEstoque
            {
                Id = idValido,
                EmpresaId = empresaId,
                ProdutoId = produtoId,
                QuantidadeInicial = Quantidade.From(8),
                QuantidadeAtual = Quantidade.From(8),
                CustoUnitario = Dinheiro.FromDecimal(10m),
                ValidadeEm = Validade.From(DateTime.UtcNow.AddDays(30)),
                Status = StatusItemEstoque.Ok,
                EntradaEm = DateTime.UtcNow.AddDays(-1),
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });

        await context.SaveChangesAsync();

        var repository = new ItemEstoqueRepository(context);

        var padrao = await repository.GetLotesDisponiveisParaSaidaAsync(empresaId, produtoId, null);
        padrao.Should().ContainSingle().Which.Id.Should().Be(idValido);

        var comVencido = await repository.GetLotesDisponiveisParaSaidaAsync(empresaId, produtoId, null, incluirVencidos: true);
        comVencido.Should().HaveCount(2, "incluirVencidos=true e usado pelas naturezas de baixa/descarte (#983)");
    }
    [SkippableFact]
    public async Task GetSaldoDisponivelPorProdutosAsync_soma_lotes_validos_do_tenant_numa_consulta()
    {
        // #1171: cardapio publico projeta o saldo produzido. Soma so lote com saldo e nao
        // vencido, do tenant informado, e ignora o filtro global (caller anonimo).
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        await using var context = fixture.CreateDbContext();
        var empresaId = Guid.NewGuid();
        var outraEmpresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var outraCategoriaId = Guid.NewGuid();
        var lasanhaId = Guid.NewGuid();
        var nhoqueId = Guid.NewGuid();
        var semSaldoId = Guid.NewGuid();
        var produtoOutraEmpresaId = Guid.NewGuid();
        context.SetMobileTenantContext(empresaId);

        context.Empresas.AddRange(
            new Empresa { Id = empresaId, Nome = "Empresa Saldo", Documento = "222", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow },
            new Empresa { Id = outraEmpresaId, Nome = "Outra", Documento = "333", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        context.Categorias.AddRange(
            new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Pratos", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow },
            new Categoria { Id = outraCategoriaId, EmpresaId = outraEmpresaId, Nome = "Pratos", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        context.Produtos.AddRange(
            NovoProduto(lasanhaId, empresaId, categoriaId, "Lasanha"),
            NovoProduto(nhoqueId, empresaId, categoriaId, "Nhoque"),
            NovoProduto(semSaldoId, empresaId, categoriaId, "Torta"),
            NovoProduto(produtoOutraEmpresaId, outraEmpresaId, outraCategoriaId, "Lasanha alheia"));
        context.ItensEstoque.AddRange(
            NovoLote(empresaId, lasanhaId, 5m, validadeEmDias: 3),
            NovoLote(empresaId, lasanhaId, 7m, validadeEmDias: null),
            NovoLote(empresaId, lasanhaId, 4m, validadeEmDias: -2),   // vencido: fora
            NovoLote(empresaId, nhoqueId, 2.5m, validadeEmDias: 10),
            NovoLote(empresaId, semSaldoId, 0m, validadeEmDias: 10),  // zerado: fora
            NovoLote(outraEmpresaId, produtoOutraEmpresaId, 9m, validadeEmDias: 10));

        await context.SaveChangesAsync();

        // Contexto novo sem tenant (como a request anonima do cardapio publico).
        await using var anonimo = fixture.CreateDbContext();
        var repository = new ItemEstoqueRepository(anonimo);

        var saldos = await repository.GetSaldoDisponivelPorProdutosAsync(
            empresaId, new[] { lasanhaId, nhoqueId, semSaldoId, produtoOutraEmpresaId });

        saldos.Should().HaveCount(2);
        saldos[lasanhaId].Should().Be(12m, "5 + 7; o lote vencido nao entra");
        saldos[nhoqueId].Should().Be(2.5m);
        saldos.Should().NotContainKey(semSaldoId);
        saldos.Should().NotContainKey(produtoOutraEmpresaId, "EmpresaId no WHERE isola o tenant");
    }

    private static Produto NovoProduto(Guid id, Guid empresaId, Guid categoriaId, string nome) => new()
    {
        Id = id,
        EmpresaId = empresaId,
        CategoriaId = categoriaId,
        Nome = nome,
        Tipo = TipoProduto.Alimento,
        Status = StatusProduto.Ativo,
        CriadoEm = DateTime.UtcNow,
        AlteradoEm = DateTime.UtcNow
    };

    private static ItemEstoque NovoLote(Guid empresaId, Guid produtoId, decimal quantidade, int? validadeEmDias) => new()
    {
        Id = Guid.NewGuid(),
        EmpresaId = empresaId,
        ProdutoId = produtoId,
        QuantidadeInicial = Quantidade.From(Math.Max(quantidade, 1m)),
        QuantidadeAtual = Quantidade.From(quantidade),
        CustoUnitario = Dinheiro.FromDecimal(10m),
        ValidadeEm = validadeEmDias is null ? null : Validade.From(DateTime.UtcNow.AddDays(validadeEmDias.Value)),
        Status = StatusItemEstoque.Ok,
        EntradaEm = DateTime.UtcNow.AddDays(-1),
        CriadoEm = DateTime.UtcNow,
        AlteradoEm = DateTime.UtcNow
    };
}
