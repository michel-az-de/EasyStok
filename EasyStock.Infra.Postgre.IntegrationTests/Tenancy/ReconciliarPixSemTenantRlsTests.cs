using System.Reflection;
using System.Text;
using EasyStock.Api.BackgroundServices;
using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Financeiro.Pagamentos;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Infra.Async.Pagamentos.Webhooks;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Tenancy;

/// <summary>
/// #2743: o webhook Pix, o processor generico e o job horario rodam SEM usuario autenticado
/// (CurrentTenantId = Guid.Empty). <c>GetParcelaByEfiTxidAsync</c> so ignora o filtro EF; a policy RLS de
/// <c>parcelas_receber</c> (FORCE) zera as linhas, a parcela "some" e o Pix pago nunca baixa. Os tres pontos
/// de entrada precisam ligar <c>UseRowLevelSecurityBypass</c> antes da primeira query.
/// Roda como <c>rls_test_client</c> (NOSUPERUSER): o superuser ignoraria o RLS e nao provaria nada.
/// </summary>
public class ReconciliarPixSemTenantRlsTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task Processor_do_webhook_sem_usuario_baixa_a_parcela_e_gera_movimento()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, parcelaId, txid) = await SemearParcelaComPixAsync();

        await using var db = CriarContextoSemUsuario();
        var processor = new EfiPixWebhookProcessor(CriarReconciliar(db), db, new RowLevelSecurityBypass(db), NullLogger<EfiPixWebhookProcessor>.Instance);

        await processor.ProcessarAsync(Payload(txid), new Dictionary<string, string?>());

        await AssertBaixadaAsync(empresaId, parcelaId);
    }

    [SkippableFact]
    public async Task Controller_do_webhook_sem_usuario_baixa_a_parcela_e_gera_movimento()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, parcelaId, txid) = await SemearParcelaComPixAsync();

        await using var db = CriarContextoSemUsuario();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Efi:WebhookAllowUnsigned"] = "true",
        }).Build();
        var ambiente = Substitute.For<IWebHostEnvironment>();
        ambiente.EnvironmentName.Returns("Development");
        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(Payload(txid)));
        var controller = new WebhookPixController(config, CriarReconciliar(db),
            NullLogger<WebhookPixController>.Instance, ambiente, new RowLevelSecurityBypass(db))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        (await controller.Pix(CancellationToken.None)).Should().BeOfType<OkResult>();

        await AssertBaixadaAsync(empresaId, parcelaId);
    }

    [SkippableFact]
    public async Task Job_de_reconciliacao_sem_usuario_baixa_a_parcela_e_gera_movimento()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, parcelaId, txid) = await SemearParcelaComPixAsync();

        var efi = Substitute.For<IEfiPixService>();
        efi.ConsultarCobrancaAsync(txid, Arg.Any<CancellationToken>()).Returns(
            new EfiCobrancaStatusResult(txid, EfiCobrancaStatus.Concluida, 100m, 100m, PagoEm: DateTime.UtcNow));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => CriarContextoSemUsuario());
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EasyStockDbContext>());
        services.AddScoped<IContaReceberRepository, ContaReceberRepository>();
        services.AddScoped<ICaixaRepository, CaixaRepository>();
        services.AddScoped(_ => efi);
        services.AddScoped<ReconciliarPixParcelaReceberUseCase>();
        await using var provider = services.BuildServiceProvider();
        var job = new ContaReceberPixReconciliacaoJob(provider, NullLogger<ContaReceberPixReconciliacaoJob>.Instance);

        var processar = typeof(ContaReceberPixReconciliacaoJob)
            .GetMethod("ProcessarAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)processar.Invoke(job, [CancellationToken.None])!;

        await AssertBaixadaAsync(empresaId, parcelaId);
    }

    private static string Payload(string txid) =>
        "{\"pix\":[{\"txid\":\"" + txid + "\",\"valor\":\"100.00\"}]}";

    private static ReconciliarPixParcelaReceberUseCase CriarReconciliar(EasyStockDbContext db) =>
        new(new ContaReceberRepository(db), new CaixaRepository(db), Substitute.For<IEfiPixService>(), db,
            NullLogger<ReconciliarPixParcelaReceberUseCase>.Instance);

    // Como o escopo do webhook/job: rls_test_client (sujeito a RLS), interceptor e usuario nao autenticado.
    private EasyStockDbContext CriarContextoSemUsuario()
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.RlsClientConnectionString)
            .AddInterceptors(new SetTenantOnConnectionInterceptor())
            .Options;
        return new EasyStockDbContext(options, Substitute.For<ICurrentUserAccessor>());
    }

    private async Task<(Guid EmpresaId, Guid ParcelaId, string Txid)> SemearParcelaComPixAsync()
    {
        await fixture.ResetDatabaseAsync();
        var empresaId = Guid.NewGuid();
        var txid = $"cr{Guid.NewGuid():N}";

        await using var db = CriarContextoSemUsuario();
        using var _ = db.UseRowLevelSecurityBypass();
        db.Empresas.Add(new Empresa
        {
            Id = empresaId, Nome = "Emp Pix", Documento = empresaId.ToString("N")[..14],
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow,
        });
        var categoria = CategoriaFinanceira.Criar(empresaId, "Rec", TipoCategoriaFinanceira.Receita);
        db.CategoriasFinanceiras.Add(categoria);
        var conta = ContaReceber.Criar(empresaId, null, categoria.Id, "Encomenda Pix", DateTime.UtcNow.Date.AddDays(-10));
        conta.AdicionarParcela(1, 100m, DateTime.UtcNow.Date.AddDays(5));
        conta.Emitir();
        var parcela = conta.Parcelas.Single();
        parcela.AssociarPix(txid, "copia-cola", "qr", DateTime.UtcNow.AddDays(1));
        db.ContasReceber.Add(conta);
        await db.SaveChangesAsync();
        return (empresaId, parcela.Id, txid);
    }

    private async Task AssertBaixadaAsync(Guid empresaId, Guid parcelaId)
    {
        await using var leitura = CriarContextoSemUsuario();
        using var _ = leitura.UseRowLevelSecurityBypass();
        var parcela = await leitura.ParcelasReceber.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == parcelaId);
        parcela.Status.Should().Be(StatusParcela.Paga, "o Pix pago sem usuario logado precisa baixar a parcela");
        var movimentos = await leitura.MovimentosCaixa.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.EmpresaId == empresaId && m.Tipo == "entrada").ToListAsync();
        movimentos.Should().ContainSingle("a baixa do Pix gera um movimento de caixa");
    }
}
