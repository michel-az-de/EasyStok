using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Atendimento;

/// <summary>
/// #1440 (homologação de 07/10): "Pôr na viagem" numa viagem já gravada. A parada nasce com o Id gerado
/// no domínio; sem marcar a parada como nova, o EF a tomava por existente, mandava UPDATE de 0 linha e a
/// tela recebia "Conflito de concorrência".
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class ViagemIncluirParadaPostgresTests(PostgreSqlDatabaseFixture fixture)
{
    private static readonly DateTime Agora = new(2026, 10, 7, 15, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task PorNaViagemJaGravada_GravaAParada()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = Guid.NewGuid();
        var pedido = await SemearPedidoAsync(empresaId);
        var viagem = Viagem.Criar(empresaId, Agora);
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            db.Viagens.Add(viagem);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            var useCase = new IncluirParadaViagemUseCase(
                new ViagemRepository(db), new PedidoRepository(db), new ClienteRepository(db), db);

            await useCase.ExecuteAsync(empresaId, viagem.Id, pedido.Id);
        }

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresaId);
        (await leitura.ParadasViagem.Where(p => p.ViagemId == viagem.Id).Select(p => p.PedidoId).ToListAsync())
            .Should().Equal(pedido.Id);
    }

    private async Task<Pedido> SemearPedidoAsync(Guid empresaId)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(new Empresa
        {
            Id = empresaId,
            Nome = "Empresa Parada",
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
