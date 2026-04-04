using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AutenticarUsuario;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Application.UseCases.ReporEstoque;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.MongoDb.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EasyStock.Infra.MongoDb.IntegrationTests.Workflows;

public class MongoInfrastructureIntegrationTests(MongoDbFixture fixture) : IClassFixture<MongoDbFixture>
{
    [Fact]
    public async Task Migration_runner_deve_criar_indices_essenciais()
    {
        if (!fixture.IsAvailable) return;
        await fixture.ResetDatabaseAsync();

        await using var provider = fixture.CreateServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();

        var usuariosIndexes = await context.Database.GetCollection<BsonDocument>("usuarios").Indexes.ListAsync();
        var itensIndexes = await context.Database.GetCollection<BsonDocument>("itens_estoque").Indexes.ListAsync();

        var usuarios = await usuariosIndexes.ToListAsync();
        var itens = await itensIndexes.ToListAsync();

        usuarios.Should().Contain(x => x["name"] == "ux_usuarios_email");
        itens.Should().Contain(x => x["name"] == "ix_itens_empresa");
        itens.Should().Contain(x => x["name"] == "ix_itens_codigo_interno");
        itens.Should().Contain(x => x["name"] == "ix_itens_codigo_marketplace");
    }

    [Fact]
    public async Task RegistrarEntrada_deve_persistir_item_movimentacao_e_chave_pesquisa_no_mongo()
    {
        if (!fixture.IsAvailable) return;
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();

        await SeedProdutoAsync(empresaId, categoriaId, produtoId);

        await using (var provider = fixture.CreateServiceProvider())
        {
            using var scope = provider.CreateScope();
            var useCase = scope.ServiceProvider.GetRequiredService<RegistrarEntradaEstoqueUseCase>();

            var result = await useCase.ExecuteAsync(new RegistrarEntradaEstoqueCommand(
                empresaId,
                produtoId,
                null,
                8,
                210m,
                399.90m,
                new DateTime(2026, 4, 4, 12, 0, 0, DateTimeKind.Utc),
                NaturezaMovimentacaoEstoque.Compra,
                "CAP3426",
                "LOTE-MONGO-01",
                "MKT-123",
                "Grafite",
                "Grafite",
                "Unico",
                "Fornecedor Mongo",
                null,
                "Entrada inicial",
                null,
                "DOC-MONGO-01",
                new DimensoesInput(0.2m, 10m, 5m, 8m),
                "Mercado Livre"));

            result.ChavePesquisa.Should().Contain("CAP3426");
        }

        await using var assertProvider = fixture.CreateServiceProvider();
        using var assertScope = assertProvider.CreateScope();
        var context = assertScope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();

        var item = await context.GetCollection<ItemEstoque>("itens_estoque").Find(FilterDefinition<ItemEstoque>.Empty).SingleAsync();
        var movimentacao = await context.GetCollection<MovimentacaoEstoque>("movimentacoes_estoque").Find(FilterDefinition<MovimentacaoEstoque>.Empty).SingleAsync();

        item.CodigoInterno.Should().Be("CAP3426");
        item.CodigoLote!.Value.Should().Be("LOTE-MONGO-01");
        item.QuantidadeAtual.Value.Should().Be(8);
        item.ChavePesquisa.Should().Contain("CAP3426");
        movimentacao.Tipo.Should().Be(TipoMovimentacaoEstoque.Entrada);
        movimentacao.Quantidade.Value.Should().Be(8);
    }

    [Fact]
    public async Task RegistrarSaida_deve_persistir_venda_item_venda_movimentacao_e_baixa_no_mongo()
    {
        if (!fixture.IsAvailable) return;
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        await SeedProdutoAsync(empresaId, categoriaId, produtoId);
        await SeedItemEstoqueAsync(empresaId, produtoId, itemId, 10, 250m, "Buds FE Grafite");

        await using (var provider = fixture.CreateServiceProvider())
        {
            using var scope = provider.CreateScope();
            var useCase = scope.ServiceProvider.GetRequiredService<RegistrarSaidaEstoqueUseCase>();

            var result = await useCase.ExecuteAsync(new RegistrarSaidaEstoqueCommand(
                empresaId,
                [new RegistrarSaidaEstoqueItemCommand(itemId, 3, 399.90m, "Venda Mongo")],
                new DateTime(2026, 4, 4, 14, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 4, 14, 5, 0, DateTimeKind.Utc),
                null,
                "NF-MONGO-01",
                NaturezaMovimentacaoEstoque.Venda,
                CanalVenda.MercadoLivre,
                "Venda de teste"));

            result.ValorTotal.Should().Be(1199.70m);
        }

        await using var assertProvider = fixture.CreateServiceProvider();
        using var assertScope = assertProvider.CreateScope();
        var context = assertScope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();

        var item = await context.GetCollection<ItemEstoque>("itens_estoque").Find(x => x.Id == itemId).SingleAsync();
        var vendas = await context.GetCollection<Venda>("vendas").Find(FilterDefinition<Venda>.Empty).ToListAsync();
        var itensVenda = await context.GetCollection<ItemVenda>("itens_venda").Find(FilterDefinition<ItemVenda>.Empty).ToListAsync();
        var movimentacoes = await context.GetCollection<MovimentacaoEstoque>("movimentacoes_estoque").Find(FilterDefinition<MovimentacaoEstoque>.Empty).ToListAsync();

        item.QuantidadeAtual.Value.Should().Be(7);
        vendas.Should().HaveCount(1);
        itensVenda.Should().HaveCount(1);
        movimentacoes.Should().HaveCount(1);
        movimentacoes.Single().Tipo.Should().Be(TipoMovimentacaoEstoque.Saida);
    }

    [Fact]
    public async Task ReporEstoque_deve_atualizar_quantidade_e_gerar_movimentacao_no_mongo()
    {
        if (!fixture.IsAvailable) return;
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        await SeedProdutoAsync(empresaId, categoriaId, produtoId);
        await SeedItemEstoqueAsync(empresaId, produtoId, itemId, 5, 200m, "Item reposicao");

        await using (var provider = fixture.CreateServiceProvider())
        {
            using var scope = provider.CreateScope();
            var useCase = scope.ServiceProvider.GetRequiredService<ReporEstoqueUseCase>();

            await useCase.ExecuteAsync(new ReporEstoqueCommand(
                empresaId,
                itemId,
                4,
                205m,
                420m,
                new DateTime(2026, 4, 5, 10, 0, 0, DateTimeKind.Utc),
                "Grafite",
                "Grafite",
                "Unico",
                "Reposicao Mongo",
                "DOC-REPO-MONGO",
                null,
                null));
        }

        await using var assertProvider = fixture.CreateServiceProvider();
        using var assertScope = assertProvider.CreateScope();
        var context = assertScope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();

        var item = await context.GetCollection<ItemEstoque>("itens_estoque").Find(x => x.Id == itemId).SingleAsync();
        var movimentacao = await context.GetCollection<MovimentacaoEstoque>("movimentacoes_estoque").Find(FilterDefinition<MovimentacaoEstoque>.Empty).SingleAsync();

        item.QuantidadeAtual.Value.Should().Be(9);
        item.CustoUnitario.Valor.Should().Be(205m);
        item.PrecoVendaSugerido!.Valor.Should().Be(420m);
        movimentacao.Natureza.Should().Be(NaturezaMovimentacaoEstoque.Reposicao);
        movimentacao.Quantidade.Value.Should().Be(4);
    }

    [Fact]
    public async Task AutenticarUsuario_deve_atualizar_ultimo_acesso_no_mongo()
    {
        if (!fixture.IsAvailable) return;
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var usuarioId = Guid.NewGuid();
        var perfilId = Guid.NewGuid();

        await SeedUsuarioAsync(empresaId, usuarioId, perfilId);

        await using (var provider = fixture.CreateServiceProvider())
        {
            using var scope = provider.CreateScope();
            var useCase = scope.ServiceProvider.GetRequiredService<AutenticarUsuarioUseCase>();

            var result = await useCase.ExecuteAsync(new AutenticarUsuarioCommand(
                "mongo@easystock.com",
                "123456",
                empresaId));

            result.UsuarioId.Should().Be(usuarioId);
            result.EmpresaId.Should().Be(empresaId);
        }

        await using var assertProvider = fixture.CreateServiceProvider();
        using var assertScope = assertProvider.CreateScope();
        var usuarioRepository = assertScope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var usuario = await usuarioRepository.GetByIdAsync(usuarioId);

        usuario.Should().NotBeNull();
        usuario!.UltimoAcessoEm.Should().NotBeNull();
    }

    private async Task SeedProdutoAsync(Guid empresaId, Guid categoriaId, Guid produtoId)
    {
        await using var provider = fixture.CreateServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();

        await context.GetCollection<Empresa>("empresas").InsertOneAsync(new Empresa
        {
            Id = empresaId,
            Nome = "Empresa Mongo",
            Documento = $"{Random.Shared.Next(100000, 999999)}",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });

        await context.GetCollection<Categoria>("categorias").InsertOneAsync(new Categoria
        {
            Id = categoriaId,
            EmpresaId = empresaId,
            Nome = "Audio",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });

        await context.GetCollection<Produto>("produtos").InsertOneAsync(new Produto
        {
            Id = produtoId,
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Galaxy Buds FE",
            Tipo = TipoProduto.Fisico,
            Status = StatusProduto.Ativo,
            PrecoReferencia = Dinheiro.FromDecimal(399.90m),
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });
    }

    private async Task SeedItemEstoqueAsync(Guid empresaId, Guid produtoId, Guid itemId, int quantidade, decimal custoUnitario, string descricaoAnuncio)
    {
        await using var provider = fixture.CreateServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();

        await context.GetCollection<ItemEstoque>("itens_estoque").InsertOneAsync(new ItemEstoque
        {
            Id = itemId,
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            QuantidadeInicial = Quantidade.From(quantidade),
            QuantidadeAtual = Quantidade.From(quantidade),
            CustoUnitario = Dinheiro.FromDecimal(custoUnitario),
            Status = StatusItemEstoque.Ativo,
            EntradaEm = DateTime.UtcNow,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow,
            DescricaoAnuncio = descricaoAnuncio
        });
    }

    private async Task SeedUsuarioAsync(Guid empresaId, Guid usuarioId, Guid perfilId)
    {
        await using var provider = fixture.CreateServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();

        await context.GetCollection<Empresa>("empresas").InsertOneAsync(new Empresa
        {
            Id = empresaId,
            Nome = "Empresa Usuario Mongo",
            Documento = $"{Random.Shared.Next(100000, 999999)}",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });

        await context.GetCollection<Usuario>("usuarios").InsertOneAsync(new Usuario
        {
            Id = usuarioId,
            Nome = "Usuario Mongo",
            Email = "mongo@easystock.com",
            SenhaHash = BCrypt.Net.BCrypt.HashPassword("123456"),
            Ativo = true,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });

        await context.GetCollection<UsuarioEmpresa>("usuarios_empresas").InsertOneAsync(new UsuarioEmpresa
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            EmpresaId = empresaId,
            Ativo = true,
            CriadoEm = DateTime.UtcNow
        });

        await context.GetCollection<Perfil>("perfis").InsertOneAsync(new Perfil
        {
            Id = perfilId,
            EmpresaId = empresaId,
            Nome = "Admin",
            Nivel = NivelAcesso.Admin,
            CriadoEm = DateTime.UtcNow
        });

        await context.GetCollection<UsuarioPerfil>("usuarios_perfis").InsertOneAsync(new UsuarioPerfil
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            EmpresaId = empresaId,
            PerfilId = perfilId,
            AtribuidoEm = DateTime.UtcNow
        });
    }
}
