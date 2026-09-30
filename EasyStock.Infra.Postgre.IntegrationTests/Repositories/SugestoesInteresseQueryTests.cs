using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Repositories.Campanhas;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Sugestão de quem avisar quando o item volta (S31) em Postgres real: só interesses abertos
/// (<c>AtendidoEm</c> nulo), só daquele item e só da empresa; RLS na tabela nova.
/// </summary>
public class SugestoesInteresseQueryTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task SoAbertos()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var agora = DateTime.UtcNow;
        var empresa = Empresa.Criar("Interesse A", "11111111000191");
        var outra = Empresa.Criar("Interesse B", "22222222000191");
        var loja = StorefrontEntity.Criar(empresa.Id, "interesse-" + Guid.NewGuid().ToString("N")[..6], "Loja", 0m);
        var bolo = CardapioItem.CriarAvulso(loja.Id, "Bolo de Cenoura", 35m);
        var torta = CardapioItem.CriarAvulso(loja.Id, "Torta", 40m);
        var ana = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Ana", Telefone = "11988887777" };
        var bia = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Bia" };
        var caio = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Caio" };
        var intrusa = new Cliente { Id = Guid.NewGuid(), EmpresaId = outra.Id, Nome = "Outra empresa" };

        var anaAberto = InteresseItem.Registrar(empresa.Id, ana.Id, bolo.Id, "Bolo de Cenoura", OrigemInteresse.Agente, agora.AddHours(-2));
        var biaAberto = InteresseItem.Registrar(empresa.Id, bia.Id, bolo.Id, null, OrigemInteresse.Dona, agora.AddHours(-1));
        var caioAtendido = InteresseItem.Registrar(empresa.Id, caio.Id, bolo.Id, null, OrigemInteresse.Agente, agora.AddDays(-5));
        caioAtendido.MarcarAtendido(agora.AddDays(-4));
        var anaOutroItem = InteresseItem.Registrar(empresa.Id, ana.Id, torta.Id, null, OrigemInteresse.Agente, agora);
        var deOutraEmpresa = InteresseItem.Registrar(outra.Id, intrusa.Id, bolo.Id, null, OrigemInteresse.Agente, agora);

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            db.Empresas.AddRange(empresa, outra);
            db.Set<StorefrontEntity>().Add(loja);
            db.Set<CardapioItem>().AddRange(bolo, torta);
            db.Clientes.AddRange(ana, bia, caio, intrusa);
            db.InteressesItem.AddRange(anaAberto, biaAberto, caioAtendido, anaOutroItem, deOutraEmpresa);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa.Id);
            var repo = new InteresseItemRepository(db);

            var sugestoes = await repo.ListarAbertosDoItemAsync(empresa.Id, bolo.Id);
            sugestoes.Select(s => s.InteresseId).Should().Equal(biaAberto.Id, anaAberto.Id); // mais recente primeiro
            sugestoes.Single(s => s.ClienteId == ana.Id).Should().Match<InteresseAbertoCliente>(s =>
                s.ClienteNome == "Ana" && s.ClienteTelefone == "11988887777" && s.Origem == OrigemInteresse.Agente);

            (await repo.ContarAbertosDoItemAsync(empresa.Id, bolo.Id)).Should().Be(2);
        }

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            (await db.Database
                .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM pg_policies WHERE policyname = 'tenant_isolation' AND tablename = 'interesses_item'")
                .SingleAsync()).Should().Be(1);
        }
    }
}
