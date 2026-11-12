using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

public sealed class BuscaNaCasaIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task BuscaAcentosTelefonesEIdsNoBancoSemMisturarEmpresas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Empresa.Criar("Busca R4", "11111111000191");
        var outra = Empresa.Criar("Outra busca R4", "22222222000191");
        var cliente = Pessoa(empresa.Id);
        var alheio = Pessoa(outra.Id);
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Pratos" };
        var categoriaAlheia = new Categoria { Id = Guid.NewGuid(), EmpresaId = outra.Id, Nome = "Pratos" };
        var produto = ProdutoDaBusca(empresa.Id, categoria.Id);
        var produtoAlheio = ProdutoDaBusca(outra.Id, categoriaAlheia.Id);
        var pedido = Pedido.Criar(empresa.Id, cliente);
        var pedidoAlheio = Pedido.Criar(outra.Id, alheio);
        pedido.DefinirNumeroDoDia(new DateOnly(2026, 11, 12), 42);
        var conversa = Conversa.Abrir(empresa.Id, "5511999991234", DateTime.UtcNow, "João Açúcar", cliente.Id);
        var conversaAlheia = Conversa.Abrir(outra.Id, "5511999991234", DateTime.UtcNow, "João Açúcar", alheio.Id);
        await using (var gravar = fixture.CreateDbContext())
        {
            using var bypass = gravar.UseRowLevelSecurityBypass();
            await gravar.Database.MigrateAsync();
            gravar.Empresas.AddRange(empresa, outra);
            gravar.Clientes.AddRange(cliente, alheio);
            gravar.Categorias.AddRange(categoria, categoriaAlheia);
            gravar.Produtos.AddRange(produto, produtoAlheio);
            gravar.Pedidos.AddRange(pedido, pedidoAlheio);
            gravar.AtendimentoConversas.AddRange(conversa, conversaAlheia);
            await gravar.SaveChangesAsync();
        }
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa.Id);
        var clientes = new ClienteRepository(db);
        var produtos = new ProdutoRepository(db);
        var pedidos = new PedidoRepository(db);
        var conversas = new ConversaRepository(db);
        foreach (var termo in new[] { "joao acucar", "JOÃO AÇÚCAR", "11999991234", "(11) 99999-1234", "+55 (11) 99999-1234" })
        {
            (await clientes.SearchAsync(empresa.Id, termo)).Should().ContainSingle().Which.Id.Should().Be(cliente.Id);
            (await pedidos.GetByEmpresaAsync(empresa.Id, 1, 20, search: termo)).items.Should().ContainSingle().Which.Id.Should().Be(pedido.Id);
            (await conversas.ListarInboxAsync(empresa.Id, null, termo, null, 1, 10)).Should().ContainSingle().Which.Id.Should().Be(conversa.Id);
        }
        foreach (var termo in new[] { "pao acucar", "PÃO AÇÚCAR", produto.Id.ToString() })
            (await produtos.SearchAsync(empresa.Id, termo)).Should().ContainSingle().Which.Id.Should().Be(produto.Id);
        (await clientes.SearchAsync(empresa.Id, cliente.Id.ToString())).Should().ContainSingle().Which.Id.Should().Be(cliente.Id);
        (await pedidos.GetByEmpresaAsync(empresa.Id, 1, 20, search: pedido.Id.ToString())).items.Should().ContainSingle().Which.Id.Should().Be(pedido.Id);
        (await pedidos.GetByEmpresaAsync(empresa.Id, 1, 20, search: "#42")).items.Should().ContainSingle().Which.Id.Should().Be(pedido.Id);
        (await conversas.ListarInboxAsync(empresa.Id, null, conversa.Id.ToString(), null, 1, 10)).Should().ContainSingle().Which.Id.Should().Be(conversa.Id);
        (await clientes.SearchAsync(empresa.Id, alheio.Id.ToString())).Should().BeEmpty();
        (await produtos.SearchAsync(empresa.Id, produtoAlheio.Id.ToString())).Should().BeEmpty();
        (await pedidos.GetByEmpresaAsync(empresa.Id, 1, 20, search: pedidoAlheio.Id.ToString())).items.Should().BeEmpty();
        (await conversas.ListarInboxAsync(empresa.Id, null, conversaAlheia.Id.ToString(), null, 1, 10)).Should().BeEmpty();
    }

    private static Cliente Pessoa(Guid empresa)
    {
        var cliente = Cliente.Criar(empresa, "João Açúcar");
        cliente.AtualizarCadastro(cliente.Nome, null, null, "+55 (11) 99999-1234", null, null, null);
        return cliente;
    }

    private static Produto ProdutoDaBusca(Guid empresa, Guid categoria) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = empresa, CategoriaId = categoria,
        Nome = "Pão Açúcar", Tipo = TipoProduto.Alimento, Status = StatusProduto.Ativo,
    };
}
