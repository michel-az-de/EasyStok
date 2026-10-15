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
    private async Task<SessaoChatSite> CriarSessaoAsync()
    {
        var empresa = Empresa.Criar("Chat revogacao " + Guid.NewGuid().ToString("N"), null);
        var loja = StorefrontEntity.Criar(empresa.Id, "chat-r-" + Guid.NewGuid().ToString("N"), "Chat", 0m);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var sessao = SessaoChatSite.Abrir(empresa.Id, loja.Id, hash, DateTime.UtcNow);
        await using var db = fixture.CreateDbContext();
        using var bypass = db.UseRowLevelSecurityBypass();
        db.Empresas.Add(empresa);
        db.Set<StorefrontEntity>().Add(loja);
        db.SessoesChatSite.Add(sessao);
        await db.SaveChangesAsync();
        return sessao;
    }

    [SkippableFact]
    public async Task Logout_NaoPermiteRenovacaoEmVooRestaurarBearer()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var inicial = await CriarSessaoAsync();
        var hashOriginal = inicial.TokenHash;
        await using var mensagem = fixture.CreateDbContext();
        mensagem.SetMobileTenantContext(inicial.EmpresaId);
        var antiga = (await new SessaoChatSiteRepository(mensagem).ObterPorTokenHashAsync(inicial.EmpresaId, hashOriginal))!;
        antiga.RegistrarUso(DateTime.UtcNow.AddMinutes(1));

        await using (var logout = fixture.CreateDbContext())
        {
            logout.SetMobileTenantContext(inicial.EmpresaId);
            var atual = (await new SessaoChatSiteRepository(logout).ObterPorTokenHashAsync(inicial.EmpresaId, hashOriginal))!;
            atual.Encerrar(DateTime.UtcNow);
            await logout.SaveChangesAsync();
        }
        // A requisicao de mensagem, que autenticou antes do logout, termina depois dele.
        await mensagem.SaveChangesAsync();
        await using var verificar = fixture.CreateDbContext();
        verificar.SetMobileTenantContext(inicial.EmpresaId);
        var repo = new SessaoChatSiteRepository(verificar);
        (await repo.ObterPorTokenHashAsync(inicial.EmpresaId, hashOriginal)).Should().BeNull();
        (await repo.ObterSnapshotPorTokenHashAsync(inicial.EmpresaId, hashOriginal)).Should().BeNull();
        (await verificar.SessoesChatSite.SingleAsync(s => s.Id == inicial.Id)).TokenHash.Should().NotBe(hashOriginal);
    }

    [SkippableFact]
    public async Task SnapshotDoStream_EnxergaConversaNovaELogoutEmOutroContexto()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var inicial = await CriarSessaoAsync();
        var hash = inicial.TokenHash;
        await using var stream = fixture.CreateDbContext();
        stream.SetMobileTenantContext(inicial.EmpresaId);
        var repo = new SessaoChatSiteRepository(stream);
        var rastreada = (await repo.ObterPorTokenHashAsync(inicial.EmpresaId, hash))!;
        rastreada.ConversaId.Should().BeNull();
        Guid conversaId;
        await using (var mensagem = fixture.CreateDbContext())
        {
            mensagem.SetMobileTenantContext(inicial.EmpresaId);
            var sessao = (await new SessaoChatSiteRepository(mensagem).ObterPorTokenHashAsync(inicial.EmpresaId, hash))!;
            var conversa = Conversa.Abrir(inicial.EmpresaId, sessao.ContatoIdExterno, DateTime.UtcNow, canal: CanalConversa.ChatSite);
            conversaId = conversa.Id;
            mensagem.AtendimentoConversas.Add(conversa);
            sessao.VincularConversa(conversa.Id);
            await mensagem.SaveChangesAsync();
        }
        (await repo.ObterSnapshotPorTokenHashAsync(inicial.EmpresaId, hash))!.ConversaId.Should().Be(conversaId);
        rastreada.ConversaId.Should().BeNull("o snapshot nao reutiliza o objeto rastreado");
        await using (var logout = fixture.CreateDbContext())
        {
            logout.SetMobileTenantContext(inicial.EmpresaId);
            var sessao = (await new SessaoChatSiteRepository(logout).ObterPorTokenHashAsync(inicial.EmpresaId, hash))!;
            sessao.Encerrar(DateTime.UtcNow);
            await logout.SaveChangesAsync();
        }
        (await repo.ObterSnapshotPorTokenHashAsync(inicial.EmpresaId, hash)).Should().BeNull();
    }

    [SkippableFact]
    public async Task Logout_CookieNaoVoltaAtivoQuandoMiddlewareAntigoRenovaDepois()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Empresa.Criar("Sessao cookie " + Guid.NewGuid().ToString("N"), null);
        var cliente = Cliente.Criar(empresa.Id, "Cliente teste revogacao");
        var session = EasyStock.Domain.Entities.Storefront.ClienteSession.Criar(cliente.Id, empresa.Id, TimeProvider.System);
        await using (var seed = fixture.CreateDbContext())
        {
            using var bypass = seed.UseRowLevelSecurityBypass();
            seed.Empresas.Add(empresa);
            seed.Clientes.Add(cliente);
            seed.ClienteSessions.Add(session);
            await seed.SaveChangesAsync();
        }
        await using var antiga = fixture.CreateDbContext();
        antiga.SetMobileTenantContext(empresa.Id);
        var antigoRepo = new EasyStock.Infra.Postgre.Repositories.Storefront.ClienteSessionRepository(antiga);
        var autenticada = (await antigoRepo.GetByIdAsync(session.Id))!;
        autenticada.EstaValida(TimeProvider.System).Should().BeTrue();
        await using (var logout = fixture.CreateDbContext())
        {
            logout.SetMobileTenantContext(empresa.Id);
            var repo = new EasyStock.Infra.Postgre.Repositories.Storefront.ClienteSessionRepository(logout);
            var encerrar = (await repo.GetByIdAsync(session.Id))!;
            encerrar.Revogar("logout");
            await repo.UpdateAsync(encerrar);
            await logout.SaveChangesAsync();
        }
        // O handler guardou a entidade antes do logout; o middleware renova ao terminar a resposta.
        (await antigoRepo.GetByIdAsync(session.Id)).Should().BeSameAs(autenticada);
        autenticada.RegistrarUso(TimeProvider.System);
        await antigoRepo.UpdateAsync(autenticada);
        await antiga.SaveChangesAsync();
        await using var verificar = fixture.CreateDbContext();
        verificar.SetMobileTenantContext(empresa.Id);
        var final = (await new EasyStock.Infra.Postgre.Repositories.Storefront.ClienteSessionRepository(verificar).GetByIdAsync(session.Id))!;
        final.Revogada.Should().BeTrue();
        final.MotivoRevogacao.Should().Be("logout");
        final.EstaValida(TimeProvider.System).Should().BeFalse();
    }

}
