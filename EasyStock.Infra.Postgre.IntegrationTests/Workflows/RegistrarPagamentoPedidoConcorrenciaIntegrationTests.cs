using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.RegistrarPagamentoPedido;
using EasyStock.Domain.Entities;
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
        services.AddMemoryCache();
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddEasyStockPostgreInfrastructure(fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        return services.BuildServiceProvider();
    }
}
