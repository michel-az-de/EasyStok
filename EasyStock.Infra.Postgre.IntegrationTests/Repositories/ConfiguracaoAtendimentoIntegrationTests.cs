using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #1889: o agente automático nasce desligado. A coluna <c>Ativo</c> tem default <c>true</c> no banco; o EF não pode
/// tratar o <c>false</c> da entidade como "não informado" e religar o agente ao gravar a configuração.
/// </summary>
public class ConfiguracaoAtendimentoIntegrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task PadraoDesligadoPersisteDesligado()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            db.Empresas.Add(new Empresa { Id = empresa, Nome = "Casa", Documento = empresa.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            db.ConfiguracoesAtendimento.Add(ConfiguracaoAtendimento.CriarPadrao(empresa));
            await db.SaveChangesAsync();
        }

        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(empresa);
        var salva = await conferir.ConfiguracoesAtendimento.AsNoTracking().SingleAsync(c => c.EmpresaId == empresa);
        salva.Ativo.Should().BeFalse("o padrão gravado é desligado; o default da coluna não pode religar");
    }
}
