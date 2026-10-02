using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Postgre.IntegrationTests.Notifications;

/// <summary>N5: <c>ListarAtivasAsync</c> devolve a rotina da empresa antes da global, depois a mais antiga.</summary>
public class RotinaNotificacaoRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const TipoEventoNotificacao Tipo = TipoEventoNotificacao.ReembolsoEfetuado;
    private readonly MotorNotificacoesSuporte _s = new(fixture);

    private async Task<Guid> SemearRotinaAsync(Guid? empresaId, DateTime criadaEm)
    {
        var codigo = $"n5-rotina-{Guid.NewGuid():N}";
        var rotina = RotinaNotificacao.Criar(codigo, "Rotina", Tipo, TriggerTipoRotina.Evento, codigo,
            CategoriaConteudoNotificacao.Operacional, empresaId: empresaId);
        rotina.DefinirFallback("[\"Email\"]", "teste");
        rotina.Ativar("teste");
        rotina.CriadaEm = criadaEm;
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        db.NotifRotinas.Add(rotina);
        await db.SaveChangesAsync();
        return rotina.Id;
    }

    [SkippableFact]
    public async Task ListarAtivasOrdenaEmpresaAntesDeGlobalComTenantLigado()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        var agora = DateTime.UtcNow;
        // A global é a mais antiga e a da empresa a mais nova: só a regra "empresa primeiro" as põe nesta ordem.
        var global = await SemearRotinaAsync(null, agora.AddDays(-30));
        var empresaNova = await SemearRotinaAsync(empresa, agora.AddDays(-1));
        var empresaAntiga = await SemearRotinaAsync(empresa, agora.AddDays(-10));

        await using var provider = _s.ConstruirProviderDaApi(papelRls: true, empresa);
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(empresa);

        var rotinas = await scope.ServiceProvider.GetRequiredService<IRotinaRepository>().ListarAtivasAsync(Tipo, empresa);

        var ids = rotinas.Select(r => r.Id).ToList();
        ids.Should().Contain([global, empresaNova, empresaAntiga]);
        ids.IndexOf(empresaAntiga).Should().BeLessThan(ids.IndexOf(empresaNova), "dentro da empresa, a mais antiga primeiro");
        ids.IndexOf(empresaNova).Should().BeLessThan(ids.IndexOf(global), "a da empresa vem antes da global");
        rotinas.Where(r => r.EmpresaId == empresa).Select(r => r.Id).Should().Equal(empresaAntiga, empresaNova);
    }
}
