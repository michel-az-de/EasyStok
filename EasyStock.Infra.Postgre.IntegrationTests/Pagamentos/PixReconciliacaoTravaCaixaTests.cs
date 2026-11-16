using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using EasyStock.Infra.Postgre.IntegrationTests.Workflows;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Pagamentos;

/// <summary>
/// #2763: o Pix de parcela reconciliado pelo webhook ou pelo job entra no Caixa depois da trava que o fechamento também
/// toma. Sem ela, a entrada gravada durante o retrato podia ficar com data anterior ao fechamento e fora dele.
/// Roda como <c>rls_test_client</c>, com bypass de RLS ligado antes da primeira conexão, como o webhook.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class PixReconciliacaoTravaCaixaTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pix_pago_espera_o_fechamento_e_entra_depois_do_retrato(bool comLoja)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, lojaId, parcelaId, txid) = await SemearAsync(comLoja);

        // Outra conexão faz o papel do fechamento em curso.
        await using var segura = fixture.CreateDbContext();
        segura.SetMobileTenantContext(empresaId);
        await using var tx = await segura.Database.BeginTransactionAsync();
        await TravaDoCaixaTeste.TomarComoOFechamentoAsync(segura, empresaId, lojaId);

        var reconciliar = Task.Run(async () =>
        {
            await using var db = CriarContextoDoWebhook();
            using var _ = db.UseRowLevelSecurityBypass();
            var uc = new ReconciliarPixParcelaReceberUseCase(new ContaReceberRepository(db), new CaixaRepository(db),
                Substitute.For<IEfiPixService>(), db, NullLogger<ReconciliarPixParcelaReceberUseCase>.Instance);
            return await uc.ExecuteAsync(new ReconciliarPixParcelaReceberCommand(txid, ValorPagoEfi: 100m));
        });
        var esperouATrava = await TravaDoCaixaTeste.EsperamATravaAsync(fixture, empresaId, 1, reconciliar);

        var fechamento = FechamentoCaixa.Criar(empresaId, HorarioBrasil.Hoje(), 100m, 0m, 0m, 0m, 0m, lojaId);
        segura.FechamentosCaixa.Add(fechamento);
        await segura.SaveChangesAsync();
        await tx.CommitAsync();
        var resultado = await reconciliar;

        esperouATrava.Should().BeTrue("o Pix só grava depois de quem segura o caixa");
        resultado.Reconciliado.Should().BeTrue(resultado.Motivo);
        await using var conferir = fixture.CreateDbContext();
        var entrada = await conferir.MovimentosCaixa.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(m => m.EmpresaId == empresaId && m.Tipo == "entrada");
        entrada.DataMovimento.Should().BeAfter(fechamento.FechadoEm, "a entrada fica fora do dia já retratado");
        (await conferir.ParcelasReceber.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == parcelaId))
            .Status.Should().Be(StatusParcela.Paga);
    }

    private EasyStockDbContext CriarContextoDoWebhook()
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.RlsClientConnectionString)
            .AddInterceptors(new SetTenantOnConnectionInterceptor())
            .Options;
        return new EasyStockDbContext(options, Substitute.For<ICurrentUserAccessor>());
    }

    private async Task<(Guid EmpresaId, Guid? LojaId, Guid ParcelaId, string Txid)> SemearAsync(bool comLoja)
    {
        var empresaId = Guid.NewGuid();
        var txid = $"cr{Guid.NewGuid():N}";
        await using var db = CriarContextoDoWebhook();
        using var _ = db.UseRowLevelSecurityBypass();
        db.Empresas.Add(new Empresa
        {
            Id = empresaId, Nome = "Emp Pix trava", Documento = empresaId.ToString("N")[..14],
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow,
        });
        Guid? lojaId = null;
        if (comLoja)
        {
            var loja = Loja.Criar(empresaId, "Loja Pix trava");
            db.Lojas.Add(loja);
            lojaId = loja.Id;
        }
        db.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "abertura", 100m, DateTime.UtcNow.AddMinutes(-5), lojaId));
        var categoria = CategoriaFinanceira.Criar(empresaId, "Rec", TipoCategoriaFinanceira.Receita);
        db.CategoriasFinanceiras.Add(categoria);
        var conta = ContaReceber.Criar(empresaId, null, categoria.Id, "Encomenda Pix", DateTime.UtcNow.Date.AddDays(-10), lojaId: lojaId);
        conta.AdicionarParcela(1, 100m, DateTime.UtcNow.Date.AddDays(5));
        conta.Emitir();
        var parcela = conta.Parcelas.Single();
        parcela.AssociarPix(txid, "copia-cola", "qr", DateTime.UtcNow.AddDays(1));
        db.ContasReceber.Add(conta);
        await db.SaveChangesAsync();
        return (empresaId, lojaId, parcela.Id, txid);
    }
}
