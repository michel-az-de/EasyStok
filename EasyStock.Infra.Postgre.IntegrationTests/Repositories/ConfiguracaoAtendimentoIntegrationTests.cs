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

    /// <summary>S68 (#2597): os campos do Assistente vão e voltam do banco; o roteiro fica em jsonb.</summary>
    [SkippableFact]
    public async Task AssistentePorLojaVaiEVoltaDoBanco()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        const string roteiro = """[{"nome":"Saudar","instrucao":"Cumprimente.","ativo":true}]""";
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            db.Empresas.Add(new Empresa { Id = empresa, Nome = "Casa", Documento = empresa.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            var configuracao = ConfiguracaoAtendimento.CriarPadrao(empresa);
            configuracao.DefinirAssistente("Michele", "claude-haiku-5", 2_000_000, roteiro, "Sem glúten só por encomenda.");
            db.ConfiguracoesAtendimento.Add(configuracao);
            await db.SaveChangesAsync();
        }

        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(empresa);
        var salva = await conferir.ConfiguracoesAtendimento.AsNoTracking().SingleAsync(c => c.EmpresaId == empresa);
        salva.NomeDona.Should().Be("Michele");
        salva.ModeloAgente.Should().Be("claude-haiku-5");
        salva.CotaTokensMes.Should().Be(2_000_000);
        salva.RoteiroJson.Should().Contain("\"Saudar\"");
        salva.ObservacoesLoja.Should().Be("Sem glúten só por encomenda.");
        salva.MensagemEscalada.Should().Be(ConfiguracaoAtendimento.MensagemEscaladaPadrao);
    }

    /// <summary>#3139: o dicionário de termos da loja fica em jsonb e volta igual, com acento.</summary>
    [SkippableFact]
    public async Task DicionarioDaLojaVaiEVoltaDoBanco()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        var json = EasyStock.Application.Services.Atendimento.DicionarioDaLoja.Serializar(
            [new("açaí", "fruta roxa servida gelada"), new("pote", "bolo no copo")]);
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            db.Empresas.Add(new Empresa { Id = empresa, Nome = "Casa", Documento = empresa.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            var configuracao = ConfiguracaoAtendimento.CriarPadrao(empresa);
            configuracao.DefinirDicionario(json);
            db.ConfiguracoesAtendimento.Add(configuracao);
            await db.SaveChangesAsync();
        }

        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(empresa);
        var salva = await conferir.ConfiguracoesAtendimento.AsNoTracking().SingleAsync(c => c.EmpresaId == empresa);
        EasyStock.Application.Services.Atendimento.DicionarioDaLoja.DaLoja(salva.DicionarioJson).Select(t => t.Termo)
            .Should().Equal("açaí", "pote");
    }
}
