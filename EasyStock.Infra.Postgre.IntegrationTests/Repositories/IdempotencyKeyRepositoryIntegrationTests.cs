using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// issue 917 Fase B: prova contra Postgres real que o INSERT-first fecha o TOCTOU que o
/// <c>GetActiveAsync</c>-antes-do-<c>SaveAsync</c> original deixava aberto. O cenario do QA
/// (5 POSTs paralelos, mesma chave, servidor real) reduz, no nivel do repositorio, a: so UM
/// <see cref="IdempotencyKeyRepository.TryBeginAsync"/> reserva — os outros recebem a linha
/// existente via <c>unique_violation</c> em <c>ux_idempotency_key_empresa_recurso</c>, nao
/// duas transacoes vencendo a mesma corrida (o que o fake/NSubstitute do teste unitario da
/// middleware nao prova — so o SqlState real prova).
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class IdempotencyKeyRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task Cinco_TryBeginAsync_concorrentes_mesma_chave_so_uma_reserva()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => TryBeginAsync(empresaId, "chave-concorrente", "POST /api/estoque/saida"));
        var resultados = await Task.WhenAll(tasks);

        resultados.Count(r => r.Reservado).Should().Be(1,
            "so UM concorrente pode vencer o INSERT no indice unico — os outros 4 tem que receber a linha existente, nao lancar nem reservar de novo");
        resultados.Select(r => r.Entry.Id).Distinct().Should().ContainSingle(
            "todos os 5 resultados apontam para a MESMA linha (a do vencedor da corrida)");
    }

    [SkippableFact]
    public async Task TryBeginAsync_apos_MarcarConcluido_retorna_a_linha_concluida_para_replay()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var (primeira, reservadaAntes) = await TryBeginAsync(empresaId, "chave-replay", "POST /api/vendas");
        reservadaAntes.Should().BeTrue();

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            await new IdempotencyKeyRepository(db).MarcarConcluidoAsync(primeira.Id, 201, "{\"ok\":true}");
        }

        var (segunda, reservadaDepois) = await TryBeginAsync(empresaId, "chave-replay", "POST /api/vendas");

        reservadaDepois.Should().BeFalse();
        segunda.Id.Should().Be(primeira.Id);
        segunda.Status.Should().Be(StatusIdempotencyKey.Concluido);
        segunda.HttpStatus.Should().Be(201);
        segunda.RespostaJson.Should().Be("{\"ok\":true}");
    }

    [SkippableFact]
    public async Task TryTakeoverAsync_falha_quando_LockedAtUtc_ja_mudou_desde_o_SELECT()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var (entry, _) = await TryBeginAsync(empresaId, "chave-lease", "POST /api/vendas");

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        var repo = new IdempotencyKeyRepository(db);

        // "esperado" desatualizado -- simula outro processo que ja tomou o lease entre o
        // SELECT (fora desta chamada) e este CAS.
        var tomou = await repo.TryTakeoverAsync(entry.Id, entry.LockedAtUtc.AddSeconds(-1), DateTime.UtcNow);

        tomou.Should().BeFalse("o CAS so pode ter sucesso se LockedAtUtc ainda estiver EXATAMENTE no valor esperado");
    }

    [SkippableFact]
    public async Task TryTakeoverAsync_com_LockedAtUtc_correto_tem_sucesso_e_persiste_o_novo_lock()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var (entry, _) = await TryBeginAsync(empresaId, "chave-lease-ok", "POST /api/vendas");
        var novoLock = DateTime.UtcNow.AddMinutes(5);

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        var repo = new IdempotencyKeyRepository(db);

        (await repo.TryTakeoverAsync(entry.Id, entry.LockedAtUtc, novoLock)).Should().BeTrue();

        var recarregada = await db.IdempotencyKeys.AsNoTracking().SingleAsync(x => x.Id == entry.Id);
        recarregada.Status.Should().Be(StatusIdempotencyKey.Pendente, "takeover so muda o lock, nao o status");
        recarregada.LockedAtUtc.Should().BeCloseTo(novoLock, TimeSpan.FromSeconds(1));
    }

    [SkippableFact]
    public async Task TryReabrirAsync_so_funciona_a_partir_de_Falhou()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var (entry, _) = await TryBeginAsync(empresaId, "chave-falhou", "POST /api/vendas");

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        var repo = new IdempotencyKeyRepository(db);

        // Ainda Pendente -- reabertura nao pode funcionar (so Falhou reabre).
        (await repo.TryReabrirAsync(entry.Id, null, DateTime.UtcNow)).Should().BeFalse();

        await repo.MarcarFalhouAsync(entry.Id);
        (await repo.TryReabrirAsync(entry.Id, "novo-hash", DateTime.UtcNow)).Should().BeTrue();

        var recarregada = await db.IdempotencyKeys.AsNoTracking().SingleAsync(x => x.Id == entry.Id);
        recarregada.Status.Should().Be(StatusIdempotencyKey.Pendente);
        recarregada.PayloadHash.Should().Be("novo-hash");
    }

    [SkippableFact]
    public async Task CleanupExpiredAsync_remove_so_linhas_expiradas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            db.IdempotencyKeys.Add(IdempotencyKey.CriarPendente("expirada", empresaId, "POST /x", null, TimeSpan.FromSeconds(-1)));
            db.IdempotencyKeys.Add(IdempotencyKey.CriarPendente("valida", empresaId, "POST /y", null, TimeSpan.FromHours(1)));
            await db.SaveChangesAsync();
        }

        await using var assertDb = fixture.CreateDbContext();
        assertDb.SetMobileTenantContext(empresaId);
        var repo = new IdempotencyKeyRepository(assertDb);

        var removidas = await repo.CleanupExpiredAsync(DateTime.UtcNow);

        removidas.Should().Be(1);
        var restantes = await assertDb.IdempotencyKeys.AsNoTracking().ToListAsync();
        restantes.Should().ContainSingle(x => x.Key == "valida");
    }

    private async Task<(IdempotencyKey Entry, bool Reservado)> TryBeginAsync(Guid empresaId, string key, string metodoRecurso)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        var repo = new IdempotencyKeyRepository(db);
        return await repo.TryBeginAsync(key, empresaId, metodoRecurso, null, TimeSpan.FromHours(1));
    }
}
