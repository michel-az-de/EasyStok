using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Pagamentos;

/// <summary>
/// #2501: conciliação Pix de Contas a Receber contra Postgres real.
/// (a) Pix estornado não pode voltar a ser reconciliado (índice único do txid).
/// (b) Parcela vencida com Pix ativo entra no job de reserva.
/// </summary>
public class PixReconciliacaoEstornoVencidaTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task Reconciliar_de_novo_um_Pix_estornado_nao_lanca_nem_grava_pagamento()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var txid = $"cr{Guid.NewGuid():N}";
        var (empresaId, parcelaId) = await SeedAsync(txid, DateTime.UtcNow.Date.AddDays(5));

        await using (var db = fixture.CreateDbContext())
        {
            var r = await CriarReconciliar(db).ExecuteAsync(new ReconciliarPixParcelaReceberCommand(txid, ValorPagoEfi: 100m));
            r.Reconciliado.Should().BeTrue();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            var pagamentoId = await db.Set<PagamentoParcela>().IgnoreQueryFilters()
                .Where(p => p.GatewayTransactionId == txid).Select(p => p.Id).SingleAsync();
            var estornar = new EstornarPagamentoParcelaReceberUseCase(
                new ContaReceberRepository(db), new CaixaRepository(db), db,
                NullLogger<EstornarPagamentoParcelaReceberUseCase>.Instance);
            (await estornar.ExecuteAsync(new EstornarPagamentoParcelaReceberCommand(
                empresaId, parcelaId, pagamentoId, "cliente pediu"))).Should().BeTrue();
        }

        // O job (ou um webhook reenviado) consulta o mesmo txid de novo.
        await using (var db = fixture.CreateDbContext())
        {
            var act = async () => await CriarReconciliar(db)
                .ExecuteAsync(new ReconciliarPixParcelaReceberCommand(txid, ValorPagoEfi: 100m));
            var r = (await act.Should().NotThrowAsync()).Subject;
            r.Reconciliado.Should().BeFalse();
        }

        await using var assert = fixture.CreateDbContext();
        var pagamentos = await assert.Set<PagamentoParcela>().IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.GatewayTransactionId == txid).ToListAsync();
        pagamentos.Should().ContainSingle().Which.Status.Should().Be(StatusPagamentoParcela.Estornado);
    }

    [SkippableFact]
    public async Task Parcela_vencida_com_Pix_ativo_entra_na_lista_do_job()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var txid = $"cr{Guid.NewGuid():N}";
        var (empresaId, parcelaId) = await SeedAsync(txid, DateTime.UtcNow.Date.AddDays(-3));

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            var parcela = await db.ParcelasReceber.SingleAsync(p => p.Id == parcelaId);
            parcela.MarcarVencidaSeAplicavel(DateTime.UtcNow);
            parcela.Status.Should().Be(StatusParcela.Vencida);
            await db.SaveChangesAsync();
        }

        await using var leitura = fixture.CreateDbContext();
        var lista = await new ContaReceberRepository(leitura).ListarParcelasComPixAtivoAsync(DateTime.UtcNow);
        lista.Select(p => p.Id).Should().Contain(parcelaId);
    }

    private static ReconciliarPixParcelaReceberUseCase CriarReconciliar(EasyStockDbContext db) =>
        new(new ContaReceberRepository(db), new CaixaRepository(db), Substitute.For<IEfiPixService>(), db,
            NullLogger<ReconciliarPixParcelaReceberUseCase>.Instance);

    private async Task<(Guid EmpresaId, Guid ParcelaId)> SeedAsync(string txid, DateTime vencimento)
    {
        var empresaId = Guid.NewGuid();
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(new Empresa
        {
            Id = empresaId, Nome = "Emp", Documento = empresaId.ToString("N")[..14],
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow,
        });
        var categoria = CategoriaFinanceira.Criar(empresaId, "Rec", TipoCategoriaFinanceira.Receita);
        db.CategoriasFinanceiras.Add(categoria);
        var conta = ContaReceber.Criar(empresaId, null, categoria.Id, "Encomenda Pix", DateTime.UtcNow.Date.AddDays(-10));
        conta.AdicionarParcela(1, 100m, vencimento);
        conta.Emitir();
        var parcela = conta.Parcelas.Single();
        parcela.AssociarPix(txid, "copia-cola", "qr", DateTime.UtcNow.AddDays(1));
        db.ContasReceber.Add(conta);
        await db.SaveChangesAsync();
        return (empresaId, parcela.Id);
    }
}
