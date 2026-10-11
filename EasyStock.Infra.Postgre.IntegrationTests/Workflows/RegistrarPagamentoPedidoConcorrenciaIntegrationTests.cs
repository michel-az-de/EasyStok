using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.UseCases.CancelarPedido;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.RegistrarPagamentoPedido;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

/// <summary>
/// issue 951 (correlacionado ao QA rodada 2, BUG-006): dois pagamentos concorrentes,
/// ambos primeiros do dia, disputavam a abertura automatica de caixa. O try/catch de
/// <c>TentarAbrirCaixaAsync</c> nunca capturava a <c>unique_violation</c> real (ela
/// estourava no CommitAsync final, fora do bloco), entao o PERDEDOR da corrida tinha o
/// PAGAMENTO inteiro descartado junto com a tentativa de abertura — nao so a abertura.
///
/// <para>
/// Reproduz contra Postgres real via Testcontainers (fixture): dois
/// <see cref="RegistrarPagamentoPedidoUseCase"/> completos, cada um resolvido do DI de
/// producao com seu proprio <see cref="EasyStockDbContext"/> scoped, disparados em
/// paralelo para o MESMO dia/empresa/loja SEM abertura previa.
/// </para>
///
/// <para>Aceite da issue 951 (fora do CI — Docker/Testcontainers indisponivel no gate):</para>
/// <list type="bullet">
///   <item>os DOIS pagamentos persistem (nenhum e descartado pela corrida da abertura);</item>
///   <item>exatamente UMA abertura automatica existe no dia (a unique parcial nao dobra).</item>
/// </list>
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class RegistrarPagamentoPedidoConcorrenciaIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableTheory]
    [InlineData("0.001", false)]
    [InlineData("0.005", false)]
    [InlineData("199.999", false)]
    [InlineData("0.01", true)]
    [InlineData("199.99", true)]
    [InlineData("200.000", true)]
    public async Task Recebimento_preserva_centavos_sem_arredondamento_implicito_no_postgres(string valorInformado, bool aceitar)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var empresaId = Guid.NewGuid();
        var loja = Loja.Criar(empresaId, "Loja centavos");
        var pedido = NovoPedidoOperacional(empresaId, loja.Id);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Empresas.Add(new Empresa { Id = empresaId, Nome = "Teste centavos", Documento = "11223344556677",
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            seed.Lojas.Add(loja);
            seed.Pedidos.Add(pedido);
            await seed.SaveChangesAsync();
        }

        var valor = decimal.Parse(valorInformado, System.Globalization.CultureInfo.InvariantCulture);
        await using var provider = BuildProductionProvider();
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
        EasyStock.Application.UseCases.Pedidos.PedidoResult? resultado = null;
        var erro = await Record.ExceptionAsync(async () => resultado = await scope.ServiceProvider
            .GetRequiredService<RegistrarPagamentoPedidoUseCase>().ExecuteAsync(
                new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "dinheiro", valor)));

        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(empresaId);
        var salvo = await conferir.Pedidos.Include(p => p.Pagamentos).SingleAsync(p => p.Id == pedido.Id);
        if (aceitar)
        {
            erro.Should().BeNull();
            resultado.Should().NotBeNull();
            resultado!.TotalPago.Should().Be(valor);
            salvo.Pagamentos.Should().ContainSingle(p => p.Valor == valor);
            salvo.TotalPago.Should().Be(resultado.TotalPago);
        }
        else
        {
            erro.Should().BeOfType<UseCaseValidationException>(
                $"o valor {valorInformado} não cabe em centavos; resposta TotalPago={resultado?.TotalPago}, banco TotalPago={salvo.TotalPago}");
            salvo.Pagamentos.Should().BeEmpty();
            (await conferir.Set<PedidoEvento>().AnyAsync(e => e.PedidoId == pedido.Id)).Should().BeFalse();
            (await conferir.MovimentosCaixa.AnyAsync(m => m.EmpresaId == empresaId)).Should().BeFalse();
            (await conferir.CobrancasPedido.AnyAsync(c => c.PedidoId == pedido.Id)).Should().BeFalse();
            (await conferir.ImpressoesPendentes.AnyAsync(i => i.PedidoId == pedido.Id)).Should().BeFalse();
        }
    }

    [SkippableFact]
    public async Task Saldo_de_encomenda_apos_sinal_online_quita_uma_vez_no_postgres()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var empresaId = Guid.NewGuid();
        var loja = Loja.Criar(empresaId, "Loja encomenda");
        var pedido = NovoPedidoOperacional(empresaId, loja.Id);
        pedido.Status = "aguardando_pagamento";
        pedido.AgendadoParaEm = DateTime.UtcNow.AddDays(2);
        var agora = DateTime.UtcNow;
        var sinal = CobrancaPedido.CriarOnline(empresaId, pedido.Id, 100m, "pref-sinal",
            "https://mp.test/sinal", agora.AddMinutes(30), 1, agora);

        await using (var seed = fixture.CreateDbContext())
        {
            seed.Empresas.Add(new Empresa { Id = empresaId, Nome = "Teste encomenda",
                Documento = "99887766554433", CriadoEm = agora, AlteradoEm = agora });
            seed.Lojas.Add(loja);
            seed.Pedidos.Add(pedido);
            seed.CobrancasPedido.Add(sinal);
            await seed.SaveChangesAsync();
        }

        await using var provider = BuildProductionProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ConfirmarPagamentoPedidoUseCase>().ExecuteAsync(
                new ConfirmarPagamentoPedidoInput(pedido.Id, "pay-sinal", "approved", 100m,
                    MetodoPagamentoExterno: "pix", PagoEm: agora, ReferenciaExterna: "pref-sinal"));
        }
        Guid saldoId;
        await using (var parcial = fixture.CreateDbContext())
        {
            parcial.SetMobileTenantContext(empresaId);
            var pagamentoParcial = await parcial.Pedidos.Include(p => p.Pagamentos).SingleAsync(p => p.Id == pedido.Id);
            pagamentoParcial.TotalPago.Should().Be(100m);
            var saldo = await parcial.CobrancasPedido.SingleAsync(c => c.PedidoId == pedido.Id && c.Provedor == "na_entrega");
            saldo.Valor.Should().Be(100m);
            saldo.Status.Should().Be(StatusCobrancaPedido.Pendente);
            saldoId = saldo.Id;
            (await parcial.Set<PedidoEvento>().CountAsync(e => e.PedidoId == pedido.Id && e.Tipo == "quitacao_confirmada"))
                .Should().Be(0);
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
            await scope.ServiceProvider.GetRequiredService<RegistrarPagamentoPedidoUseCase>().ExecuteAsync(
                new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "dinheiro", 100m, Origem: "web"));
        }

        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(empresaId);
        var salvo = await conferir.Pedidos.Include(p => p.Pagamentos).SingleAsync(p => p.Id == pedido.Id);
        salvo.TotalPago.Should().Be(200m);
        (await conferir.CobrancasPedido.SingleAsync(c => c.Id == saldoId)).Status.Should().Be(StatusCobrancaPedido.Paga);
        (await conferir.CobrancasPedido.SingleAsync(c => c.Id == sinal.Id)).Status.Should().Be(StatusCobrancaPedido.Paga);
        (await conferir.Set<PedidoEvento>().CountAsync(e => e.PedidoId == pedido.Id && e.Tipo == "quitacao_confirmada"))
            .Should().Be(1);
        (await conferir.ImpressoesPendentes.CountAsync(i => i.PedidoId == pedido.Id)).Should().Be(1);
        await using var caixaScope = provider.CreateAsyncScope();
        caixaScope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
        (await caixaScope.ServiceProvider.GetRequiredService<ICaixaRepository>()
            .GetTotalPagamentosPedidosDoDiaAsync(empresaId, HorarioBrasil.DataOperacional(DateTime.UtcNow), loja.Id))
            .Should().Be(200m);
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancelamento_e_recebimento_concorrentes_respeitam_a_decisao_que_obteve_o_lock(bool pagamentoPrimeiro)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var empresaId = Guid.NewGuid();
        var loja = Loja.Criar(empresaId, "Loja cancelamento");
        var pedido = NovoPedidoOperacional(empresaId, loja.Id);
        await using (var db = fixture.CreateDbContext())
        {
            db.Empresas.Add(new Empresa { Id = empresaId, Nome = "Teste cancelamento", Documento = "11223344556677",
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
            db.Lojas.Add(loja);
            db.Pedidos.Add(pedido);
            await db.SaveChangesAsync();
        }
        await using var provider = BuildProductionProvider();
        var primeiroAplicado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liberarCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Executar(bool pagar, bool primeiro)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionSemRetryAsync(async _ =>
            {
                if (pagar)
                    await scope.ServiceProvider.GetRequiredService<RegistrarPagamentoPedidoUseCase>()
                        .ExecuteAsync(new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "dinheiro", 10m));
                else
                    await scope.ServiceProvider.GetRequiredService<CancelarPedidoUseCase>().ExecuteAsync(
                        new CancelarPedidoCommand(empresaId, pedido.Id, Motivo: "Desistência", NivelSolicitante: NivelAcesso.Operador),
                        publicarOperacao: false);
                if (primeiro)
                {
                    primeiroAplicado.SetResult();
                    await liberarCommit.Task.WaitAsync(TimeSpan.FromSeconds(15));
                }
                return true;
            });
        }
        var vencedor = Executar(pagamentoPrimeiro, true);
        await primeiroAplicado.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var concorrente = Executar(!pagamentoPrimeiro, false);
        try
        {
            await Task.Delay(200);
            concorrente.IsCompleted.Should().BeFalse("a outra operação precisa esperar o lock do pedido");
        }
        finally { liberarCommit.TrySetResult(); }
        await vencedor;
        if (pagamentoPrimeiro)
            await FluentActions.Invoking(() => concorrente).Should().ThrowAsync<UnauthorizedAccessException>();
        else
            await FluentActions.Invoking(() => concorrente).Should().ThrowAsync<UseCaseValidationException>()
                .WithMessage("*cancelado*");

        await using var conferir = fixture.CreateDbContext();
        conferir.SetMobileTenantContext(empresaId);
        var salvo = await conferir.Pedidos.Include(p => p.Pagamentos).SingleAsync(p => p.Id == pedido.Id);
        salvo.Status.Should().Be(pagamentoPrimeiro ? "aguardando" : "cancelado");
        salvo.TotalPago.Should().Be(pagamentoPrimeiro ? 10m : 0m);
    }

    [SkippableFact]
    public async Task Dois_pagamentos_concorrentes_primeiros_do_dia_persistem_ambos_e_abrem_o_caixa_uma_vez()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        Guid pedido1Id, pedido2Id;

        // ── Seed: empresa + loja + 2 pedidos operacionais (aceitam pagamento) ──────────────
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Set<Empresa>().Add(new Empresa
            {
                Id = empresaId,
                Nome = "Empresa Concorrencia Pagamento",
                Documento = $"{Random.Shared.Next(100000, 999999)}",
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            seed.Set<Loja>().Add(new Loja
            {
                Id = lojaId,
                EmpresaId = empresaId,
                Nome = "Loja Concorrencia",
                Ativa = true,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });

            var pedido1 = NovoPedidoOperacional(empresaId, lojaId);
            var pedido2 = NovoPedidoOperacional(empresaId, lojaId);
            pedido1Id = pedido1.Id;
            pedido2Id = pedido2.Id;
            seed.Pedidos.Add(pedido1);
            seed.Pedidos.Add(pedido2);

            await seed.SaveChangesAsync();
        }

        // ── Execucao concorrente: 2 registros de pagamento, cada um com seu proprio
        // scope/DbContext (mesmo padrao de FinalizarVendaBalcaoIntegrationTests) ───────────
        await using var provider = BuildProductionProvider();

        var task1 = RegistrarPagamentoAsync(provider, empresaId, pedido1Id, 100m);
        var task2 = RegistrarPagamentoAsync(provider, empresaId, pedido2Id, 150m);

        var resultados = await Task.WhenAll(task1, task2);

        // ── Asserts: nenhum pagamento pode ter sido descartado pela corrida da abertura ────
        resultados.Should().OnlyContain(r => r != null,
            "o pagamento nunca pode ser derrubado por uma falha na abertura automatica de caixa (issue 951)");

        await using (var assert = fixture.CreateDbContext())
        {
            assert.SetMobileTenantContext(empresaId);

            var pagamentos = await assert.Set<PedidoPagamento>().ToListAsync();
            pagamentos.Should().HaveCount(2, "os dois pagamentos concorrentes tem que persistir, nao so o vencedor da corrida da abertura");
            pagamentos.Should().ContainSingle(p => p.PedidoId == pedido1Id && p.Valor == 100m);
            pagamentos.Should().ContainSingle(p => p.PedidoId == pedido2Id && p.Valor == 150m);

            var aberturas = await assert.MovimentosCaixa
                .Where(m => m.EmpresaId == empresaId && m.Tipo == "abertura" && m.EstornadoEm == null)
                .ToListAsync();
            aberturas.Should().ContainSingle("a unique parcial de abertura por dia/loja garante que so uma vence a corrida — a outra tentativa deve ter sido absorvida sem derrubar o pagamento");
        }
    }

    private static async Task<object?> RegistrarPagamentoAsync(
        ServiceProvider provider, Guid empresaId, Guid pedidoId, decimal valor)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(empresaId);

        var uc = scope.ServiceProvider.GetRequiredService<RegistrarPagamentoPedidoUseCase>();
        return await uc.ExecuteAsync(new RegistrarPagamentoPedidoCommand(
            EmpresaId: empresaId,
            PedidoId: pedidoId,
            Metodo: "pix",
            Valor: valor,
            RegistradoPorNome: "Operador Concorrencia"));
    }

    private static Pedido NovoPedidoOperacional(Guid empresaId, Guid lojaId)
    {
        var pedido = Pedido.Criar(empresaId, lojaId: lojaId); // Status = "aguardando" => aceita pagamento
        var item = new PedidoItem
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Nome = "Item Concorrencia",
            Quantidade = 1,
            PrecoUnitario = 200m,
            Subtotal = 200m,
            CriadoEm = DateTime.UtcNow
        };
        pedido.Itens.Add(item);
        pedido.RecalcularTotal();
        return pedido;
    }

    private ServiceProvider BuildProductionProvider()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        services.AddSingleton(Substitute.For<IEstornoPedidoGateway>());
        services.AddMemoryCache();
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddEasyStockPostgreInfrastructure(fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        return services.BuildServiceProvider();
    }
}
