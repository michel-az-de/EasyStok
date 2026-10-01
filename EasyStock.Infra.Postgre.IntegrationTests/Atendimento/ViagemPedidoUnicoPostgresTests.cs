using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Sales;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EasyStock.Infra.Postgre.IntegrationTests.Atendimento;

/// <summary>
/// F08 item 4 (#1238): o mesmo pedido não fica em duas viagens ativas. A checagem do use case
/// (<c>PedidoEmViagemAtivaAsync</c>) corre antes do commit; o índice único parcial é quem garante
/// quando duas operadoras montam viagens ao mesmo tempo.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class ViagemPedidoUnicoPostgresTests(PostgreSqlDatabaseFixture fixture)
{
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task MesmoPedidoEmDuasViagensMontando_Recusa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = Guid.NewGuid();
        var pedido = await SemearPedidoAsync(empresaId);

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            var primeira = Viagem.Criar(empresaId, Agora);
            primeira.IncluirParada(pedido.Id, clienteBloqueado: false);
            db.Viagens.Add(primeira);
            await db.SaveChangesAsync();
        }

        await using var outra = fixture.CreateDbContext();
        outra.SetMobileTenantContext(empresaId);
        var segunda = Viagem.Criar(empresaId, Agora);
        segunda.IncluirParada(pedido.Id, clienteBloqueado: false);
        outra.Viagens.Add(segunda);

        var act = () => outra.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.ConstraintName.Should().Be("ux_viagem_paradas_pedido_ativo");
    }

    [SkippableFact]
    public async Task ViagemDesfeitaLiberaOPedidoParaOutra()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = Guid.NewGuid();
        var pedido = await SemearPedidoAsync(empresaId);

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        var primeira = Viagem.Criar(empresaId, Agora);
        primeira.IncluirParada(pedido.Id, clienteBloqueado: false);
        db.Viagens.Add(primeira);
        await db.SaveChangesAsync();

        primeira.Desfazer();
        var segunda = Viagem.Criar(empresaId, Agora);
        segunda.IncluirParada(pedido.Id, clienteBloqueado: false);
        db.Viagens.Add(segunda);
        await db.SaveChangesAsync();

        (await db.ParadasViagem.CountAsync(p => p.PedidoId == pedido.Id)).Should().Be(1);
    }

    private async Task<Pedido> SemearPedidoAsync(Guid empresaId)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(new Empresa
        {
            Id = empresaId,
            Nome = "Empresa Viagens",
            Documento = empresaId.ToString("N")[..14],
            CriadoEm = Agora,
            AlteradoEm = Agora,
        });
        var pedido = Pedido.Criar(empresaId);
        pedido.Status = StatusPedidoMapper.Pronto;
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();
        return pedido;
    }
}
