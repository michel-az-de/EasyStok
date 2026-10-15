using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications.Orchestrators;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Postgre.IntegrationTests.Notifications;

/// <summary>
/// N4 em Postgres real e sob o papel de produção (<c>rls_test_client</c>, NOBYPASSRLS): a audiência de superadmins sai da
/// porta de bypass num escopo próprio, a de admins nunca atravessa a empresa do evento, e duas pessoas com a mesma chave
/// de negócio geram duas mensagens. As sementes vão pelo superusuário.
/// </summary>
public class AudienciaIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private readonly MotorNotificacoesSuporte _s = new(fixture);

    [SkippableFact]
    public async Task SuperAdminsSaoLidosComNobypassrlsPelaPortaDeBypass()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        var superAdmin = await SemearSuperAdminAsync("super", ativo: true);
        var superInativo = await SemearSuperAdminAsync("inativo", ativo: false);
        var adminDaEmpresa = await SemearUsuarioComPerfilAsync(empresa, NivelAcesso.Admin, "admin");
        await using var provider = _s.ConstruirProviderDoWorker(papelRls: true);

        // Escopo do evento: tenant da empresa fixado. A consulta de superadmins NÃO usa este escopo.
        await using var escopoDoEvento = provider.CreateAsyncScope();
        escopoDoEvento.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(empresa);

        var lidos = await escopoDoEvento.ServiceProvider.GetRequiredService<ISuperAdminsDaPlataforma>().ListarAsync();

        lidos.Select(u => u.Id).Should().Contain(superAdmin);
        lidos.Select(u => u.Id).Should().NotContain([superInativo, adminDaEmpresa.Id], "só superadmin ativo; admin de empresa não é superadmin");
        lidos.Should().OnlyContain(u => u.Ativo);

        // Prova de que o bypass é o que dá a visão: o mesmo escopo, sem a porta, não enxerga os perfis globais.
        await using var db = escopoDoEvento.ServiceProvider.GetRequiredService<EasyStock.Infra.Postgre.Data.EasyStockDbContext>();
        (await db.UsuariosPerfis.IgnoreQueryFilters().CountAsync(up => up.UsuarioId == superAdmin))
            .Should().Be(0, "sob RLS e sem bypass a linha global de UsuarioPerfil é invisível");
    }

    [SkippableFact]
    public async Task AdminsDeOutraEmpresaNaoVazam()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaA = await _s.SemearEmpresaAsync();
        var empresaB = await _s.SemearEmpresaAsync();
        var adminA = await SemearUsuarioComPerfilAsync(empresaA, NivelAcesso.Admin, "adminA");
        var gerenteA = await SemearUsuarioComPerfilAsync(empresaA, NivelAcesso.Gerente, "gerenteA");
        var operadorA = await SemearUsuarioComPerfilAsync(empresaA, NivelAcesso.Operador, "operadorA");
        var adminB = await SemearUsuarioComPerfilAsync(empresaB, NivelAcesso.Admin, "adminB");
        await using var provider = _s.ConstruirProviderDoWorker(papelRls: true);

        await using var escopo = provider.CreateAsyncScope();
        escopo.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(empresaA);
        var usuarios = escopo.ServiceProvider.GetRequiredService<IAudienciaUsuarios>();

        var admins = await usuarios.ListarDaEmpresaAsync(empresaA, [NivelAcesso.Admin]);
        var gestores = await usuarios.ListarDaEmpresaAsync(empresaA, [NivelAcesso.Admin, NivelAcesso.Gerente]);
        var deB = await usuarios.ListarDaEmpresaAsync(empresaB, [NivelAcesso.Admin]);

        admins.Select(u => u.Id).Should().BeEquivalentTo([adminA.Id]);
        gestores.Select(u => u.Id).Should().BeEquivalentTo([adminA.Id, gerenteA.Id]);
        gestores.Select(u => u.Id).Should().NotContain([operadorA.Id, adminB.Id]);
        deB.Should().BeEmpty("sob o tenant da empresa A, a empresa B não é visível");
    }

    [SkippableFact]
    public async Task DoisAdminsComAMesmaChaveDeNegocioGeramDuasMensagens()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        var ana = await SemearUsuarioComPerfilAsync(empresa, NivelAcesso.Admin, "ana");
        var leo = await SemearUsuarioComPerfilAsync(empresa, NivelAcesso.Admin, "leo");
        await SemearRotinaDaEmpresaAsync(empresa, TipoEventoNotificacao.ParcelaRecebida, """{"modoCanais":"todos","audiencia":"admins"}""");
        var chave = $"aud:{Guid.NewGuid():N}";
        var evento = await _s.SemearEventoPendenteAsync(
            empresa, TipoEventoNotificacao.ParcelaRecebida,
            $$"""{"nome":"Equipe","email":"cliente@fora.com","chaveIdempotencia":"{{chave}}"}""");
        await using var provider = _s.ConstruirProviderDoWorker(papelRls: true);

        await using (var escopo = provider.CreateAsyncScope())
            await escopo.ServiceProvider.GetRequiredService<INotificacoesAvaliadorOrchestrator>()
                .ExecutarRodadaAsync(TimeSpan.FromMinutes(2));

        (await _s.LerEventoAsync(evento.Id)).Status.Should().Be(StatusEventoNotificacao.Processado);
        var mensagens = await _s.LerMensagensDoEventoAsync(evento.Id);
        mensagens.Should().HaveCount(2, "o segundo admin não pode ser barrado pelo índice único nem por ExisteAsync");
        mensagens.Select(m => m.UsuarioDestinoId).Should().BeEquivalentTo(new Guid?[] { ana.Id, leo.Id });
        mensagens.Select(m => m.Destinatario).Should().BeEquivalentTo([ana.Email, leo.Email]);
        mensagens.Select(m => m.IdempotencyKey).Distinct().Should().HaveCount(2);
        mensagens.Select(m => m.Destinatario).Should().NotContain("cliente@fora.com");
    }

    // ----- sementes -----

    private async Task SemearRotinaDaEmpresaAsync(Guid empresaId, TipoEventoNotificacao tipo, string parametrosJson)
    {
        var codigo = $"aud-{Guid.NewGuid():N}";
        var rotina = RotinaNotificacao.Criar(
            codigo, "Rotina com audiência", tipo, TriggerTipoRotina.Evento, codigo,
            CategoriaConteudoNotificacao.Operacional, empresaId: empresaId);
        rotina.CanaisOrdemFallbackJson = "[\"Email\"]";
        rotina.DefinirParametros(parametrosJson, "teste");
        rotina.Ativar("teste");
        var template = TemplateNotificacao.Criar(codigo, "Template", CanalNotificacao.Email, tipo, "Parcela", "Parcela recebida", empresaId);
        template.Aprovar("teste");
        template.Ativar();

        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        db.NotifRotinas.Add(rotina);
        db.NotifTemplates.Add(template);
        if (!await db.NotifConfiguracoesCanal.IgnoreQueryFilters().AnyAsync(c => c.Canal == CanalNotificacao.Email && c.EmpresaId == null))
            db.NotifConfiguracoesCanal.Add(ConfiguracaoCanal.Criar(CanalNotificacao.Email, "stub"));
        await db.SaveChangesAsync();
    }

    private async Task<Usuario> SemearUsuarioComPerfilAsync(Guid empresaId, NivelAcesso nivel, string nome)
    {
        var usuario = Usuario.Criar(nome, $"{nome}-{Guid.NewGuid():N}@casadababa.com", "hash");
        usuario.EmailConfirmado = true;
        var perfil = new Perfil { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = $"{nivel}-{nome}", Nivel = nivel, CriadoEm = DateTime.UtcNow };

        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        db.Usuarios.Add(usuario);
        db.Perfis.Add(perfil);
        await db.SaveChangesAsync();
        db.UsuariosEmpresas.Add(new UsuarioEmpresa
        {
            Id = Guid.NewGuid(), UsuarioId = usuario.Id, EmpresaId = empresaId, Ativo = true, CriadoEm = DateTime.UtcNow
        });
        db.UsuariosPerfis.Add(new UsuarioPerfil
        {
            Id = Guid.NewGuid(), UsuarioId = usuario.Id, PerfilId = perfil.Id, EmpresaId = empresaId, AtribuidoEm = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return usuario;
    }

    /// <summary>Como o <c>SuperAdminSeed</c>: perfil global (EmpresaId nulo), vínculo com Guid.Empty e nenhum UsuarioEmpresa.</summary>
    private async Task<Guid> SemearSuperAdminAsync(string nome, bool ativo)
    {
        var usuario = Usuario.Criar(nome, $"{nome}-{Guid.NewGuid():N}@casadababa.com", "hash");
        usuario.Ativo = ativo;
        usuario.EmailConfirmado = true;
        var perfil = new Perfil { Id = Guid.NewGuid(), EmpresaId = null, Nome = "SuperAdmin", Nivel = NivelAcesso.SuperAdmin, CriadoEm = DateTime.UtcNow };

        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        db.Usuarios.Add(usuario);
        db.Perfis.Add(perfil);
        await db.SaveChangesAsync();
        db.UsuariosPerfis.Add(new UsuarioPerfil
        {
            Id = Guid.NewGuid(), UsuarioId = usuario.Id, PerfilId = perfil.Id, EmpresaId = Guid.Empty, AtribuidoEm = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return usuario.Id;
    }
}
