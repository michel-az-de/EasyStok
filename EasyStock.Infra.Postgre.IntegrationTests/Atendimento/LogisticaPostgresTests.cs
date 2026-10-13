using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Atendimento;

[Collection("PostgreSqlTestCollection")]
public sealed class LogisticaPostgresTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task Rls_esconde_cotacao_despacho_e_politica_mesmo_sem_filtro_ef()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid(); var outra = Guid.NewGuid();
        await Semear(empresa); await Semear(outra);
        await using var db = fixture.CreateRlsClientDbContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {empresa.ToString()}, false)");
        (await db.Set<CotacaoLogistica>().IgnoreQueryFilters().Select(c => c.EmpresaId).ToListAsync()).Should().Equal(empresa);
        (await db.Set<DespachoLogistica>().IgnoreQueryFilters().Select(c => c.EmpresaId).ToListAsync()).Should().Equal(empresa);
        (await db.Set<PoliticaLogistica>().IgnoreQueryFilters().Select(c => c.EmpresaId).ToListAsync()).Should().Equal(empresa);
        db.Set<PoliticaLogistica>().Add(new() { EmpresaId = Guid.NewGuid() });
        await FluentActions.Invoking(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }

    [SkippableFact]
    public async Task Chave_contratacao_unica_impede_duplicar_no_banco()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid(); var cotacao = await Semear(empresa);
        await using var db = fixture.CreateDbContext(); db.SetMobileTenantContext(empresa);
        db.Set<DespachoLogistica>().Add(new() { EmpresaId = empresa, CotacaoId = cotacao, ChaveIdempotencia = "uma-operacao", CriadoEm = DateTime.UtcNow });
        await FluentActions.Invoking(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }

    [SkippableFact]
    public async Task Estado_json_e_custo_sobrevivem_a_novo_contexto()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid(); await Semear(empresa);
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            var despacho = await db.Set<DespachoLogistica>().SingleAsync();
            despacho.RegistrarEvento("a_caminho", null);
            despacho.EventosJson = "[{\"tipo\":\"a_caminho\"}]";
            await db.SaveChangesAsync();
        }
        await using var leitura = fixture.CreateDbContext(); leitura.SetMobileTenantContext(empresa);
        var salvo = await leitura.Set<DespachoLogistica>().SingleAsync();
        salvo.Status.Should().Be("a_caminho"); salvo.ValorCentavos.Should().Be(1600);
        salvo.EventosJson.Should().Contain("a_caminho");
    }

    private async Task<Guid> Semear(Guid empresa)
    {
        await using var db = fixture.CreateDbContext(); db.SetMobileTenantContext(empresa);
        db.Empresas.Add(new Empresa { Id = empresa, Nome = "Logística teste", Documento = empresa.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        var cotacao = new CotacaoLogistica { EmpresaId = empresa, Provedor = "lalamove", ValorCentavos = 1600, DuracaoMinutos = 30, CriadoEm = DateTime.UtcNow, ExpiraEm = DateTime.UtcNow.AddMinutes(5) };
        db.Add(cotacao);
        var viagem = Viagem.Criar(empresa, DateTime.UtcNow); db.Add(viagem);
        db.Add(new DespachoLogistica { EmpresaId = empresa, CotacaoId = cotacao.Id, ViagemId = viagem.Id, ChaveIdempotencia = "uma-operacao", ValorCentavos = 1600, CriadoEm = DateTime.UtcNow });
        db.Add(new PoliticaLogistica { EmpresaId = empresa });
        await db.SaveChangesAsync();
        return cotacao.Id;
    }
}
