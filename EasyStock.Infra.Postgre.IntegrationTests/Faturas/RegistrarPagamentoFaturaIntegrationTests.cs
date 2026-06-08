using EasyStock.Application.UseCases.Faturas.CancelarFatura;
using EasyStock.Application.UseCases.Faturas.RegistrarPagamentoFatura;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Faturas;

/// <summary>
/// Regressao do BUG-01 (issue #512): registrar pagamento / cancelar falhavam com
/// <see cref="DbUpdateConcurrencyException"/> porque os use cases chamavam
/// <c>repo.UpdateAsync</c> (<c>db.Faturas.Update</c>) numa fatura JA rastreada,
/// rebaixando os filhos novos (FaturaPagamento/FaturaEvento) a Modified.
/// Estes testes exercitam os use cases REAIS contra Postgres real — o que o teste
/// mockado do EfiPix nunca fez. Sem a fix, falham com concorrencia falsa.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class RegistrarPagamentoFaturaIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task RegistrarPagamento_total_persiste_pagamento_e_marca_paga()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");

        var empresaId = Guid.NewGuid();
        var faturaId = await SeedFaturaEmitidaAsync(empresaId);

        // ACT — use case real num scope novo (recarrega a fatura rastreada, como em producao)
        await using (var act = fixture.CreateDbContext())
        {
            var uc = new RegistrarPagamentoFaturaUseCase(
                new FaturaRepository(act), act, NullLogger<RegistrarPagamentoFaturaUseCase>.Instance);
            var registrar = async () => await uc.ExecuteAsync(new RegistrarPagamentoFaturaCommand(
                EmpresaId: empresaId, FaturaId: faturaId, Metodo: "dinheiro", Valor: 100m,
                StatusInicial: StatusFaturaPagamento.Confirmado, OrigemRegistro: "test"));
            await registrar.Should().NotThrowAsync(
                "fatura rastreada + filho novo deve INSERIR, nao falhar por concorrencia falsa");
        }

        // ASSERT — semantica de Fatura
        await using var assert = fixture.CreateDbContext();
        var fatura = await assert.Faturas.AsNoTracking().IgnoreQueryFilters()
            .Include(f => f.Pagamentos).Include(f => f.Eventos)
            .FirstAsync(f => f.Id == faturaId);

        fatura.Status.Should().Be(StatusFatura.Paga);
        fatura.Pagamentos.Should().HaveCount(1);
        var pagamento = fatura.Pagamentos.Single();
        pagamento.Status.Should().Be(StatusFaturaPagamento.Confirmado);
        pagamento.Valor.Should().Be(100m);
        fatura.Eventos.Should().Contain(e => e.Tipo == TipoEventoFatura.PagamentoConfirmado);
        fatura.TotalPago.Should().Be(100m);
    }

    [SkippableFact]
    public async Task CancelarFatura_persiste_status_e_evento()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");

        var empresaId = Guid.NewGuid();
        var faturaId = await SeedFaturaEmitidaAsync(empresaId);

        await using (var act = fixture.CreateDbContext())
        {
            var uc = new CancelarFaturaUseCase(
                new FaturaRepository(act), act, NullLogger<CancelarFaturaUseCase>.Instance);
            var cancelar = async () => await uc.ExecuteAsync(new CancelarFaturaCommand(
                EmpresaId: empresaId, FaturaId: faturaId, Motivo: "teste", OrigemRegistro: "test"));
            await cancelar.Should().NotThrowAsync();
        }

        await using var assert = fixture.CreateDbContext();
        var fatura = await assert.Faturas.AsNoTracking().IgnoreQueryFilters()
            .Include(f => f.Eventos).FirstAsync(f => f.Id == faturaId);
        fatura.Status.Should().Be(StatusFatura.Cancelada);
        fatura.Eventos.Should().Contain(e => e.Tipo == TipoEventoFatura.Cancelada);
    }

    private async Task<Guid> SeedFaturaEmitidaAsync(Guid empresaId)
    {
        await using var seed = fixture.CreateDbContext();
        seed.SetMobileTenantContext(empresaId);
        seed.Empresas.Add(FaturaTestSeed.Empresa(empresaId));
        var fatura = FaturaTestSeed.FaturaEmitida(empresaId, valor: 100m);
        seed.Faturas.Add(fatura);
        await seed.SaveChangesAsync();
        return fatura.Id;
    }
}
