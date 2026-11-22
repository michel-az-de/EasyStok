using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Acervo;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #3191: a busca do acervo usa o padrão do projeto (ILIKE com o termo escapado) em vez de
/// <c>lower()</c> na coluna. O comportamento não muda: ignora maiúsculas e trata curinga como caractere.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class AcervoBuscaIlike3191Tests(PostgreSqlDatabaseFixture fixture)
{
    private static readonly string[] Titulos = ["Bolo de Pote", "Kit_festa", "Desconto 10%", "Pão de Açúcar", @"Barra\invertida"];

    private async Task<Guid> SemearAsync()
    {
        await using var db = fixture.CreateDbContext();
        var empresaId = Guid.NewGuid();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(new Empresa
        {
            Id = empresaId, Nome = "Acervo 3191", Documento = empresaId.ToString("N")[..14],
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
        });
        db.AddRange(Titulos.Select(titulo => new AcervoImagem
        {
            EmpresaId = empresaId, Titulo = titulo, Sha256 = Guid.NewGuid().ToString("N")
        }));
        await db.SaveChangesAsync();
        return empresaId;
    }

    private static Task<IReadOnlyList<AcervoImagem>> BuscarAsync(EasyStockDbContext db, Guid empresaId, string busca) =>
        new AcervoRepository(db).BuscarAsync(empresaId, 0, busca, "", "", "", null, "", CancellationToken.None);

    [SkippableTheory]
    [InlineData("bolo", "Bolo de Pote")]
    [InlineData("DE POTE", "Bolo de Pote")]
    [InlineData("_", "Kit_festa")]
    [InlineData("%", "Desconto 10%")]
    [InlineData(@"\", @"Barra\invertida")]
    [InlineData("AÇÚCAR", "Pão de Açúcar")]
    public async Task Busca_ignora_caixa_e_trata_curinga_como_caractere(string busca, string esperado)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaId = await SemearAsync();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);

        var achados = await BuscarAsync(db, empresaId, busca);

        achados.Select(x => x.Titulo).Should().Equal(esperado);
    }

    [SkippableFact]
    public async Task Busca_vazia_devolve_todas_as_imagens_da_empresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaId = await SemearAsync();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);

        (await BuscarAsync(db, empresaId, "")).Should().HaveCount(Titulos.Length);
    }

    [SkippableFact]
    public async Task Consulta_usa_ILIKE_sem_lower_na_coluna()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaId = await SemearAsync();
        var comandos = new List<string>();
        await using var db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .LogTo(comandos.Add, [RelationalEventId.CommandExecuted])
            .Options);
        db.SetMobileTenantContext(empresaId);

        (await BuscarAsync(db, empresaId, "bolo")).Should().ContainSingle();

        var consulta = comandos.Should().ContainSingle(sql => sql.Contains("acervo_imagens")).Subject;
        consulta.Should().Contain("ILIKE").And.NotContain("lower(");
    }
}
