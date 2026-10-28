using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Repositories.Pagamentos;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// I3.2 (#1688) no Postgres real: a varredura do job deixa de fora a cobrança que aguarda credencial e a
/// recebe de volta quando a conta dela volta; a migration marca as cobranças online antigas como
/// <c>global</c>. Banco próprio (<see cref="IClassFixture{TFixture}"/>): o teste da migration faz Down e Up.
/// </summary>
public class CobrancaContaRecebedoraPostgresTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string Migracao = "AddCobrancaContaRecebedora";
    private static readonly DateTime Agora = new(2026, 10, 12, 15, 0, 0, DateTimeKind.Utc);

    private async Task<(Guid EmpresaId, Guid PedidoId)> SemearPedidoAsync()
    {
        var empresaId = Guid.NewGuid();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Cobranca", Documento = empresaId.ToString("N")[..14], CriadoEm = Agora, AlteradoEm = Agora });
        var pedido = Pedido.Criar(empresaId);
        pedido.Status = StatusPedidoMapper.AguardandoPagamento;
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return (empresaId, pedido.Id);
    }

    private async Task<CobrancaPedido> SemearOnlineAsync(Guid empresaId, Guid pedidoId, string? conta, bool aguardando)
    {
        var cobranca = CobrancaPedido.CriarOnline(empresaId, pedidoId, 50m, $"pref-{Guid.NewGuid():N}", "https://mp.test/pref",
            Agora.AddMinutes(1), 1, Agora, contaRecebedoraId: conta);
        if (aguardando) cobranca.AguardarCredencial(Agora.AddMinutes(2));
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        db.CobrancasPedido.Add(cobranca);
        await db.SaveChangesAsync();
        return cobranca;
    }

    [SkippableFact]
    public async Task Varredura_ignora_quem_aguarda_credencial_e_recebe_de_volta_quando_a_conta_volta()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var (lojaA, pedidoA) = await SemearPedidoAsync();
        var (lojaB, pedidoB) = await SemearPedidoAsync();
        var normal = await SemearOnlineAsync(lojaA, pedidoA, "111", aguardando: false);
        var presaDa111 = await SemearOnlineAsync(lojaA, pedidoA, "111", aguardando: true);
        var presaDa222 = await SemearOnlineAsync(lojaB, pedidoB, "222", aguardando: true);
        var presaSemConta = await SemearOnlineAsync(lojaB, pedidoB, null, aguardando: true);
        var limite = Agora.AddMinutes(10);

        await using (var db = fixture.CreateDbContext())
        {
            var repo = new CobrancaPedidoRepository(db);
            var vencidas = await repo.ListarPendentesVencidasAsync(limite, 500);
            vencidas.Select(v => v.CobrancaId).Should().Contain(normal.Id)
                .And.NotContain([presaDa111.Id, presaDa222.Id, presaSemConta.Id]);
            vencidas.Single(v => v.CobrancaId == normal.Id).ContaRecebedoraId.Should().Be("111");
            (await repo.ExisteAguardandoCredencialAsync()).Should().BeTrue();

            (await repo.RetomarAguardandoCredencialAsync("111")).Should().Be(2, "volta a da conta 111 e a que não tem conta gravada");
        }

        await using (var db = fixture.CreateDbContext())
        {
            var depois = (await new CobrancaPedidoRepository(db).ListarPendentesVencidasAsync(limite, 500)).Select(v => v.CobrancaId).ToList();
            depois.Should().Contain([normal.Id, presaDa111.Id, presaSemConta.Id]).And.NotContain(presaDa222.Id);
        }
    }

    [SkippableFact]
    public async Task Migration_marca_as_cobrancas_online_antigas_como_global_e_desce_limpa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var (empresaId, pedidoId) = await SemearPedidoAsync();
        var online = await SemearOnlineAsync(empresaId, pedidoId, null, aguardando: false);
        var naEntrega = CobrancaPedido.CriarNaEntrega(empresaId, pedidoId, 50m, Agora);
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            db.CobrancasPedido.Add(naEntrega);
            await db.SaveChangesAsync();
        }

        await using var admin = fixture.CreateDbContext();
        var migracoes = admin.Database.GetMigrations().ToList();
        var alvo = migracoes.Single(m => m.EndsWith("_" + Migracao, StringComparison.Ordinal));
        var anterior = migracoes[migracoes.IndexOf(alvo) - 1];
        var migrator = admin.GetService<IMigrator>();

        await migrator.MigrateAsync(anterior);
        (await ColunasAsync(admin)).Should().BeEmpty("o Down tira as três colunas");

        await migrator.MigrateAsync(alvo);
        (await ColunasAsync(admin)).Should().BeEquivalentTo(["AguardandoCredencialDesde", "ContaRecebedoraId", "OrigemCredencial"]);
        // O banco é da classe inteira: volta ao topo para os outros testes verem as migrações posteriores (#2096).
        await migrator.MigrateAsync();

        await using var leitura = fixture.CreateDbContext();
        using var _ = leitura.UseRowLevelSecurityBypass();
        var origens = await leitura.CobrancasPedido.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.PedidoId == pedidoId).ToDictionaryAsync(c => c.Id, c => c.OrigemCredencial);
        origens[online.Id].Should().Be(CobrancaPedido.OrigemCredencialGlobal, "cobrança online antiga nasceu da credencial global");
        origens[naEntrega.Id].Should().BeNull("na entrega não há credencial");
    }

    private static async Task<List<string>> ColunasAsync(DbContext db) =>
        await db.Database.SqlQueryRaw<string>(
            """
            SELECT column_name AS "Value" FROM information_schema.columns
            WHERE table_schema = current_schema() AND table_name = 'cobrancas_pedido'
              AND column_name IN ('AguardandoCredencialDesde', 'ContaRecebedoraId', 'OrigemCredencial')
            """).ToListAsync();
}