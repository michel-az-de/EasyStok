using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

[Collection("PostgreSqlTestCollection")]
public sealed class PaginacaoEstavelTests(PostgreSqlDatabaseFixture fixture)
{
    private static readonly DateTime Instante = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private EasyStockDbContext CriarContexto(Guid empresaId)
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.ConnectionString, pg => pg.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .Options;
        var db = new EasyStockDbContext(options);
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(new Empresa
        {
            Id = empresaId, Nome = "Paginação", Documento = empresaId.ToString("N")[..14],
            CriadoEm = Instante, AlteradoEm = Instante
        });
        return db;
    }

    private static Guid[] NovosIds() => Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).Order().ToArray();

    private static IEnumerable<Guid> OrdenacaoEsperada(Guid[] ids, bool desc) => desc
        ? ids.Reverse()
        : ids.Take(3).Reverse().Concat(ids.Skip(3).Reverse());

    [SkippableTheory]
    [InlineData("nome", "asc")]
    [InlineData("nome", "desc")]
    [InlineData("criadoem", "asc")]
    [InlineData("criadoem", "desc")]
    [InlineData("lastorderat", "asc")]
    [InlineData("lastorderat", "desc")]
    [InlineData("ordercount", "asc")]
    [InlineData("ordercount", "desc")]
    public async Task Clientes_desempatam_por_id_sem_perder_a_ordenacao_principal(string sort, string order)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresaId = Guid.NewGuid();
        await using var db = CriarContexto(empresaId);
        var ids = NovosIds();
        for (var i = 0; i < ids.Length; i++)
        {
            var grupo = i / 3;
            var cliente = Cliente.Criar(empresaId, $"Cliente {grupo}");
            cliente.Id = ids[i];
            cliente.CriadoEm = Instante.AddHours(grupo);
            cliente.LastOrderAt = grupo == 0 ? null : Instante;
            cliente.OrderCount = grupo;
            db.Clientes.Add(cliente);
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var repo = new ClienteRepository(db);
        var encontrados = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var (items, total) = await repo.GetByEmpresaAsync(empresaId, page, 2, sort: sort, order: order);
            total.Should().Be(6);
            encontrados.AddRange(items.Select(c => c.Id));
        }

        // PostgreSQL coloca NULL por último em ASC e por primeiro em DESC.
        var desc = sort == "lastorderat" ? order == "asc" : order == "desc";
        encontrados.Should().Equal(OrdenacaoEsperada(ids, desc));
        db.ChangeTracker.Entries().Should().BeEmpty();
        (await repo.GetByEmpresaAsync(Guid.NewGuid(), 1, 2)).items.Should().BeEmpty();
    }

    [SkippableTheory]
    [InlineData("criadoem", "asc")]
    [InlineData("criadoem", "desc")]
    [InlineData("total", "asc")]
    [InlineData("total", "desc")]
    [InlineData("status", "asc")]
    [InlineData("status", "desc")]
    [InlineData("cliente", "asc")]
    [InlineData("cliente", "desc")]
    [InlineData("urgencia", "asc")]
    [InlineData("urgencia", "desc")]
    public async Task Pedidos_paginados_em_split_query_preservam_itens_e_pagamentos(string sort, string order)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresaId = Guid.NewGuid();
        await using var db = CriarContexto(empresaId);
        var ids = NovosIds();
        for (var i = 0; i < ids.Length; i++)
        {
            var grupo = i / 3;
            var pedido = Pedido.Criar(empresaId);
            pedido.Id = ids[i];
            pedido.CriadoEm = Instante.AddHours(grupo);
            pedido.ClienteNome = $"Cliente {grupo}";
            pedido.Status = grupo == 0 ? "aguardando" : "preparando";
            pedido.Itens.Add(new PedidoItem
            {
                Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = $"Item {i}",
                Quantidade = 1, PrecoUnitario = 10m + grupo, Subtotal = 10m + grupo, CriadoEm = Instante
            });
            pedido.Pagamentos.Add(new PedidoPagamento
            {
                Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "pix", Valor = i + 1m, PagoEm = Instante
            });
            pedido.RecalcularTotal();
            db.Pedidos.Add(pedido);
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var repo = new PedidoRepository(db);
        var encontrados = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var (items, total) = await repo.GetByEmpresaAsync(empresaId, page, 2, sort: sort, order: order);
            total.Should().Be(6);
            foreach (var pedido in items)
            {
                var i = Array.IndexOf(ids, pedido.Id);
                pedido.Itens.Should().ContainSingle().Which.Nome.Should().Be($"Item {i}");
                pedido.TotalPago.Should().Be(i + 1m);
                encontrados.Add(pedido.Id);
            }
        }

        encontrados.Should().Equal(OrdenacaoEsperada(ids, sort == "urgencia" || order == "desc"));
        db.ChangeTracker.Entries().Should().BeEmpty();
        (await repo.GetByEmpresaAsync(Guid.NewGuid(), 1, 2)).items.Should().BeEmpty();
    }

    [SkippableTheory]
    [InlineData("datamovimento", "asc")]
    [InlineData("datamovimento", "desc")]
    [InlineData("valor", "asc")]
    [InlineData("valor", "desc")]
    [InlineData("tipo", "asc")]
    [InlineData("tipo", "desc")]
    public async Task Movimentos_do_caixa_nao_repetem_nem_omitem_linhas_entre_paginas(string sort, string order)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresaId = Guid.NewGuid();
        await using var db = CriarContexto(empresaId);
        var ids = NovosIds();
        for (var i = 0; i < ids.Length; i++)
        {
            var grupo = i / 3;
            var movimento = MovimentoCaixa.Criar(empresaId, grupo == 0 ? "entrada" : "saida",
                10m + grupo, Instante.AddHours(grupo));
            movimento.Id = ids[i];
            db.MovimentosCaixa.Add(movimento);
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var repo = new CaixaRepository(db);
        var encontrados = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var (items, total) = await repo.ListMovimentosAsync(empresaId, page, 2, sort: sort, order: order);
            total.Should().Be(6);
            encontrados.AddRange(items.Select(m => m.Id));
        }

        encontrados.Should().Equal(OrdenacaoEsperada(ids, order == "desc"));
        db.ChangeTracker.Entries().Should().BeEmpty();
        (await repo.ListMovimentosAsync(Guid.NewGuid(), 1, 2)).items.Should().BeEmpty();
    }
}
