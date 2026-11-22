using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// E4 L2 (#3076): o dia da entrega é <c>AgendadoParaEm ?? CriadoEm</c>; o filtro e a ordem da lista de Pedidos usam
/// esse instante, e o período é o dia civil de Brasília (03:00Z a 02:59:59Z do dia seguinte).
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class PedidoEntregaFiltroOrdemIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    // "Hoje" é 10/06/2026 em Brasília: de 10/06 03:00Z a 11/06 02:59:59Z.
    private static readonly DateTime Hoje = new(2026, 6, 10, 0, 0, 0, DateTimeKind.Unspecified);

    private static Pedido Novo(Guid empresa, string nome, DateTime criadoEm, DateTime? agendadoPara)
    {
        var p = Pedido.Criar(empresa);
        p.Status = StatusPedidoMapper.Aguardando;
        p.ClienteNome = nome;
        p.CriadoEm = criadoEm;
        p.AlteradoEm = criadoEm;
        p.AgendadoParaEm = agendadoPara;
        return p;
    }

    private static DateTime Utc(int mes, int dia, int hora, int min = 0) => new(2026, mes, dia, hora, min, 0, DateTimeKind.Utc);

    private async Task<(PedidoRepository Repo, Guid Empresa, EasyStock.Infra.Postgre.Data.EasyStockDbContext Db)> PrepararAsync(params Func<Guid, Pedido>[] pedidos)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        var empresa = Guid.NewGuid();
        var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        db.Empresas.Add(new Empresa
        {
            Id = empresa, Nome = "Entrega do pedido", Documento = empresa.ToString("N")[..14],
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
        });
        db.Pedidos.AddRange(pedidos.Select(f => f(empresa)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (new PedidoRepository(db), empresa, db);
    }

    [SkippableFact]
    public async Task Entrega_hoje_inclui_o_para_agora_criado_hoje_e_o_agendado_hoje_e_exclui_os_de_amanha()
    {
        var (repo, empresa, db) = await PrepararAsync(
            e => Novo(e, "para agora hoje", Utc(6, 10, 15), null),
            e => Novo(e, "agendado hoje", Utc(6, 1, 12), Utc(6, 10, 18)),
            e => Novo(e, "21h de Brasilia de hoje", Utc(6, 1, 12), Utc(6, 11, 0)),
            e => Novo(e, "agendado amanha", Utc(6, 10, 12), Utc(6, 11, 15)),
            e => Novo(e, "criado hoje para amanha", Utc(6, 10, 12), Utc(6, 12, 2)),
            e => Novo(e, "para agora amanha", Utc(6, 11, 15), null),
            e => Novo(e, "entrega as 02h59 de hoje em UTC, ainda ontem", Utc(6, 9, 12), Utc(6, 10, 2, 59)));
        await using var _ = db;

        var (itens, total) = await repo.GetByEmpresaAsync(empresa, 1, 50,
            entregaDesde: DataUtc.InicioDoPeriodoUtc(Hoje), entregaAte: DataUtc.FimDoPeriodoUtc(Hoje));

        total.Should().Be(3);
        itens.Select(p => p.ClienteNome).Should().BeEquivalentTo("para agora hoje", "agendado hoje", "21h de Brasilia de hoje");
    }

    [SkippableFact]
    public async Task Sort_entrega_ordena_pelo_dia_da_entrega_e_nao_pela_criacao()
    {
        var (repo, empresa, db) = await PrepararAsync(
            e => Novo(e, "P1", Utc(6, 1, 12), Utc(6, 12, 15)),
            e => Novo(e, "P2", Utc(6, 9, 12), null),
            e => Novo(e, "P3", Utc(6, 5, 12), Utc(6, 11, 15)));
        await using var _ = db;

        var asc = await repo.GetByEmpresaAsync(empresa, 1, 50, sort: "entrega", order: "asc");
        var desc = await repo.GetByEmpresaAsync(empresa, 1, 50, sort: "entrega", order: "desc");

        asc.items.Select(p => p.ClienteNome).Should().Equal("P2", "P3", "P1");
        desc.items.Select(p => p.ClienteNome).Should().Equal("P1", "P3", "P2");
    }
}
