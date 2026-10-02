using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Postgre.IntegrationTests.Notifications;

/// <summary>
/// N5: o template por tipo e canal, com o papel de produção (<c>rls_test_client</c>, NOBYPASSRLS) e o tenant fixado: o
/// global aparece pela policy só de SELECT, sem bypass; a empresa vence o global mesmo com versão menor; a maior versão
/// vence dentro de cada dono; a empresa dos outros nunca entra.
/// </summary>
public class TemplatePorTipoECanalIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const TipoEventoNotificacao Tipo = TipoEventoNotificacao.LembreteVencido;
    private const CanalNotificacao Canal = CanalNotificacao.Sms;
    private readonly MotorNotificacoesSuporte _s = new(fixture);

    private async Task SemearTemplateAsync(string codigo, Guid? empresaId, int versao)
    {
        var template = TemplateNotificacao.Criar(codigo, "Por tipo e canal", Canal, Tipo, "A", "B", empresaId);
        template.DefinirVersao(versao);
        template.Aprovar("teste");
        template.Ativar();
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        db.NotifTemplates.Add(template);
        await db.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task AchaGlobalComPapelNobypassrlsPreferindoEmpresaEMaiorVersao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        var outraEmpresa = await _s.SemearEmpresaAsync();
        var sufixo = Guid.NewGuid().ToString("N");
        var globalAntigo = $"n5-global-v1-{sufixo}";
        var globalNovo = $"n5-global-v900-{sufixo}";
        await SemearTemplateAsync(globalAntigo, null, versao: 899);
        await SemearTemplateAsync(globalNovo, null, versao: 900);
        await SemearTemplateAsync($"n5-outra-{sufixo}", outraEmpresa, versao: 1000);

        await using var provider = _s.ConstruirProviderDaApi(papelRls: true, empresa);
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(empresa);
        var repo = scope.ServiceProvider.GetRequiredService<ITemplateRepository>();

        // Só global e de outra empresa: o global de maior versão, nunca o da outra empresa (RLS e predicado).
        var soGlobal = await repo.GetAtivoPorTipoAsync(Tipo, Canal, empresa);
        soGlobal.Should().NotBeNull("a policy de leitura do catálogo expõe o global ao tenant sem bypass");
        soGlobal!.EmpresaId.Should().BeNull();
        soGlobal.Codigo.Should().Be(globalNovo);

        // Com template da própria empresa, ele vence o global, ainda que de versão menor.
        var daEmpresa = $"n5-empresa-{sufixo}";
        await SemearTemplateAsync(daEmpresa, empresa, versao: 1);
        var comEmpresa = await repo.GetAtivoPorTipoAsync(Tipo, Canal, empresa);
        comEmpresa!.Codigo.Should().Be(daEmpresa);

        // Sem empresa (consulta global): o template da empresa não entra.
        var global = await repo.GetAtivoPorTipoAsync(Tipo, Canal, null);
        global!.EmpresaId.Should().BeNull();
    }
}
