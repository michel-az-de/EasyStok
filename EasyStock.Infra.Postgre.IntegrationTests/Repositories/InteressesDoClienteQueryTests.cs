using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Repositories.Campanhas;
using FluentAssertions;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Interesses do cliente (#1228) em Postgres real: busca por id e listagem só da empresa, e os abertos
/// do cliente nos itens de um pedido, rastreados para o fechamento gravar.
/// </summary>
public class InteressesDoClienteQueryTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task PorClienteEPorItensDoPedido()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var agora = DateTime.UtcNow;
        var empresa = Empresa.Criar("Interesse Cliente A", "33333333000191");
        var outra = Empresa.Criar("Interesse Cliente B", "44444444000191");
        var loja = StorefrontEntity.Criar(empresa.Id, "interesse-cli-" + Guid.NewGuid().ToString("N")[..6], "Loja", 0m);
        var bolo = CardapioItem.CriarAvulso(loja.Id, "Bolo de Cenoura", 35m);
        var torta = CardapioItem.CriarAvulso(loja.Id, "Torta", 40m);
        var pao = CardapioItem.CriarAvulso(loja.Id, "Pão", 10m);
        var ana = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Ana" };
        var bia = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Bia" };
        var intrusa = new Cliente { Id = Guid.NewGuid(), EmpresaId = outra.Id, Nome = "Outra empresa" };

        var anaBolo = InteresseItem.Registrar(empresa.Id, ana.Id, bolo.Id, null, OrigemInteresse.Agente, agora.AddHours(-3));
        var anaTorta = InteresseItem.Registrar(empresa.Id, ana.Id, torta.Id, null, OrigemInteresse.Dona, agora.AddHours(-1));
        var anaPaoAtendido = InteresseItem.Registrar(empresa.Id, ana.Id, pao.Id, null, OrigemInteresse.Agente, agora.AddHours(-2));
        anaPaoAtendido.MarcarAtendido(agora.AddMinutes(-30));
        var anaSemItem = InteresseItem.Registrar(empresa.Id, ana.Id, null, "Bolo de fubá", OrigemInteresse.Agente, agora.AddHours(-4));
        var biaBolo = InteresseItem.Registrar(empresa.Id, bia.Id, bolo.Id, null, OrigemInteresse.Agente, agora);
        var deOutraEmpresa = InteresseItem.Registrar(outra.Id, intrusa.Id, bolo.Id, null, OrigemInteresse.Agente, agora);

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            db.Empresas.AddRange(empresa, outra);
            db.Set<StorefrontEntity>().Add(loja);
            db.Set<CardapioItem>().AddRange(bolo, torta, pao);
            db.Clientes.AddRange(ana, bia, intrusa);
            db.InteressesItem.AddRange(anaBolo, anaTorta, anaPaoAtendido, anaSemItem, biaBolo, deOutraEmpresa);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa.Id);
            var repo = new InteresseItemRepository(db);

            (await repo.ListarDoClienteAsync(empresa.Id, ana.Id)).Select(i => i.Id)
                .Should().Equal(anaTorta.Id, anaPaoAtendido.Id, anaBolo.Id, anaSemItem.Id); // mais recente primeiro
            (await repo.ListarDoClienteAsync(empresa.Id, intrusa.Id)).Should().BeEmpty();

            (await repo.GetByIdAsync(empresa.Id, anaBolo.Id)).Should().NotBeNull();
            (await repo.GetByIdAsync(empresa.Id, deOutraEmpresa.Id)).Should().BeNull();

            var abertos = await repo.ListarAbertosDoClienteNosItensAsync(empresa.Id, ana.Id, [bolo.Id, pao.Id]);
            abertos.Select(i => i.Id).Should().Equal(anaBolo.Id); // pão já atendido; torta fora do pedido; Bia é outra cliente

            abertos[0].MarcarAtendido(agora);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa.Id);
            (await new InteresseItemRepository(db).GetByIdAsync(empresa.Id, anaBolo.Id))!.AtendidoEm.Should().NotBeNull();
        }
    }
}
