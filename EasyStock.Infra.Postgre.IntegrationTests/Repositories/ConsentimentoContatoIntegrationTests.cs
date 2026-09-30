using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Consentimento por canal (S38) em Postgres real: o backfill converte ConsentiuMarketing=true em
/// marketing concedido nos canais que o cadastro tem, e a unicidade por cliente/canal/finalidade vale.
/// Fixture por classe: a migration é desfeita e refeita aqui.
/// </summary>
public class ConsentimentoContatoIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string MigrationAntesDaS38 = "20260926191235_RenomeiaContatoConversaPorCanal";

    [SkippableFact]
    public async Task BackfillConverteConsentiuMarketingNosCanaisDoCadastro()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        var empresa = Empresa.Criar("Casa da Baba Consentimento", "11111111000191");
        var comTudo = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Tem tudo", Telefone = "11988887777", Email = "a@x.com", ConsentiuMarketing = true };
        var soEmail = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Só e-mail", Email = "b@x.com", ConsentiuMarketing = true };
        var semConsentir = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresa.Id, Nome = "Não quis", Telefone = "11977776666", ConsentiuMarketing = false };

        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass(); // igual ao boot: migration roda com bypass
        var migrator = db.GetService<IMigrator>();

        // Grava com o schema atual e só depois desce para antes da S38: o modelo EF sempre reflete o
        // schema mais novo, então inserir depois do downgrade quebra quando uma migration posterior
        // acrescenta coluna em empresas ou clientes. As linhas sobrevivem ao Down.
        await migrator.MigrateAsync();
        db.Empresas.Add(empresa);
        db.Clientes.AddRange(comTudo, soEmail, semConsentir);
        await db.SaveChangesAsync();
        await migrator.MigrateAsync(MigrationAntesDaS38);

        await migrator.MigrateAsync();
        db.SetMobileTenantContext(empresa.Id); // o repositório passa pelo filtro global de tenant

        var repo = new ConsentimentoContatoRepository(db);
        (await repo.ListarDoClienteAsync(empresa.Id, comTudo.Id)).Select(c => c.Canal)
            .Should().BeEquivalentTo([CanalConversa.WhatsApp, CanalConversa.Email]);
        (await repo.ListarDoClienteAsync(empresa.Id, soEmail.Id)).Should().ContainSingle()
            .Which.Should().Match<ConsentimentoContato>(c => c.Canal == CanalConversa.Email
                && c.Finalidade == FinalidadeContato.Marketing && c.Situacao == SituacaoConsentimento.Concedido);
        (await repo.ListarDoClienteAsync(empresa.Id, semConsentir.Id)).Should().BeEmpty();

        // Unicidade: uma linha atual por cliente, canal e finalidade.
        db.ConsentimentosContato.Add(ConsentimentoContato.Registrar(empresa.Id, comTudo.Id, CanalConversa.Email,
            FinalidadeContato.Marketing, SituacaoConsentimento.Revogado, "teste", DateTime.UtcNow));
        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
