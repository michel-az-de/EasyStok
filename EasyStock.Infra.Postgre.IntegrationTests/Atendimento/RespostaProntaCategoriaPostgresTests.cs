using EasyStock.Domain.Entities.Atendimento;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Atendimento;

/// <summary>#3208: a coluna Categoria de respostas_prontas existe após as migrações e persiste em Postgres real.</summary>
[Collection("PostgreSqlTestCollection")]
public sealed class RespostaProntaCategoriaPostgresTests(PostgreSqlDatabaseFixture fixture)
{
    private static readonly DateTime Agora = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    private static RespostaPronta Resposta(Guid empresa, string? categoria) =>
        RespostaPronta.Criar(empresa, "Saudação", "oi-" + Guid.NewGuid().ToString("N")[..8], "Olá, tudo bem?", Agora, categoria);

    private async Task Gravar(Guid empresa, RespostaPronta resposta)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        db.RespostasProntas.Add(resposta);
        await db.SaveChangesAsync();
    }

    private async Task<string?> CategoriaNoBanco(Guid empresa, Guid id)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        return await db.RespostasProntas.AsNoTracking().Where(r => r.Id == id).Select(r => r.Categoria).SingleAsync();
    }

    [SkippableFact]
    public async Task ComCategoria_GravaERelêOMesmoValor()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid();
        var resposta = Resposta(empresa, "  Cobrança ");
        await Gravar(empresa, resposta);

        (await CategoriaNoBanco(empresa, resposta.Id)).Should().Be("Cobrança");
    }

    [SkippableFact]
    public async Task SemCategoria_GravaNull()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid();
        var resposta = Resposta(empresa, null);
        await Gravar(empresa, resposta);

        (await CategoriaNoBanco(empresa, resposta.Id)).Should().BeNull();
    }

    [SkippableFact]
    public async Task EditarLimpandoACategoria_PersisteNull()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid();
        var resposta = Resposta(empresa, "Cobrança");
        await Gravar(empresa, resposta);

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresa);
            var salva = await db.RespostasProntas.SingleAsync(r => r.Id == resposta.Id);
            salva.Editar(salva.Titulo, salva.Atalho, salva.Texto, Agora.AddMinutes(1), "  ");
            await db.SaveChangesAsync();
        }

        (await CategoriaNoBanco(empresa, resposta.Id)).Should().BeNull();
    }

    [SkippableFact]
    public async Task ColunaCategoria_ExisteComoVarchar40Anulavel()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        await using var db = fixture.CreateDbContext();
        var colunas = await db.Database.SqlQuery<string>($"""
            SELECT data_type || '(' || character_maximum_length || ')/' || is_nullable AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'respostas_prontas' AND column_name = 'Categoria'
            """).ToListAsync();

        colunas.Should().Equal("character varying(40)/YES");
    }

    [SkippableFact]
    public async Task Rls_naoMostraACategoriaDeOutraEmpresa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "PostgreSQL indisponível");
        var empresa = Guid.NewGuid(); var outra = Guid.NewGuid();
        var daOutra = Resposta(outra, "Sigilo");
        await Gravar(empresa, Resposta(empresa, "Cobrança"));
        await Gravar(outra, daOutra);

        await using var db = fixture.CreateRlsClientDbContext();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {empresa.ToString()}, false)");

        var vistas = await db.RespostasProntas.IgnoreQueryFilters().Select(r => new { r.EmpresaId, r.Categoria }).ToListAsync();
        vistas.Should().ContainSingle().Which.Should().Be(new { EmpresaId = empresa, Categoria = (string?)"Cobrança" });
        vistas.Should().NotContain(v => v.Categoria == "Sigilo");
    }
}
