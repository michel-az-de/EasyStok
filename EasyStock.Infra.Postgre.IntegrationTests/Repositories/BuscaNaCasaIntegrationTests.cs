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
            var listaClientes = await clientes.GetByEmpresaAsync(empresa.Id, 1, 20, search: termo);
            listaClientes.items.Should().ContainSingle().Which.Id.Should().Be(cliente.Id);
            listaClientes.total.Should().Be(1);
            (await pedidos.GetByEmpresaAsync(empresa.Id, 1, 20, search: termo)).items.Should().ContainSingle().Which.Id.Should().Be(pedido.Id);
            (await conversas.ListarInboxAsync(empresa.Id, null, termo, null, 1, 10)).Should().ContainSingle().Which.Id.Should().Be(conversa.Id);
        }
        foreach (var termo in new[] { "pao acucar", "PÃO AÇÚCAR", produto.Id.ToString() })
            (await produtos.SearchAsync(empresa.Id, termo)).Should().ContainSingle().Which.Id.Should().Be(produto.Id);
        // Cadastros usa /produtos/search; o status continua filtrando a pesquisa normalizada.
        (await produtos.SearchAsync(empresa.Id, "pao acucar", status: StatusProduto.Ativo)).Should().ContainSingle().Which.Id.Should().Be(produto.Id);
        (await produtos.SearchAsync(empresa.Id, "pao acucar", status: StatusProduto.Inativo)).Should().BeEmpty();
        (await clientes.SearchAsync(empresa.Id, cliente.Id.ToString())).Should().ContainSingle().Which.Id.Should().Be(cliente.Id);
        (await clientes.GetByEmpresaAsync(empresa.Id, 1, 20, search: cliente.Id.ToString())).items.Should().ContainSingle().Which.Id.Should().Be(cliente.Id);
        (await pedidos.GetByEmpresaAsync(empresa.Id, 1, 20, search: pedido.Id.ToString())).items.Should().ContainSingle().Which.Id.Should().Be(pedido.Id);
        (await pedidos.GetByEmpresaAsync(empresa.Id, 1, 20, search: "#42")).items.Should().ContainSingle().Which.Id.Should().Be(pedido.Id);
        (await conversas.ListarInboxAsync(empresa.Id, null, conversa.Id.ToString(), null, 1, 10)).Should().ContainSingle().Which.Id.Should().Be(conversa.Id);
        (await clientes.SearchAsync(empresa.Id, alheio.Id.ToString())).Should().BeEmpty();
        var listaAlheia = await clientes.GetByEmpresaAsync(empresa.Id, 1, 20, search: alheio.Id.ToString());
        listaAlheia.items.Should().BeEmpty();
        listaAlheia.total.Should().Be(0);
        (await produtos.SearchAsync(empresa.Id, produtoAlheio.Id.ToString())).Should().BeEmpty();
        (await pedidos.GetByEmpresaAsync(empresa.Id, 1, 20, search: pedidoAlheio.Id.ToString())).items.Should().BeEmpty();
        (await conversas.ListarInboxAsync(empresa.Id, null, conversaAlheia.Id.ToString(), null, 1, 10)).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task ListaLocalClientesNormalizaTermoSemPerderFiltrosPaginacaoOuTenant()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Empresa.Criar("Lista local R4", "33333333000191");
        var outra = Empresa.Criar("Lista alheia R4", "44444444000191");
        var primeiroLead = Cliente.Criar(empresa.Id, "Revisão móvel R4 A");
        primeiroLead.AtualizarCadastro(primeiroLead.Nome, "Bloco São José", null,
            "+55 (11) 99999-1234", "contato-r4@example.test", "11122233344", null);
        primeiroLead.NomeFantasia = "Açúcar Comércio";
        var segundoLead = Cliente.Criar(empresa.Id, "Revisão móvel R4 B");
        var comPedido = Cliente.Criar(empresa.Id, "Revisão móvel R4 C");
        var inativo = Cliente.Criar(empresa.Id, "Revisão móvel R4 D");
        inativo.Ativo = false;
        var alheio = Cliente.Criar(outra.Id, "Revisão móvel R4 A");
        var pedido = Pedido.Criar(empresa.Id, comPedido);
        await using (var gravar = fixture.CreateDbContext())
        {
            using var bypass = gravar.UseRowLevelSecurityBypass();
            await gravar.Database.MigrateAsync();
            gravar.Empresas.AddRange(empresa, outra);
            gravar.Clientes.AddRange(primeiroLead, segundoLead, comPedido, inativo, alheio);
            gravar.Pedidos.Add(pedido);
            await gravar.SaveChangesAsync();
        }
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa.Id);
        var clientes = new ClienteRepository(db);
        foreach (var termo in new[] { "Revisão móvel R4", "revisao movel r4", "  REVISÃO MÓVEL R4  " })
        {
            var lista = await clientes.GetByEmpresaAsync(empresa.Id, 1, 20, search: termo);
            lista.total.Should().Be(4);
            lista.items.Select(c => c.Id).Should().BeEquivalentTo(new[] { primeiroLead.Id, segundoLead.Id, comPedido.Id, inativo.Id });
        }
        var primeira = await clientes.GetByEmpresaAsync(empresa.Id, 1, 1, ativo: true, search: "revisao movel r4", relacionamento: "lead");
        var segunda = await clientes.GetByEmpresaAsync(empresa.Id, 2, 1, ativo: true, search: "revisao movel r4", relacionamento: "lead");
        var terceira = await clientes.GetByEmpresaAsync(empresa.Id, 3, 1, ativo: true, search: "revisao movel r4", relacionamento: "lead");
        primeira.total.Should().Be(2);
        segunda.total.Should().Be(2);
        terceira.total.Should().Be(2);
        primeira.items.Should().ContainSingle().Which.Id.Should().Be(primeiroLead.Id);
        segunda.items.Should().ContainSingle().Which.Id.Should().Be(segundoLead.Id);
        terceira.items.Should().BeEmpty();
        var descendente = await clientes.GetByEmpresaAsync(empresa.Id, 1, 1, ativo: true, search: "revisao movel r4", order: "desc", relacionamento: "lead");
        descendente.items.Should().ContainSingle().Which.Id.Should().Be(segundoLead.Id);
        var comCompra = await clientes.GetByEmpresaAsync(empresa.Id, 1, 20, ativo: true, search: "revisao movel r4", relacionamento: "cliente");
        comCompra.total.Should().Be(1);
        comCompra.items.Should().ContainSingle().Which.Id.Should().Be(comPedido.Id);
        var desativados = await clientes.GetByEmpresaAsync(empresa.Id, 1, 20, ativo: false, search: "revisao movel r4", relacionamento: "lead");
        desativados.total.Should().Be(1);
        desativados.items.Should().ContainSingle().Which.Id.Should().Be(inativo.Id);
        foreach (var termo in new[] { primeiroLead.Id.ToString().ToUpperInvariant(), "acucar comercio", "BLOCO SAO JOSE",
            "CONTATO-R4@EXAMPLE.TEST", "11122233344", "11999991234", "(11) 99999-1234", "+55 (11) 99999-1234" })
        {
            var lista = await clientes.GetByEmpresaAsync(empresa.Id, 1, 20, search: termo);
            lista.total.Should().Be(1);
            lista.items.Should().ContainSingle().Which.Id.Should().Be(primeiroLead.Id);
        }
        var porIdAlheio = await clientes.GetByEmpresaAsync(empresa.Id, 1, 20, search: alheio.Id.ToString());
        porIdAlheio.total.Should().Be(0);
        porIdAlheio.items.Should().BeEmpty();
        var idNaoFuraFiltro = await clientes.GetByEmpresaAsync(empresa.Id, 1, 20, ativo: true, search: inativo.Id.ToString());
        idNaoFuraFiltro.total.Should().Be(0);
        idNaoFuraFiltro.items.Should().BeEmpty();
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
