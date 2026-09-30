using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Chat do site (S36) em Postgres real: sessão achada pelo hash só dentro da empresa, limpeza das
/// vencidas atravessando empresas, RLS na tabela nova e o cursor "depois de" das mensagens.
/// </summary>
public class SessaoChatSiteIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task SessaoPorHashLimpezaERls()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var agora = DateTime.UtcNow;
        var empresaA = Empresa.Criar("Chat A", "11111111000191");
        var empresaB = Empresa.Criar("Chat B", "22222222000191");
        var lojaA = StorefrontEntity.Criar(empresaA.Id, "chat-a-" + Guid.NewGuid().ToString("N")[..6], "Chat A", 0m);
        var lojaB = StorefrontEntity.Criar(empresaB.Id, "chat-b-" + Guid.NewGuid().ToString("N")[..6], "Chat B", 0m);
        var valida = SessaoChatSite.Abrir(empresaA.Id, lojaA.Id, new string('a', 64), agora);
        var vencidaA = SessaoChatSite.Abrir(empresaA.Id, lojaA.Id, new string('b', 64), agora.AddDays(-3));
        var vencidaB = SessaoChatSite.Abrir(empresaB.Id, lojaB.Id, new string('c', 64), agora.AddDays(-3));

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            db.Empresas.AddRange(empresaA, empresaB);
            db.Set<StorefrontEntity>().AddRange(lojaA, lojaB);
            db.SessoesChatSite.AddRange(valida, vencidaA, vencidaB);
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaA.Id);
            var repo = new SessaoChatSiteRepository(db);
            (await repo.ObterPorTokenHashAsync(empresaA.Id, new string('a', 64)))!.Id.Should().Be(valida.Id);
            (await repo.ObterPorTokenHashAsync(empresaA.Id, new string('c', 64))).Should().BeNull("a sessão é de outra empresa");
        }

        await using (var db = fixture.CreateDbContext())
        {
            var removidas = await new SessaoChatSiteRepository(db).RemoverVencidasAsync(agora.AddDays(-1));
            removidas.Should().Be(2, "a limpeza atravessa empresas");
        }

        await using (var db = fixture.CreateDbContext())
        {
            using var _ = db.UseRowLevelSecurityBypass();
            (await db.SessoesChatSite.IgnoreQueryFilters().Select(s => s.Id).ToListAsync()).Should().Contain(valida.Id)
                .And.NotContain([vencidaA.Id, vencidaB.Id]);
            (await db.Database
                .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM pg_policies WHERE policyname = 'tenant_isolation' AND tablename = 'sessoes_chat_site'")
                .SingleAsync())
                .Should().Be(1);
        }
    }

    [SkippableFact]
    public async Task MensagensDepoisDoCursorEmOrdem()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        var agora = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        var conversa = Conversa.Abrir(empresa, Guid.NewGuid().ToString("N"), agora, canal: CanalConversa.ChatSite);
        db.AtendimentoConversas.Add(conversa);
        db.AtendimentoMensagens.AddRange(
            Mensagem.Entrada(empresa, conversa.Id, agora, TipoConteudoMensagem.Texto, "1"),
            Mensagem.Saida(empresa, conversa.Id, AutorMensagem.Dona, agora.AddSeconds(1), TipoConteudoMensagem.Texto, "2", "chatsite:2"),
            Mensagem.Entrada(empresa, conversa.Id, agora.AddSeconds(2), TipoConteudoMensagem.Texto, "3"));
        await db.SaveChangesAsync();

        var repo = new ConversaRepository(db);
        (await repo.ListarMensagensDepoisAsync(empresa, conversa.Id, null, 10)).Select(m => m.Texto).Should().Equal("1", "2", "3");
        (await repo.ListarMensagensDepoisAsync(empresa, conversa.Id, agora, 10)).Select(m => m.Texto).Should().Equal("2", "3");
        (await repo.ListarMensagensDepoisAsync(empresa, conversa.Id, agora.AddSeconds(2), 10)).Should().BeEmpty();
    }
}
