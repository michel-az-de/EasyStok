using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2199 (D4): o número curto do lote, conteúdo do Code128 C. Sai do gatilho <c>trg_lotes_numero_curto</c> da migração,
/// sequencial por empresa e loja; lote antigo ganha o seu sob demanda, uma vez só.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class NumeroCurtoLoteIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task Lote_novo_ganha_8_digitos_em_sequencia_propria_de_cada_loja()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        var (empresa, lojaA, lojaB) = await SemearAsync();

        await using var db = Contexto(empresa.Id);
        var a1 = Lote.Criar(empresa.Id, Codigo(), lojaId: lojaA.Id);
        var a2 = Lote.Criar(empresa.Id, Codigo(), lojaId: lojaA.Id);
        var b1 = Lote.Criar(empresa.Id, Codigo(), lojaId: lojaB.Id);
        var semLoja = Lote.Criar(empresa.Id, Codigo());
        db.Lotes.Add(a1);
        await db.SaveChangesAsync();
        db.Lotes.AddRange(a2, b1, semLoja);
        await db.SaveChangesAsync();

        // O EF lê de volta o que o gatilho gravou: o lote recém-criado já responde com o número.
        a1.NumeroCurto.Should().Be("00000001");
        a2.NumeroCurto.Should().Be("00000002");
        b1.NumeroCurto.Should().Be("00000001");
        semLoja.NumeroCurto.Should().Be("00000001");
    }

    [SkippableFact]
    public async Task Depois_de_99999999_o_numero_passa_a_10_digitos_para_seguir_par_no_Code128_C()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        var (empresa, loja, _) = await SemearAsync();
        await using var db = Contexto(empresa.Id);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""INSERT INTO lote_numero_contador ("EmpresaId", "LojaId", "Ultimo") VALUES ({empresa.Id}, {loja.Id}, 99999998)""");

        var ultimoDe8 = Lote.Criar(empresa.Id, Codigo(), lojaId: loja.Id);
        db.Lotes.Add(ultimoDe8);
        await db.SaveChangesAsync();
        var primeiroDe10 = Lote.Criar(empresa.Id, Codigo(), lojaId: loja.Id);
        db.Lotes.Add(primeiroDe10);
        await db.SaveChangesAsync();

        ultimoDe8.NumeroCurto.Should().Be("99999999");
        primeiroDe10.NumeroCurto.Should().Be("0100000000");
    }

    [SkippableFact]
    public async Task Lote_antigo_ganha_numero_sob_demanda_e_pedir_de_novo_devolve_o_mesmo()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        var (empresa, loja, _) = await SemearAsync();
        var antigo = await LoteAntigoAsync(empresa.Id, loja.Id);

        await using var db = Contexto(empresa.Id);
        var repo = new LoteNumeroCurtoRepository(db);
        var primeiro = await repo.GarantirAsync(empresa.Id, antigo);
        var segundo = await repo.GarantirAsync(empresa.Id, antigo);

        primeiro.Should().Be("00000002", "o lote criado antes ocupou o 1 e o número antigo foi apagado para simular lote velho");
        segundo.Should().Be(primeiro);
        var novo = Lote.Criar(empresa.Id, Codigo(), lojaId: loja.Id);
        db.Lotes.Add(novo);
        await db.SaveChangesAsync();
        novo.NumeroCurto.Should().Be("00000003", "repetir o pedido não gasta número");
    }

    [SkippableFact]
    public async Task Pedidos_simultaneos_do_mesmo_lote_antigo_recebem_o_mesmo_numero()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        var (empresa, loja, _) = await SemearAsync();
        var antigo = await LoteAntigoAsync(empresa.Id, loja.Id);

        var numeros = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var db = Contexto(empresa.Id);
            return await new LoteNumeroCurtoRepository(db).GarantirAsync(empresa.Id, antigo);
        }));

        numeros.Should().OnlyContain(n => n == numeros[0]).And.NotContainNulls();
    }

    [SkippableFact]
    public async Task Lotes_criados_ao_mesmo_tempo_na_mesma_loja_nao_repetem_numero()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        var (empresa, loja, _) = await SemearAsync();

        var numeros = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = Contexto(empresa.Id);
            var lote = Lote.Criar(empresa.Id, Codigo(), lojaId: loja.Id);
            db.Lotes.Add(lote);
            await db.SaveChangesAsync();
            return lote.NumeroCurto;
        }));

        numeros.Should().BeEquivalentTo(Enumerable.Range(1, 8).Select(n => n.ToString("D8")));
    }

    [SkippableFact]
    public async Task Lote_de_outra_empresa_nao_ganha_numero()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        var (empresa, loja, _) = await SemearAsync();
        var (outra, _, _) = await SemearAsync();
        var antigo = await LoteAntigoAsync(empresa.Id, loja.Id);

        await using (var db = Contexto(outra.Id))
            (await new LoteNumeroCurtoRepository(db).GarantirAsync(outra.Id, antigo)).Should().BeNull();

        await using var conferencia = Contexto(empresa.Id);
        (await conferencia.Lotes.AsNoTracking().SingleAsync(l => l.Id == antigo)).NumeroCurto.Should().BeNull();
    }

    [SkippableFact]
    public async Task Sob_RLS_o_gatilho_numera_e_o_contador_de_outra_empresa_fica_invisivel()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason);
        var (empresa, loja, _) = await SemearAsync();
        var (outra, lojaOutra, _) = await SemearAsync();
        await using (var db = Contexto(outra.Id))
        {
            db.Lotes.Add(Lote.Criar(outra.Id, Codigo(), lojaId: lojaOutra.Id));
            await db.SaveChangesAsync();
        }

        await using var rls = fixture.CreateRlsClientDbContext();
        await rls.Database.OpenConnectionAsync();
        await rls.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {empresa.Id.ToString()}, false)");
        rls.SetMobileTenantContext(empresa.Id);
        var lote = Lote.Criar(empresa.Id, Codigo(), lojaId: loja.Id);
        rls.Lotes.Add(lote);
        await rls.SaveChangesAsync();

        lote.NumeroCurto.Should().Be("00000001");
        var contadores = await rls.LoteNumeroContadores.IgnoreQueryFilters().Select(c => c.EmpresaId).ToListAsync();
        contadores.Should().OnlyContain(id => id == empresa.Id).And.NotBeEmpty();
    }

    private async Task<(Empresa Empresa, Loja A, Loja B)> SemearAsync()
    {
        var empresa = Empresa.Criar($"Empresa {Guid.NewGuid():N}", null);
        var a = Loja.Criar(empresa.Id, "Loja A");
        var b = Loja.Criar(empresa.Id, "Loja B");
        await using var db = fixture.CreateDbContext();
        db.Empresas.Add(empresa);
        db.Lojas.AddRange(a, b);
        await db.SaveChangesAsync();
        return (empresa, a, b);
    }

    /// <summary>Lote anterior à migração: criado, e o número apagado como se nunca tivesse existido.</summary>
    private async Task<Guid> LoteAntigoAsync(Guid empresaId, Guid lojaId)
    {
        await using var db = Contexto(empresaId);
        var lote = Lote.Criar(empresaId, Codigo(), lojaId: lojaId);
        db.Lotes.Add(lote);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE lotes SET "NumeroCurto" = NULL WHERE "Id" = {lote.Id}""");
        return lote.Id;
    }

    private EasyStockDbContext Contexto(Guid empresaId)
    {
        var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        return db;
    }

    private static string Codigo() => $"LOT-{Guid.NewGuid():N}"[..20];
}
