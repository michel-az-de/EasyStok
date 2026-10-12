using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

public class InboxResponsavelIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NomesEFiltrosUsamVinculoDaEmpresa_InclusiveComRls(bool rls)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        var agora = DateTime.UtcNow;
        var empresa = Empresa.Criar("Inbox atendentes", null);
        var outra = Empresa.Criar("Outra inbox", null);
        var ana = Usuario.Criar("Ana", $"ana-{Guid.NewGuid():N}@local.test", "hash");
        var homonima = Usuario.Criar("Ana", $"ana-{Guid.NewGuid():N}@local.test", "hash");
        var fora = Usuario.Criar("Nome de fora", $"fora-{Guid.NewGuid():N}@local.test", "hash");
        var minha = Conversa.Abrir(empresa.Id, "5511999990001", agora);
        var deOutra = Conversa.Abrir(empresa.Id, "5511999990002", agora.AddSeconds(-1));
        var semDono = Conversa.Abrir(empresa.Id, "5511999990003", agora.AddSeconds(-2));
        var vinculoInvalido = Conversa.Abrir(empresa.Id, "5511999990004", agora.AddSeconds(-3));
        var alheia = Conversa.Abrir(outra.Id, "5511999990005", agora.AddSeconds(-4));
        minha.Assumir(agora, ana.Id); deOutra.Assumir(agora, homonima.Id);
        vinculoInvalido.Assumir(agora, fora.Id); alheia.Assumir(agora, fora.Id);
        await using (var seed = fixture.CreateDbContext())
        {
            using var bypass = seed.UseRowLevelSecurityBypass();
            seed.Empresas.AddRange(empresa, outra); seed.Usuarios.AddRange(ana, homonima, fora);
            seed.UsuariosEmpresas.AddRange(
                new UsuarioEmpresa { Id = Guid.NewGuid(), EmpresaId = empresa.Id, UsuarioId = ana.Id, Ativo = true, CriadoEm = agora },
                new UsuarioEmpresa { Id = Guid.NewGuid(), EmpresaId = empresa.Id, UsuarioId = homonima.Id, Ativo = true, CriadoEm = agora },
                new UsuarioEmpresa { Id = Guid.NewGuid(), EmpresaId = outra.Id, UsuarioId = fora.Id, Ativo = true, CriadoEm = agora });
            seed.AtendimentoConversas.AddRange(minha, deOutra, semDono, vinculoInvalido, alheia);
            await seed.SaveChangesAsync();
        }
        await using var db = rls ? fixture.CreateRlsClientDbContext() : fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa.Id);
        if (rls)
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {empresa.Id.ToString()}, false)");
        }
        var repo = new ConversaRepository(db);
        var todas = await repo.ListarInboxAsync(empresa.Id, null, null, null, 1, 100);
        todas.Should().HaveCount(4).And.NotContain(c => c.Id == alheia.Id);
        todas.Single(c => c.Id == minha.Id).AssumidaPorNome.Should().Be("Ana");
        todas.Single(c => c.Id == deOutra.Id).AssumidaPorNome.Should().Be("Ana");
        todas.Single(c => c.Id == vinculoInvalido.Id).AssumidaPorNome.Should().BeNull();
        todas.Single(c => c.Id == semDono.Id).AssumidaPorNome.Should().BeNull();
        (await repo.ListarInboxAsync(empresa.Id, null, null, new(ana.Id), 1, 100))
            .Should().ContainSingle(c => c.Id == minha.Id);
        (await repo.ListarInboxAsync(empresa.Id, null, null, FiltroResponsavel.Ninguem, 1, 100))
            .Should().ContainSingle(c => c.Id == semDono.Id);
        await using (var alterar = fixture.CreateDbContext())
        {
            alterar.SetMobileTenantContext(empresa.Id);
            var salva = await alterar.AtendimentoConversas.SingleAsync(c => c.Id == minha.Id);
            salva.Transferir(homonima.Id, agora.AddMinutes(1)); await alterar.SaveChangesAsync();
        }
        (await repo.ListarInboxAsync(empresa.Id, null, null, new(ana.Id), 1, 100)).Should().BeEmpty();
        (await repo.ListarInboxAsync(empresa.Id, null, null, new(homonima.Id), 1, 100)).Should().HaveCount(2);
    }
}
