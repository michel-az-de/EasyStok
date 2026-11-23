using System.Data.Common;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #3244 (S1): a lista de Cadastros devolve a relação de cada cliente num banco real. Dados fictícios.
/// Compra é a <see cref="RegraDeCompra"/>; o gasto da carteira é a soma de todas as compras da empresa.
/// </summary>
public sealed class ListaRelacaoClienteIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static readonly DateTime Agora = DateTime.UtcNow;

    private sealed class Carteira
    {
        public Empresa A = Empresa.Criar("Relacao 3244 A", Documento());
        public Empresa B = Empresa.Criar("Relacao 3244 B", Documento());
        private static string Documento() => Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        public Cliente Ana = null!, Bia = null!, Cida = null!, Duda = null!, Eva = null!, Fabi = null!, Zeca = null!;
        public List<Pedido> Pedidos = [];
        public List<object> Outros = [];
    }

    private static Pedido Compra(Carteira c, Empresa empresa, Cliente cliente, decimal total, int diasAtras,
        string status = StatusPedidoMapper.Aguardando)
    {
        var pedido = Pedido.Criar(empresa.Id, cliente);
        pedido.Status = status;
        pedido.Total = Dinheiro.FromDecimal(total);
        pedido.CriadoEm = Agora.AddDays(-diasAtras);
        c.Pedidos.Add(pedido);
        return pedido;
    }

    private static Carteira Montar()
    {
        var c = new Carteira();
        Cliente Novo(Empresa e, string nome) => Cliente.Criar(e.Id, "Relacao 3244 " + nome);
        c.Ana = Novo(c.A, "Ana"); c.Bia = Novo(c.A, "Bia"); c.Cida = Novo(c.A, "Cida");
        c.Duda = Novo(c.A, "Duda"); c.Eva = Novo(c.A, "Eva"); c.Fabi = Novo(c.A, "Fabi"); c.Zeca = Novo(c.B, "Zeca");

        Compra(c, c.A, c.Ana, 100m, 100);
        Compra(c, c.A, c.Ana, 50.50m, 50);
        Compra(c, c.A, c.Ana, 49.50m, 10);
        var cancelado = Compra(c, c.A, c.Ana, 999m, 5);
        cancelado.Cancelar();
        Compra(c, c.A, c.Ana, 777m, 4, StatusPedidoMapper.Rascunho);
        Compra(c, c.A, c.Ana, 555m, 3, StatusPedidoMapper.AguardandoPagamento);
        Compra(c, c.A, c.Bia, 10m, 90);
        foreach (var dias in new[] { 200, 150, 100, 70, 40 }) Compra(c, c.A, c.Duda, 10m, dias);
        Compra(c, c.A, c.Eva, 10m, 60);
        Compra(c, c.A, c.Eva, 10m, 45);
        Compra(c, c.A, c.Fabi, 10m, 60);
        Compra(c, c.A, c.Fabi, 10m, 5);
        Compra(c, c.B, c.Zeca, 1000m, 8);

        // Ana: uma ocorrência aberta e uma resolvida, duas notas e quatro tags.
        var aberta = Ocorrencia.Abrir(c.A.Id, cancelado.Id, c.Ana.Id, null, OrigemOcorrencia.Dona, CategoriaOcorrencia.Atraso, "Atrasou", Agora);
        var resolvida = Ocorrencia.Abrir(c.A.Id, cancelado.Id, c.Ana.Id, null, OrigemOcorrencia.Dona, CategoriaOcorrencia.Outro, "Outro", Agora);
        resolvida.Resolver("Feito", Guid.NewGuid(), Agora);
        c.Outros.AddRange([aberta, resolvida]);
        c.Outros.Add(ClienteNota.Criar(c.A.Id, c.Ana.Id, "Nota antiga", "Equipe", Agora.AddDays(-20)));
        c.Outros.Add(ClienteNota.Criar(c.A.Id, c.Ana.Id, new string('x', 200), "Equipe", Agora.AddDays(-1)));
        foreach (var (tag, dias) in new[] { ("vip", 4), ("vegano", 3), ("sem-gluten", 2), ("encomenda", 1) })
            c.Ana.AdicionarTag(tag, OrigemClienteTag.Dona, Agora.AddDays(-dias));
        // Eva está em risco, mas já foi convidada há 3 dias: sai do risco. Duda segue em risco.
        c.Eva.AdicionarTag("recompra-convidada", OrigemClienteTag.Dona, Agora.AddDays(-3));
        return c;
    }

    private async Task Gravar(Carteira c)
    {
        await using var gravar = fixture.CreateDbContext();
        using var bypass = gravar.UseRowLevelSecurityBypass();
        await gravar.Database.MigrateAsync();
        gravar.Empresas.AddRange(c.A, c.B);
        gravar.Clientes.AddRange(c.Ana, c.Bia, c.Cida, c.Duda, c.Eva, c.Fabi, c.Zeca);
        gravar.Pedidos.AddRange(c.Pedidos);
        foreach (var o in c.Outros) gravar.Add(o);
        await gravar.SaveChangesAsync();
    }

    private ClienteRepository Repo(Guid empresaId, out EasyStockDbContext db)
    {
        // SuperAdmin desliga o filtro global do EF e o superusuário do teste ignora a RLS: sobra só o EmpresaId
        // que a própria consulta escreve, que é o que este teste quer provar.
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.IsAuthenticated.Returns(true);
        usuario.Nivel.Returns(NivelAcesso.SuperAdmin);
        db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>().UseNpgsql(fixture.ConnectionString).Options, usuario);
        return new ClienteRepository(db);
    }

    [SkippableFact]
    public async Task Linha_traz_gasto_compras_datas_ocorrencias_notas_e_tags_pela_regra_de_compra()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var c = Montar();
        await Gravar(c);

        var lista = await Repo(c.A.Id, out var db).ListarComRelacaoAsync(c.A.Id, 1, 200, search: "Relacao 3244");
        await db.DisposeAsync();

        lista.Total.Should().Be(6);
        var ana = lista.Itens.Single(i => i.Cliente.Id == c.Ana.Id);
        (ana.Compras, ana.Gasto).Should().Be((3, 200m), "cancelado, rascunho e aguardando pagamento não são compra");
        ana.PrimeiraCompraEm!.Value.Should().BeCloseTo(Agora.AddDays(-100), TimeSpan.FromSeconds(5));
        ana.UltimaCompraEm!.Value.Should().BeCloseTo(Agora.AddDays(-10), TimeSpan.FromSeconds(5));
        (ana.OcorrenciasAbertas, ana.OcorrenciasTotal, ana.NotasTotal).Should().Be((1, 2, 2));
        ana.UltimaNota.Should().Be(new string('x', ClienteRelacao.TamanhoDaNotaCurta), "a última nota vem curta");
        ana.Tags.Should().HaveCount(ClienteRelacao.MaximoDeTags).And.Equal("encomenda", "sem_gluten", "vegano");
        var cida = lista.Itens.Single(i => i.Cliente.Id == c.Cida.Id);
        (cida.Compras, cida.Gasto, cida.PrimeiraCompraEm, cida.UltimaCompraEm, cida.UltimaNota, cida.Tags.Count)
            .Should().Be((0, 0m, null, null, null, 0));
        lista.Itens.Single(i => i.Cliente.Id == c.Duda.Id).Compras.Should().Be(5);
    }

    [SkippableFact]
    public async Task EmRisco_e_verdadeiro_se_e_so_se_o_cliente_aparece_em_relacionamento_recompra()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var c = Montar();
        await Gravar(c);

        var repo = Repo(c.A.Id, out var db);
        var todos = await repo.ListarComRelacaoAsync(c.A.Id, 1, 200, search: "Relacao 3244");
        var recompra = await repo.ListarComRelacaoAsync(c.A.Id, 1, 200, search: "Relacao 3244", relacionamento: RegraDeRecompra.Relacionamento);
        await db.DisposeAsync();

        todos.Itens.Where(i => i.EmRisco).Select(i => i.Cliente.Id).Should().BeEquivalentTo(recompra.Itens.Select(i => i.Cliente.Id));
        recompra.Itens.Select(i => i.Cliente.Id).Should().Equal([c.Duda.Id], "Eva foi convidada e Fabi comprou há 5 dias");
        recompra.Itens.Should().OnlyContain(i => i.EmRisco);
    }

    [SkippableFact]
    public async Task Carteira_e_a_soma_de_todos_os_clientes_da_empresa_em_qualquer_pagina_filtro_e_ordem()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var c = Montar();
        await Gravar(c);

        var repo = Repo(c.A.Id, out var db);
        var cheia = await repo.ListarComRelacaoAsync(c.A.Id, 1, 200, search: "Relacao 3244");
        cheia.GastoTotalDaCarteira.Should().Be(cheia.Itens.Sum(i => i.Gasto)).And.Be(300m, "sem a empresa B e sem o que não é compra");
        foreach (var lista in new[]
        {
            await repo.ListarComRelacaoAsync(c.A.Id, 2, 2, search: "Relacao 3244"),
            await repo.ListarComRelacaoAsync(c.A.Id, 1, 200, search: "Ana"),
            await repo.ListarComRelacaoAsync(c.A.Id, 1, 200, ativo: false),
            await repo.ListarComRelacaoAsync(c.A.Id, 1, 200, relacionamento: RegraDeRecompra.Relacionamento),
        })
            lista.GastoTotalDaCarteira.Should().Be(300m);

        var porGasto = await repo.ListarComRelacaoAsync(c.A.Id, 1, 2, search: "Relacao 3244", sort: "gasto", order: "desc");
        porGasto.Itens.Select(i => i.Cliente.Id).Should().Equal(c.Ana.Id, c.Duda.Id);
        var porTicket = await repo.ListarComRelacaoAsync(c.A.Id, 1, 1, search: "Relacao 3244", sort: "ticketmedio", order: "desc");
        porTicket.Itens.Single().Cliente.Id.Should().Be(c.Ana.Id, "200 / 3 compras é o maior ticket médio");
        var menorTicket = await repo.ListarComRelacaoAsync(c.A.Id, 1, 1, search: "Relacao 3244", sort: "ticketmedio", order: "asc");
        menorTicket.Itens.Single().Cliente.Id.Should().Be(c.Cida.Id, "sem compra, ticket zero");
        await db.DisposeAsync();
    }

    [SkippableFact]
    public async Task Toda_subconsulta_filtra_a_empresa_mesmo_com_linha_da_outra_empresa_apontando_para_o_cliente()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var c = Montar();
        // Linhas da empresa B que apontam para a Ana, da empresa A: nenhuma pode entrar em nenhuma métrica.
        var invasor = Compra(c, c.B, c.Zeca, 5000m, 1);
        invasor.ClienteId = c.Ana.Id;
        c.Outros.Add(Ocorrencia.Abrir(c.B.Id, invasor.Id, c.Ana.Id, null, OrigemOcorrencia.Dona, CategoriaOcorrencia.Atraso, "Alheia", Agora));
        c.Outros.Add(ClienteNota.Criar(c.B.Id, c.Ana.Id, "Nota alheia", "Equipe", Agora));
        await Gravar(c);
        await using (var sql = fixture.CreateDbContext())
        {
            using var bypass = sql.UseRowLevelSecurityBypass();
            await sql.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO cliente_tags (\"Id\",\"EmpresaId\",\"ClienteId\",\"Tag\",\"Origem\",\"CriadoEm\") VALUES ({Guid.NewGuid()},{c.B.Id},{c.Ana.Id},'alheia',{OrigemClienteTag.Dona},{Agora})");
            // #3244: convite da empresa B apontando para a Duda (em risco na A) não pode tirá-la do risco.
            await sql.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO cliente_tags (\"Id\",\"EmpresaId\",\"ClienteId\",\"Tag\",\"Origem\",\"CriadoEm\") VALUES ({Guid.NewGuid()},{c.B.Id},{c.Duda.Id},{RegraDeRecompra.TagConvidada},{OrigemClienteTag.Dona},{Agora})");
        }

        var repo = Repo(c.A.Id, out var db);
        var lista = await repo.ListarComRelacaoAsync(c.A.Id, 1, 200, search: "Relacao 3244");
        var recompra = await repo.ListarComRelacaoAsync(c.A.Id, 1, 200, relacionamento: RegraDeRecompra.Relacionamento);
        await db.DisposeAsync();

        lista.Itens.Single(i => i.Cliente.Id == c.Duda.Id).EmRisco.Should().BeTrue("a tag de convite da empresa B não vale na A");
        recompra.Itens.Select(i => i.Cliente.Id).Should().Contain(c.Duda.Id);

        var ana = lista.Itens.Single(i => i.Cliente.Id == c.Ana.Id);
        (ana.Compras, ana.Gasto, ana.OcorrenciasAbertas, ana.OcorrenciasTotal, ana.NotasTotal).Should().Be((3, 200m, 1, 2, 2));
        ana.Tags.Should().NotContain("alheia");
        lista.GastoTotalDaCarteira.Should().Be(300m);
        lista.Itens.Select(i => i.Cliente.Id).Should().NotContain(c.Zeca.Id);

        var repoB = Repo(c.B.Id, out var dbB);
        var listaB = await repoB.ListarComRelacaoAsync(c.B.Id, 1, 200, search: "Relacao 3244");
        await dbB.DisposeAsync();
        listaB.Itens.Should().ContainSingle().Which.Gasto.Should().Be(1000m);
    }

    [SkippableFact]
    public async Task Pagina_de_200_linhas_usa_o_mesmo_numero_de_consultas_que_a_de_5_e_o_indice_do_cliente_existe()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var c = Montar();
        var extras = Enumerable.Range(0, 200).Select(i => Cliente.Criar(c.A.Id, $"Relacao 3244 Massa {i:000}")).ToList();
        foreach (var e in extras) Compra(c, c.A, e, 10m, 20);
        c.Outros.AddRange(extras);
        await Gravar(c);

        async Task<int> Comandos(int tamanho)
        {
            var contador = new ContadorDeComandos();
            await using var db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseNpgsql(fixture.ConnectionString).AddInterceptors(contador).Options);
            db.SetMobileTenantContext(c.A.Id);
            var lista = await new ClienteRepository(db).ListarComRelacaoAsync(c.A.Id, 1, tamanho, search: "Relacao 3244 Massa");
            lista.Itens.Should().HaveCount(tamanho).And.OnlyContain(i => i.Compras == 1);
            return contador.Comandos;
        }
        var pequena = await Comandos(5);
        var grande = await Comandos(200);
        grande.Should().Be(pequena).And.BeLessThan(12, "a página inteira sai em poucas consultas em lote");

        await using var plano = fixture.CreateDbContext();
        var indice = await plano.Database.SqlQueryRaw<string>(
            "SELECT indexname AS \"Value\" FROM pg_indexes WHERE tablename = 'pedidos' AND indexname = 'IX_pedidos_ClienteId'").ToListAsync();
        indice.Should().ContainSingle("a agregação por cliente usa o índice de pedidos.ClienteId");
    }

    private sealed class ContadorDeComandos : DbCommandInterceptor
    {
        public int Comandos { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Comandos++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Comandos++;
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
