using EasyStock.Domain.Entities;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;
using Npgsql;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #1265-9: o <c>FOR UPDATE</c> de aprovar e recusar pedido do site levava só o id. Com a empresa no WHERE, o pedido de
/// outra empresa não é lido nem travado: outra conexão consegue a trava na hora (NOWAIT).
/// </summary>
public class PedidoStorefrontTravaPorEmpresaIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task ForUpdateDeOutraEmpresaNaoLeNemTravaOPedido()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var dona = Empresa.Criar("Trava por empresa", null);
        var outra = Empresa.Criar("Outra empresa trava", null);
        var pedido = new Pedido
        {
            Id = Guid.NewGuid(), EmpresaId = dona.Id, Status = StatusPedidoMapper.AguardandoAprovacaoBaba,
            ClienteNome = "Fulana", Total = Dinheiro.Zero, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow,
        };
        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            db.Empresas.AddRange(dona, outra);
            db.Pedidos.Add(pedido);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            await using var tx = await db.Database.BeginTransactionAsync();
            (await new PedidoStorefrontRepository(db).GetForUpdateAsync(outra.Id, pedido.Id)).Should().BeNull();
            (await TravaNaHoraAsync(pedido.Id)).Should().BeTrue("a linha da outra empresa não foi travada");

            (await new PedidoStorefrontRepository(db).GetForUpdateAsync(dona.Id, pedido.Id))!.Id.Should().Be(pedido.Id);
            (await TravaNaHoraAsync(pedido.Id)).Should().BeFalse("a empresa dona trava a linha até o commit");
            await tx.RollbackAsync();
        }
    }

    private async Task<bool> TravaNaHoraAsync(Guid pedidoId)
    {
        await using var conexao = new NpgsqlConnection(fixture.ConnectionString);
        await conexao.OpenAsync();
        await using var tx = await conexao.BeginTransactionAsync();
        await using var comando = new NpgsqlCommand("SELECT 1 FROM pedidos WHERE \"Id\" = @id FOR UPDATE NOWAIT", conexao, tx);
        comando.Parameters.AddWithValue("id", pedidoId);
        try
        {
            await comando.ExecuteScalarAsync();
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            return false;
        }
    }
}
