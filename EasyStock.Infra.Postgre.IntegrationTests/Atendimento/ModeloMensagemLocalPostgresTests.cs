using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Repositories.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Atendimento;

/// <summary>#3204: nome e idioma únicos por empresa em Postgres real; a edição que mantém o nome não colide consigo mesma.</summary>
[Collection("PostgreSqlTestCollection")]
public sealed class ModeloMensagemLocalPostgresTests(PostgreSqlDatabaseFixture fixture)
{
    private static ModeloMensagemLocal Modelo(Guid empresa, string nome = "retomar", string idioma = "pt_BR") =>
        ModeloMensagemLocal.Criar(empresa, nome, idioma, "UTILITY", null, "Olá {{1}}, seu pedido saiu.", null, DateTime.UtcNow);

    [SkippableFact]
    public async Task ExisteAsync_ComExcetoId_IgnoraOProprioModeloAoEditarMantendoONome()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid();
        var modelo = Modelo(empresa);
        await using (var grava = fixture.CreateDbContext())
        {
            grava.SetMobileTenantContext(empresa);
            grava.ModelosMensagem.Add(modelo);
            await grava.SaveChangesAsync();
        }

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        var repo = new ModeloMensagemLocalRepository(db);

        (await repo.ExisteAsync(empresa, "retomar", "pt_BR", null)).Should().BeTrue();
        (await repo.ExisteAsync(empresa, "retomar", "pt_BR", modelo.Id)).Should().BeFalse();
        (await repo.ExisteAsync(empresa, "retomar", "pt_BR", Guid.NewGuid())).Should().BeTrue();
        (await repo.ExisteAsync(empresa, "retomar", "en", null)).Should().BeFalse();
    }

    [SkippableFact]
    public async Task NomeEIdiomaDuplicadosNaEmpresa_ViramViolacaoDoIndiceUnico()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid();
        await using (var grava = fixture.CreateDbContext())
        {
            grava.SetMobileTenantContext(empresa);
            grava.ModelosMensagem.Add(Modelo(empresa));
            await grava.SaveChangesAsync();
        }

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        db.ModelosMensagem.Add(Modelo(empresa));

        var act = () => db.SaveChangesAsync();

        (await act.Should().ThrowAsync<DbUpdateException>()).Which.ToString()
            .Should().Contain("ux_modelos_mensagem_empresa_nome_idioma");
    }

    [SkippableFact]
    public async Task MesmoNomeEmOutraEmpresa_NaoColide()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        foreach (var empresa in new[] { a, b })
        {
            await using var db = fixture.CreateDbContext();
            db.SetMobileTenantContext(empresa);
            db.ModelosMensagem.Add(Modelo(empresa));
            await db.SaveChangesAsync();
        }

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(b);
        (await new ModeloMensagemLocalRepository(leitura).ListarAsync(b, true)).Should().ContainSingle();
    }
}
