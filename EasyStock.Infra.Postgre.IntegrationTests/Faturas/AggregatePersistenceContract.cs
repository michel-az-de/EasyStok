using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Faturas;

/// <summary>
/// Contrato de persistencia para agregados com colecao de filhos. Trava as 2
/// invariantes que o BUG-01 (issue #512) violou ao chamar <c>db.Set.Update()</c>
/// numa raiz JA rastreada:
/// <list type="number">
///   <item>adicionar um filho novo a uma raiz rastreada + commit grava como INSERT
///   (nao um UPDATE em linha inexistente que viraria DbUpdateConcurrencyException);</item>
///   <item>a concorrencia otimista (xmin na raiz) continua disparando.</item>
/// </list>
/// Qualquer agregado novo com colecao de filhos deve herdar e implementar os hooks.
/// Fatura e a 1a implementacao (<see cref="FaturaAggregatePersistenceTests"/>).
/// </summary>
public abstract class AggregatePersistenceContract<TAggregate>(PostgreSqlDatabaseFixture fixture)
    where TAggregate : class
{
    protected PostgreSqlDatabaseFixture Fixture => fixture;

    /// <summary>Cria e persiste o agregado num contexto proprio; retorna o Id.</summary>
    protected abstract Task<Guid> SeedAsync(EasyStockDbContext db);

    /// <summary>Carrega o agregado RASTREADO, com a colecao de filhos incluida.</summary>
    protected abstract Task<TAggregate?> LoadTrackedAsync(EasyStockDbContext db, Guid id);

    /// <summary>Adiciona um filho novo (PK ja preenchida) a colecao do agregado.</summary>
    protected abstract void AddChild(TAggregate aggregate);

    /// <summary>Conta os filhos persistidos do tipo que <see cref="AddChild"/> adiciona.</summary>
    protected abstract Task<int> CountChildrenAsync(EasyStockDbContext db, Guid id);

    /// <summary>Muda um escalar da raiz, para exercitar o concurrency token.</summary>
    protected abstract void MutateRoot(TAggregate aggregate);

    [SkippableFact]
    public async Task Adicionar_filho_a_raiz_rastreada_e_commit_grava_como_INSERT()
    {
        Skip.If(!Fixture.IsAvailable, Fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");

        Guid id;
        await using (var seed = Fixture.CreateDbContext())
            id = await SeedAsync(seed);

        int antes;
        await using (var count = Fixture.CreateDbContext())
            antes = await CountChildrenAsync(count, id);

        await using (var act = Fixture.CreateDbContext())
        {
            var agg = await LoadTrackedAsync(act, id);
            agg.Should().NotBeNull("o agregado seedado deve ser carregado rastreado");
            AddChild(agg!);
            var commit = async () => await act.SaveChangesAsync();
            await commit.Should().NotThrowAsync(
                "filho novo em raiz rastreada deve gerar INSERT, nao DbUpdateConcurrencyException");
        }

        await using var assert = Fixture.CreateDbContext();
        (await CountChildrenAsync(assert, id)).Should().Be(antes + 1,
            "o filho novo deve ter sido inserido, nao perdido por um UPDATE em linha inexistente");
    }

    [SkippableFact]
    public async Task Concorrencia_otimista_na_raiz_dispara_DbUpdateConcurrencyException()
    {
        Skip.If(!Fixture.IsAvailable, Fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");

        Guid id;
        await using (var seed = Fixture.CreateDbContext())
            id = await SeedAsync(seed);

        await using var ctxA = Fixture.CreateDbContext();
        await using var ctxB = Fixture.CreateDbContext();
        var a = await LoadTrackedAsync(ctxA, id);
        var b = await LoadTrackedAsync(ctxB, id);
        a.Should().NotBeNull();
        b.Should().NotBeNull();

        MutateRoot(a!);
        await ctxA.SaveChangesAsync();

        MutateRoot(b!);
        var stale = async () => await ctxB.SaveChangesAsync();
        await stale.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "a 2a escrita sobre xmin defasado deve falhar — prova que a protecao otimista segue ativa");
    }
}
