using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

/// <summary>
/// #2763: duas baixas da mesma parcela ao mesmo tempo. A segunda lê a parcela ainda em aberto, antes da trava do
/// Caixa, e só grava depois do commit da primeira. O token de concorrência da parcela (xmin) tem de barrar a segunda:
/// um só pagamento, um só movimento no Caixa e um só evento.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class BaixasSimultaneasParcelaIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableTheory]
    [InlineData(TipoLadoFinanceiro.Pagar)]
    [InlineData(TipoLadoFinanceiro.Receber)]
    public async Task Segunda_baixa_simultanea_da_mesma_parcela_nao_duplica(TipoLadoFinanceiro lado)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, contaId, parcelaId) = await SemearAsync(lado);
        await using var provider = BuildProductionProvider();

        var vencedorGravou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liberarCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Baixar(bool vencedor)
        {
            await using var scope = provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
            Task Executar() => lado == TipoLadoFinanceiro.Pagar
                ? scope.ServiceProvider.GetRequiredService<RegistrarPagamentoParcelaPagarUseCase>()
                    .ExecuteAsync(new RegistrarPagamentoParcelaPagarCommand(empresaId, parcelaId, 100m, "pix"))
                : scope.ServiceProvider.GetRequiredService<RegistrarPagamentoParcelaReceberUseCase>()
                    .ExecuteAsync(new RegistrarPagamentoParcelaReceberCommand(empresaId, parcelaId, 100m, "pix"));
            if (!vencedor)
            {
                await Executar();
                return;
            }
            // O vencedor grava dentro de uma transação externa e segura o commit (e a trava) até o concorrente esperar.
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionSemRetryAsync(async _ =>
            {
                await Executar();
                vencedorGravou.SetResult();
                await liberarCommit.Task.WaitAsync(TimeSpan.FromSeconds(30));
                return true;
            });
        }

        var vencedor = Baixar(vencedor: true);
        await vencedorGravou.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var concorrente = Task.Run(() => Baixar(vencedor: false));
        bool concorrenteNaFila;
        try { concorrenteNaFila = await TravaDoCaixaTeste.EsperamATravaAsync(fixture, empresaId, 1, concorrente); }
        finally { liberarCommit.TrySetResult(); }

        concorrenteNaFila.Should().BeTrue("a segunda baixa leu a parcela em aberto e esperou a trava do Caixa");
        await vencedor;
        await FluentActions.Invoking(() => concorrente).Should().ThrowAsync<DbUpdateConcurrencyException>(
            "a parcela mudou desde a leitura e o token de concorrência barra a gravação");

        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(empresaId);
        var pagamentos = await conferir.PagamentosParcela.AsNoTracking()
            .Where(p => lado == TipoLadoFinanceiro.Pagar ? p.ParcelaPagarId == parcelaId : p.ParcelaReceberId == parcelaId)
            .ToListAsync();
        pagamentos.Should().ContainSingle(p => p.Status == StatusPagamentoParcela.Confirmado)
            .Which.Valor.Should().Be(100m);
        pagamentos.Should().HaveCount(1);
        if (lado == TipoLadoFinanceiro.Pagar)
        {
            var parcela = await conferir.ParcelasPagar.AsNoTracking().SingleAsync(p => p.Id == parcelaId);
            (parcela.ValorPago, parcela.Status).Should().Be((100m, StatusParcela.Paga));
        }
        else
        {
            var parcela = await conferir.ParcelasReceber.AsNoTracking().SingleAsync(p => p.Id == parcelaId);
            (parcela.ValorPago, parcela.Status).Should().Be((100m, StatusParcela.Paga));
        }
        var tipoMov = lado == TipoLadoFinanceiro.Pagar ? "saida" : "entrada";
        (await conferir.MovimentosCaixa.AsNoTracking().CountAsync(m => m.EmpresaId == empresaId && m.Tipo == tipoMov))
            .Should().Be(1, "o Caixa recebe um só movimento da parcela");
        (await conferir.ContasFinanceirasEventos.AsNoTracking().CountAsync(e =>
                (e.ContaPagarId == contaId || e.ContaReceberId == contaId)
                && e.TipoEvento == TipoEventoContaFinanceira.PagamentoConfirmado))
            .Should().Be(1);
    }

    private async Task<(Guid EmpresaId, Guid ContaId, Guid ParcelaId)> SemearAsync(TipoLadoFinanceiro lado)
    {
        var empresaId = Guid.NewGuid();
        var loja = Loja.Criar(empresaId, "Loja baixas");
        await using var seed = fixture.CreateDbContext();
        seed.Empresas.Add(new Empresa { Id = empresaId, Nome = "Baixas simultaneas", Documento = Guid.NewGuid().ToString("N")[..14],
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        seed.Lojas.Add(loja);
        seed.SetMobileTenantContext(empresaId);
        seed.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "abertura", 100m, DateTime.UtcNow.AddMinutes(-5), loja.Id));
        Guid contaId, parcelaId;
        if (lado == TipoLadoFinanceiro.Pagar)
        {
            var categoria = CategoriaFinanceira.Criar(empresaId, "Desp", TipoCategoriaFinanceira.Despesa);
            seed.CategoriasFinanceiras.Add(categoria);
            var conta = ContaPagar.Criar(empresaId, null, categoria.Id, "Fornecedor", DateTime.UtcNow.Date, lojaId: loja.Id);
            parcelaId = conta.AdicionarParcela(1, 100m, DateTime.UtcNow.Date.AddDays(5)).Id;
            conta.Emitir();
            seed.ContasPagar.Add(conta);
            contaId = conta.Id;
        }
        else
        {
            var categoria = CategoriaFinanceira.Criar(empresaId, "Rec", TipoCategoriaFinanceira.Receita);
            seed.CategoriasFinanceiras.Add(categoria);
            var conta = ContaReceber.Criar(empresaId, null, categoria.Id, "Encomenda", DateTime.UtcNow.Date, lojaId: loja.Id);
            parcelaId = conta.AdicionarParcela(1, 100m, DateTime.UtcNow.Date.AddDays(5)).Id;
            conta.Emitir();
            seed.ContasReceber.Add(conta);
            contaId = conta.Id;
        }
        await seed.SaveChangesAsync();
        return (empresaId, contaId, parcelaId);
    }

    private ServiceProvider BuildProductionProvider()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        services.AddMemoryCache();
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddEasyStockPostgreInfrastructure(fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        return services.BuildServiceProvider();
    }
}
