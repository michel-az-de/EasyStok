using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Application.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Mobile;

/// <summary>
/// #2765: a exclusão de lançamento no aparelho estorna o movimento pelo caso de uso do Caixa, que toma a trava do
/// caixa, a mesma do fechamento (#2613), e só depois lê a guarda de dia fechado (#2736). Os testes do aparelho com
/// InMemory dispensam a trava (<c>DispatcherDeTeste</c>); aqui ela roda no Postgres real, pelo /api/mobile/sync.
/// Sem a trava, o estorno que passou pela guarda antes do retrato era gravado depois dele, num dia já fechado.
/// </summary>
[Collection("MobileE2E")]
public sealed class SyncEstornoTravaE2ETests(MobileE2EFixture fixture)
{
    private const string Recusa = "Não é possível estornar movimento de dia já fechado.";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)] // #2629: o fechamento da empresa (loja nula) retrata também o lançamento da loja.
    public async Task ExclusaoNoAparelho_DuranteOFechamento_EsperaATravaERecusaODiaFechado(bool fechamentoDaLoja)
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, lojaId) = await fixture.SeedEmpresaELojaAsync();
        var credencial = await fixture.SeedMobileDeviceAsync(empresaId, lojaId);
        using var aparelho = fixture.CreateMobileClient(credencial.ApiKey);
        var (lancamento, movimentoId, dia) = await LancarNoAparelhoAsync(aparelho, credencial.DeviceId);
        Guid? lojaDoFechamento = fechamentoDaLoja ? lojaId : null;

        // Outra conexão faz o papel do fechamento em curso: segura a trava do caixa e grava o retrato do dia.
        await using var fechamento = NovoContexto();
        await using var tx = await fechamento.Database.BeginTransactionAsync();
        await TravarAsync(fechamento, empresaId, lojaDoFechamento);

        var exclusao = Mutacao(credencial.DeviceId, "cashEntry.delete", new { id = lancamento });
        var envio = Task.Run(() => aparelho.PostAsJsonAsync("/api/mobile/sync",
            new SyncPushRequest(credencial.DeviceId, [exclusao], "Thati"), Json));
        var esperouATrava = await AlguemEsperaATravaAsync(envio, empresaId, lojaDoFechamento);

        fechamento.FechamentosCaixa.Add(FechamentoCaixa.Criar(empresaId, dia, 100m, 0m, 0m, 0m, 0m, lojaDoFechamento));
        await fechamento.SaveChangesAsync();
        await tx.CommitAsync();

        using var resposta = await envio;
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var resultado = (await resposta.Content.ReadFromJsonAsync<SyncPushResponse>(Json))!;
        await using var conferir = NovoContexto();
        (await conferir.Set<MovimentoCaixa>().IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Id == movimentoId))
            .EstornadoEm.Should().BeNull("o estorno do aparelho não pode entrar num dia que o fechamento já retratou");
        (await conferir.Set<CashEntry>().IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == lancamento))
            .DeletedAt.Should().BeNull("a exclusão recusada não some com o lançamento do aparelho");
        resultado.Rejected.Should().ContainSingle(r => r.MutationId == exclusao.Id,
                "a recusa vem da guarda lida atrás da trava, não da conferência do dispatcher antes dela")
            .Which.Reason.Should().Be(Recusa);
        resultado.AcceptedIds.Should().NotContain(exclusao.Id);
        esperouATrava.Should().BeTrue("o estorno do aparelho espera quem segura o caixa, como o lançamento manual");
    }

    [SkippableFact]
    public async Task ExclusaoNoAparelho_ComOCaixaAberto_EstornaComATravaNoPostgres()
    {
        // Controle: a trava exige transação real; sem fechamento em curso, a exclusão estorna normalmente.
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, lojaId) = await fixture.SeedEmpresaELojaAsync();
        var credencial = await fixture.SeedMobileDeviceAsync(empresaId, lojaId);
        using var aparelho = fixture.CreateMobileClient(credencial.ApiKey);
        var (lancamento, movimentoId, _) = await LancarNoAparelhoAsync(aparelho, credencial.DeviceId);

        var exclusao = Mutacao(credencial.DeviceId, "cashEntry.delete", new { id = lancamento });
        using var resposta = await aparelho.PostAsJsonAsync("/api/mobile/sync",
            new SyncPushRequest(credencial.DeviceId, [exclusao], "Thati"), Json);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var resultado = (await resposta.Content.ReadFromJsonAsync<SyncPushResponse>(Json))!;
        resultado.Rejected.Should().BeNullOrEmpty();
        resultado.AcceptedIds.Should().Contain(exclusao.Id);
        await using var conferir = NovoContexto();
        (await conferir.Set<MovimentoCaixa>().IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Id == movimentoId))
            .EstornadoEm.Should().NotBeNull();
        (await conferir.Set<CashEntry>().IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == lancamento))
            .DeletedAt.Should().NotBeNull();
    }

    // ---------- apoio ----------

    /// <summary>O aparelho lança a saída; o vínculo cria o MovimentoCaixa "mobile:&lt;id&gt;" dentro do push.</summary>
    private async Task<(string Lancamento, Guid MovimentoId, DateOnly Dia)> LancarNoAparelhoAsync(HttpClient aparelho, string deviceId)
    {
        var lancamento = "ce-" + Guid.NewGuid().ToString("N")[..20];
        var envio = Mutacao(deviceId, "cashEntry.upsert", new
        {
            id = lancamento, type = "expense", amount = 15m, description = "Gás", metodo = "pix", createdAt = Ms(DateTime.UtcNow),
        });
        using var resposta = await aparelho.PostAsJsonAsync("/api/mobile/sync", new SyncPushRequest(deviceId, [envio], "Thati"), Json);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = NovoContexto();
        var movimento = await db.Set<MovimentoCaixa>().IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(m => m.Referencia == "mobile:" + lancamento);
        // O dia do retrato sai do próprio movimento: a prova não depende da hora em que roda (#2770).
        return (lancamento, movimento.Id, HorarioBrasil.DataOperacional(movimento.DataMovimento));
    }

    private static MutationDto Mutacao(string deviceId, string tipo, object payload) =>
        new("mut_" + Guid.NewGuid(), deviceId, tipo, JsonSerializer.SerializeToElement(payload, Json), Ms(DateTime.UtcNow));

    /// <summary>Contexto próprio, sem a estratégia de retry da API, que recusa transação aberta pelo chamador.</summary>
    private EasyStockDbContext NovoContexto()
    {
        var conexao = fixture.Factory!.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        var db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>().UseNpgsql(conexao)
            .AddInterceptors(new EasyStock.Infra.Postgre.Data.Interceptors.SetTenantOnConnectionInterceptor()).Options);
        db.UseRowLevelSecurityBypass(); // a API roda sem BYPASSRLS (#2180); o teste confere entre empresas
        return db;
    }

    private static Task TravarAsync(EasyStockDbContext db, Guid empresaId, Guid? lojaId)
    {
        var chave = $"caixa-saida:{empresaId:N}:{lojaId:N}";
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({chave}))");
    }

    // Determinístico, sem cronômetro: o envio terminou sem esperar (sem trava) ou outra conexão ficou na fila da
    // trava deste caixa. pg_locks guarda a chave de 64 bits partida em classid (alta) e objid (baixa).
    private async Task<bool> AlguemEsperaATravaAsync(Task envio, Guid empresaId, Guid? lojaId)
    {
        var chave = $"caixa-saida:{empresaId:N}:{lojaId:N}";
        await using var db = NovoContexto();
        var limite = DateTime.UtcNow.AddSeconds(30);
        while (!envio.IsCompleted && DateTime.UtcNow < limite)
        {
            var esperando = await db.Database.SqlQuery<int>($@"SELECT count(*)::int AS ""Value"" FROM pg_locks
                WHERE locktype = 'advisory' AND NOT granted
                  AND objid::bigint = (hashtext({chave})::bigint & 4294967295)").SingleAsync();
            if (esperando > 0) return true;
            await Task.Delay(50);
        }
        return false;
    }

    private static long Ms(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
}
