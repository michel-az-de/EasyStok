using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.AtualizarStatusPedido;
using EasyStock.Application.UseCases.RegistrarPagamentoPedido;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

/// <summary>
/// Pedido → Venda → Caixa ponta a ponta contra Postgres real:
/// produtos com estoque -> criar pedido -> status Pronto (saida de estoque) ->
/// pagamentos parciais -> status Entregue -> caixa reflete tudo.
///
/// Diferente de <see cref="FinalizarVendaBalcaoIntegrationTests"/>: aqui usamos
/// produtos EXISTENTES (ja com estoque) e os use cases individuais
/// (CriarPedido + AtualizarStatusPedido + RegistrarPagamentoPedido) em vez do
/// orquestrador monolitico do balcao.
///
/// Construido via DI de PRODUCAO.
/// </summary>
public class PedidoVendaCaixaIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task Pedido_com_produtos_existentes_paga_em_duas_vezes_e_caixa_bate()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId1 = Guid.NewGuid();
        var produtoId2 = Guid.NewGuid();
        var dataPedido = new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc);
        var dataPagamento1 = new DateTime(2026, 8, 10, 11, 0, 0, DateTimeKind.Utc);
        var dataPagamento2 = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

        // ── 1. Seed: empresa + loja + categoria + 2 produtos com estoque ────
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Set<Empresa>().Add(new Empresa
            {
                Id = empresaId, Nome = "Empresa E2E",
                Documento = $"{Random.Shared.Next(100000, 999999)}",
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            seed.Set<Loja>().Add(new Loja
            {
                Id = lojaId, EmpresaId = empresaId, Nome = "Loja E2E", Ativa = true,
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            seed.Set<Categoria>().Add(new Categoria
            {
                Id = categoriaId, EmpresaId = empresaId, Nome = "Geral",
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            seed.Set<Produto>().AddRange(
                new Produto
                {
                    Id = produtoId1, EmpresaId = empresaId, CategoriaId = categoriaId,
                    Nome = "Produto A", Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo,
                    PrecoReferencia = Dinheiro.FromDecimal(50m),
                    CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
                },
                new Produto
                {
                    Id = produtoId2, EmpresaId = empresaId, CategoriaId = categoriaId,
                    Nome = "Produto B", Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo,
                    PrecoReferencia = Dinheiro.FromDecimal(30m),
                    CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
                });

            // Estoque inicial: 10 unidades do A a R$20, 5 unidades do B a R$15
            seed.Set<ItemEstoque>().AddRange(
                new ItemEstoque
                {
                    Id = Guid.NewGuid(), EmpresaId = empresaId, LojaId = lojaId, ProdutoId = produtoId1,
                    QuantidadeAtual = Quantidade.From(10m), CustoUnitario = Dinheiro.FromDecimal(20m),
                    CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
                },
                new ItemEstoque
                {
                    Id = Guid.NewGuid(), EmpresaId = empresaId, LojaId = lojaId, ProdutoId = produtoId2,
                    QuantidadeAtual = Quantidade.From(5m), CustoUnitario = Dinheiro.FromDecimal(15m),
                    CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
                });

            await seed.SaveChangesAsync();
        }

        // ── 2. Orquestrador via DI de producao ─────────────────────────────
        await using var provider = BuildProductionProvider();

        // ── 3. Criar pedido com 2 itens (2x A a R$50 + 1x B a R$30 = R$130) ─
        Guid pedidoId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);

            var useCase = scope.ServiceProvider.GetRequiredService<CriarPedidoUseCase>();
            var result = await useCase.ExecuteAsync(new CriarPedidoCommand(
                EmpresaId: empresaId,
                LojaId: lojaId,
                ClienteId: null,
                ClienteNomeAdHoc: "Cliente Balcao",
                Itens:
                [
                    new CriarPedidoItemInput(Nome: "Produto A", Quantidade: 2, PrecoUnitario: 50m, ProdutoId: produtoId1),
                    new CriarPedidoItemInput(Nome: "Produto B", Quantidade: 1, PrecoUnitario: 30m, ProdutoId: produtoId2)
                ],
                Origem: "web"));

            pedidoId = result.Id;
            result.Total.Should().Be(130m);
        }

        // ── 4. Status: Aguardando -> Preparando -> Pronto (saida estoque) ──
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);

            var useCase = scope.ServiceProvider.GetRequiredService<AtualizarStatusPedidoUseCase>();
            await useCase.ExecuteAsync(new AtualizarStatusPedidoCommand(empresaId, pedidoId, "preparando", null, null, "web"));
            await useCase.ExecuteAsync(new AtualizarStatusPedidoCommand(empresaId, pedidoId, "pronto", null, null, "web"));
        }

        // ── 5. Pagamento parcial: R$80 (pix) ───────────────────────────────
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);

            var useCase = scope.ServiceProvider.GetRequiredService<RegistrarPagamentoPedidoUseCase>();
            var result = await useCase.ExecuteAsync(new RegistrarPagamentoPedidoCommand(
                empresaId, pedidoId, "pix", 80m, null, null, null, null, "web"));

            result.Should().NotBeNull();
            result!.TotalPago.Should().Be(80m);
        }

        // ── 6. Pagamento restante: R$50 (dinheiro) ─────────────────────────
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);

            var useCase = scope.ServiceProvider.GetRequiredService<RegistrarPagamentoPedidoUseCase>();
            var result = await useCase.ExecuteAsync(new RegistrarPagamentoPedidoCommand(
                empresaId, pedidoId, "dinheiro", 50m, null, null, null, null, "web"));

            result.Should().NotBeNull();
            result!.TotalPago.Should().Be(130m);
        }

        // ── 7. Status: Pronto -> Entregue ──────────────────────────────────
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);

            var useCase = scope.ServiceProvider.GetRequiredService<AtualizarStatusPedidoUseCase>();
            await useCase.ExecuteAsync(new AtualizarStatusPedidoCommand(empresaId, pedidoId, "entregue", null, null, "web"));
        }

        // ── 8. Asserts ponta a ponta ───────────────────────────────────────
        await using (var assert = fixture.CreateDbContext())
        {
            assert.SetMobileTenantContext(empresaId);

            // Pedido entregue com 2 pagamentos
            var pedido = await assert.Pedidos
                .Include(p => p.Pagamentos)
                .Include(p => p.Itens)
                .SingleAsync(p => p.Id == pedidoId);

            pedido.Status.Should().Be("entregue");
            pedido.Total.Valor.Should().Be(130m);
            pedido.TotalPago.Should().Be(130m);
            pedido.Pagamentos.Should().HaveCount(2);
            pedido.Pagamentos.Should().ContainSingle(p => p.Metodo == "pix" && p.Valor == 80m);
            pedido.Pagamentos.Should().ContainSingle(p => p.Metodo == "dinheiro" && p.Valor == 50m);

            // Estoque descontado: A de 10->8, B de 5->4
            var estoqueA = await assert.ItensEstoque.SingleAsync(i => i.ProdutoId == produtoId1);
            var estoqueB = await assert.ItensEstoque.SingleAsync(i => i.ProdutoId == produtoId2);
            estoqueA.QuantidadeAtual!.Value.Should().Be(8m);
            estoqueB.QuantidadeAtual!.Value.Should().Be(4m);

            // Movimentacoes de saida (Venda)
            var movs = await assert.MovimentacoesEstoque.ToListAsync();
            movs.Should().HaveCount(2);
            movs.Should().OnlyContain(m => m.Tipo == TipoMovimentacaoEstoque.Saida);
            movs.Should().OnlyContain(m => m.Natureza == NaturezaMovimentacaoEstoque.Venda);
            movs.Should().ContainSingle(m => m.ProdutoId == produtoId1 && m.Quantidade.Value == 2m);
            movs.Should().ContainSingle(m => m.ProdutoId == produtoId2 && m.Quantidade.Value == 1m);

            // Caixa: abertura automatica + saldo = 130
            var caixaRepo = new EasyStock.Infra.Postgre.Repositories.CaixaRepository(assert);
            var dataOp = EasyStock.Application.Common.HorarioBrasil.DataOperacional(dataPagamento1);
            var totalPagamentos = await caixaRepo.GetTotalPagamentosPedidosDoDiaAsync(empresaId, dataOp, lojaId);
            totalPagamentos.Should().Be(130m);

            var movimentos = await caixaRepo.GetMovimentosDoDiaAsync(empresaId, dataOp, lojaId);
            movimentos.Should().ContainSingle(m => m.Tipo == "abertura" && m.Origem == "auto-pagamento");
        }
    }

    [SkippableFact]
    public async Task Pagamento_idempotente_nao_dobra_caixa_nem_pagamentos()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var dataPedido = new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc);

        // ── Seed ───────────────────────────────────────────────────────────
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Set<Empresa>().Add(new Empresa
            {
                Id = empresaId, Nome = "Empresa E2E",
                Documento = $"{Random.Shared.Next(100000, 999999)}",
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            seed.Set<Loja>().Add(new Loja
            {
                Id = lojaId, EmpresaId = empresaId, Nome = "Loja E2E", Ativa = true,
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            seed.Set<Categoria>().Add(new Categoria
            {
                Id = categoriaId, EmpresaId = empresaId, Nome = "Geral",
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            seed.Set<Produto>().Add(new Produto
            {
                Id = produtoId, EmpresaId = empresaId, CategoriaId = categoriaId,
                Nome = "Produto Unico", Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo,
                PrecoReferencia = Dinheiro.FromDecimal(100m),
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            seed.Set<ItemEstoque>().Add(new ItemEstoque
            {
                Id = Guid.NewGuid(), EmpresaId = empresaId, LojaId = lojaId, ProdutoId = produtoId,
                QuantidadeAtual = Quantidade.From(10m), CustoUnitario = Dinheiro.FromDecimal(50m),
                CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        await using var provider = BuildProductionProvider();

        // ── Criar pedido e ir ate Pronto ───────────────────────────────────
        Guid pedidoId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);

            var criar = scope.ServiceProvider.GetRequiredService<CriarPedidoUseCase>();
            var pedido = await criar.ExecuteAsync(new CriarPedidoCommand(
                EmpresaId: empresaId, LojaId: lojaId,
                Itens: [new CriarPedidoItemInput(Nome: "Produto Unico", Quantidade: 1, PrecoUnitario: 100m, ProdutoId: produtoId)],
                Origem: "web"));
            pedidoId = pedido.Id;

            var status = scope.ServiceProvider.GetRequiredService<AtualizarStatusPedidoUseCase>();
            await status.ExecuteAsync(new AtualizarStatusPedidoCommand(empresaId, pedidoId, "preparando", null, null, "web"));
            await status.ExecuteAsync(new AtualizarStatusPedidoCommand(empresaId, pedidoId, "pronto", null, null, "web"));
        }

        // ── 1o pagamento ───────────────────────────────────────────────────
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);

            var pagar = scope.ServiceProvider.GetRequiredService<RegistrarPagamentoPedidoUseCase>();
            await pagar.ExecuteAsync(new RegistrarPagamentoPedidoCommand(
                empresaId, pedidoId, "pix", 100m, null, null, null, null, "web"));
        }

        // ── 2o pagamento MESMO valor (deveria falhar por excedente) ────────
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);

            var pagar = scope.ServiceProvider.GetRequiredService<RegistrarPagamentoPedidoUseCase>();
            var act = () => pagar.ExecuteAsync(new RegistrarPagamentoPedidoCommand(
                empresaId, pedidoId, "dinheiro", 100m, null, null, null, null, "web"));

            await act.Should().ThrowAsync<UseCaseValidationException>()
                .Where(ex => ex.Message.Contains("excede o pendente"));
        }

        // ── Asserts: so 1 pagamento, caixa so 100 ──────────────────────────
        await using (var assert = fixture.CreateDbContext())
        {
            assert.SetMobileTenantContext(empresaId);

            var pedido = await assert.Pedidos.Include(p => p.Pagamentos).SingleAsync(p => p.Id == pedidoId);
            pedido.Pagamentos.Should().HaveCount(1, "sobrepagamento deve ser rejeitado");
            pedido.TotalPago.Should().Be(100m);

            var caixaRepo = new EasyStock.Infra.Postgre.Repositories.CaixaRepository(assert);
            var dataOp = EasyStock.Application.Common.HorarioBrasil.DataOperacional(dataPedido);
            var totalPagamentos = await caixaRepo.GetTotalPagamentosPedidosDoDiaAsync(empresaId, dataOp, lojaId);
            totalPagamentos.Should().Be(100m, "caixa nao deve refletir pagamento rejeitado");
        }
    }

    private ServiceProvider BuildProductionProvider()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        services.AddSingleton(Substitute.For<EasyStock.Application.Ports.Output.ICacheService>());
        services.AddEasyStockPostgreInfrastructure(fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        return services.BuildServiceProvider();
    }
}
