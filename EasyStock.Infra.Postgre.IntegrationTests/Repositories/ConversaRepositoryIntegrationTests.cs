using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Persistencia do agregado Conversa/Mensagem (S04, ADR-0050) em Postgres real:
/// uma conversa aberta por contato (indice unico parcial), isolamento de tenant
/// pelo filtro global do DbContext, unicidade do wamid por empresa, ultimas N
/// mensagens em ordem cronologica, e a migration subindo e descendo limpa com a
/// policy de RLS nas duas tabelas novas.
///
/// Fixture por classe: a migration e desfeita e refeita no ultimo cenario, entao
/// o banco nao e compartilhado com outras classes.
/// </summary>
public class ConversaRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string MigrationAnterior = "20260809132157_AddClientePessoaJuridica";
    private static readonly DateTime Agora = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task UmaAbertaPorContato()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        const string wa = "5511999990001";

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa); // override publico do tenant: liga o filtro global sem JWT

        var primeira = Conversa.Abrir(empresa, wa, Agora);
        db.AtendimentoConversas.Add(primeira);
        await db.SaveChangesAsync();

        var duplicada = Conversa.Abrir(empresa, wa, Agora.AddMinutes(1));
        db.AtendimentoConversas.Add(duplicada);
        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>("so pode haver uma conversa aberta por contato e empresa");
        db.Entry(duplicada).State = EntityState.Detached;

        primeira.Encerrar(Agora.AddMinutes(2));
        await db.SaveChangesAsync();

        var seguinte = Conversa.Abrir(empresa, wa, Agora.AddMinutes(3));
        db.AtendimentoConversas.Add(seguinte);
        await db.SaveChangesAsync();

        var repo = new ConversaRepository(db);
        var aberta = await repo.ObterAbertaPorContatoAsync(empresa, wa);
        aberta.Should().NotBeNull();
        aberta!.Id.Should().Be(seguinte.Id, "apos encerrar, o mesmo contato pode abrir outra conversa");
    }

    [SkippableFact]
    public async Task IsolamentoDeTenant()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        const string wa = "5511999990002";

        Guid idA, idB;
        await using (var dbA = fixture.CreateDbContext())
        {
            dbA.SetMobileTenantContext(empresaA);
            var c = Conversa.Abrir(empresaA, wa, Agora);
            dbA.AtendimentoConversas.Add(c);
            await dbA.SaveChangesAsync();
            idA = c.Id;
        }
        await using (var dbB = fixture.CreateDbContext())
        {
            dbB.SetMobileTenantContext(empresaB);
            var c = Conversa.Abrir(empresaB, wa, Agora);
            dbB.AtendimentoConversas.Add(c);
            await dbB.SaveChangesAsync();
            idB = c.Id;
        }

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaA);
        var repo = new ConversaRepository(db);

        var daEmpresaA = await repo.ListarAsync(empresaA, situacao: null, pagina: 1, tamanhoPagina: 50);
        daEmpresaA.Select(c => c.Id).Should().Contain(idA).And.NotContain(idB);

        (await repo.ObterPorIdAsync(empresaA, idB)).Should().BeNull("conversa de outra empresa e invisivel");
        (await repo.ObterAbertaPorContatoAsync(empresaA, wa))!.Id.Should().Be(idA);

        await using var semTenant = fixture.CreateDbContext();
        (await semTenant.AtendimentoConversas.CountAsync(c => c.ContatoWaId == wa))
            .Should().Be(0, "sem tenant na sessao o filtro global e fail-closed");
        (await semTenant.AtendimentoConversas.IgnoreQueryFilters().CountAsync(c => c.ContatoWaId == wa))
            .Should().Be(2);
    }

    [SkippableFact]
    public async Task ObterComMensagens_RetornaUltimasNEmOrdemCronologica()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        var conversa = Conversa.Abrir(empresa, "5511999990003", Agora);
        db.AtendimentoConversas.Add(conversa);
        for (var i = 0; i < 5; i++)
        {
            var em = Agora.AddMinutes(i);
            var msg = i % 2 == 0
                ? Mensagem.Entrada(empresa, conversa.Id, em, TipoConteudoMensagem.Texto, $"cliente {i}", externoId: $"wamid.in.{i}")
                : Mensagem.Saida(empresa, conversa.Id, AutorMensagem.Agente, em, TipoConteudoMensagem.Texto, $"agente {i}", externoId: $"wamid.out.{i}");
            db.AtendimentoMensagens.Add(msg);
        }
        await db.SaveChangesAsync();

        var repo = new ConversaRepository(db);
        var resultado = await repo.ObterComMensagensAsync(empresa, conversa.Id, ultimasN: 3);

        resultado.Should().NotBeNull();
        resultado!.Conversa.Id.Should().Be(conversa.Id);
        resultado.Mensagens.Should().HaveCount(3);
        resultado.Mensagens.Select(m => m.Texto).Should().ContainInOrder("cliente 2", "agente 3", "cliente 4");
    }

    [SkippableFact]
    public async Task ExternoId_UnicoPorEmpresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        const string wamid = "wamid.HBgMNTU1MTk5OTk5MDAwNBUCABIYFjNFQjBGQzQ0";

        await using var dbA = fixture.CreateDbContext();
        dbA.SetMobileTenantContext(empresaA);
        var conversaA = Conversa.Abrir(empresaA, "5511999990004", Agora);
        dbA.AtendimentoConversas.Add(conversaA);
        dbA.AtendimentoMensagens.Add(Mensagem.Entrada(empresaA, conversaA.Id, Agora, TipoConteudoMensagem.Texto, "a", externoId: wamid));
        await dbA.SaveChangesAsync();

        var repetida = Mensagem.Entrada(empresaA, conversaA.Id, Agora.AddSeconds(1), TipoConteudoMensagem.Texto, "a de novo", externoId: wamid);
        dbA.AtendimentoMensagens.Add(repetida);
        var act = () => dbA.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>("o mesmo wamid nao entra duas vezes na mesma empresa");
        dbA.Entry(repetida).State = EntityState.Detached;

        await using var dbB = fixture.CreateDbContext();
        dbB.SetMobileTenantContext(empresaB);
        var conversaB = Conversa.Abrir(empresaB, "5511999990004", Agora);
        dbB.AtendimentoConversas.Add(conversaB);
        dbB.AtendimentoMensagens.Add(Mensagem.Entrada(empresaB, conversaB.Id, Agora, TipoConteudoMensagem.Texto, "b", externoId: wamid));
        await dbB.SaveChangesAsync();

        var repoA = new ConversaRepository(dbA);
        var encontrada = await repoA.ObterMensagemPorExternoIdAsync(empresaA, wamid);
        encontrada.Should().NotBeNull();
        encontrada!.ConversaId.Should().Be(conversaA.Id);
    }

    [SkippableFact]
    public async Task Migration_SobeEDesceLimpaComRls()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");

        await using var db = fixture.CreateDbContext();
        var migrator = db.GetService<IMigrator>();

        (await ContarTabelasAsync(db)).Should().Be(2);
        (await ContarPoliciesAsync(db)).Should().Be(2, "as duas tabelas novas precisam da policy tenant_isolation (ADR-0010)");

        await migrator.MigrateAsync(MigrationAnterior);
        (await ContarTabelasAsync(db)).Should().Be(0, "Down remove as duas tabelas");

        await migrator.MigrateAsync();
        (await ContarTabelasAsync(db)).Should().Be(2);
        (await ContarPoliciesAsync(db)).Should().Be(2);
    }

    private static Task<int> ContarTabelasAsync(DbContext db) =>
        db.Database
            .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = current_schema() AND table_name IN ('atendimento_conversas', 'atendimento_mensagens')")
            .SingleAsync();

    private static Task<int> ContarPoliciesAsync(DbContext db) =>
        db.Database
            .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM pg_policies WHERE policyname = 'tenant_isolation' AND tablename IN ('atendimento_conversas', 'atendimento_mensagens')")
            .SingleAsync();
}
