using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.FinalizarVendaBalcao;
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
/// Venda balcao atomica ponta a ponta (sem browser) contra Postgres real:
/// cliente novo + produto novo + entrada de estoque + pedido +
/// Aguardando->Preparando->Pronto (saida de estoque)->Entregue + pagamento,
/// tudo numa unica transacao do <see cref="FinalizarVendaBalcaoUseCase"/>.
///
/// Construido via DI de PRODUCAO (AddEasyStockApplication + AddEasyStockPostgreInfrastructure)
/// em vez de montar os 6 use cases + servicos + ~25 repos a mao: o orquestrador abre
/// BeginTransactionAsync e os UCs compostos chamam CommitAsync (=SaveChanges) DENTRO dela,
/// entao todos precisam compartilhar o MESMO EasyStockDbContext — o que o scope do DI
/// garante (IUnitOfWork resolve para o proprio DbContext scoped). E o mesmo caminho que
/// PedidosController.CreateBalcao exercita em producao.
/// </summary>
public class FinalizarVendaBalcaoIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task VendaBalcao_paga_com_produto_novo_persiste_tudo_atomico()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();

        // ── 1. Seed: empresa + loja + categoria ────────────────────────────
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Set<Empresa>().Add(new Empresa
            {
                Id = empresaId,
                Nome = "Empresa E2E",
                Documento = $"{Random.Shared.Next(100000, 999999)}",
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            seed.Set<Loja>().Add(new Loja
            {
                Id = lojaId,
                EmpresaId = empresaId,
                Nome = "Loja E2E",
                Ativa = true,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            seed.Set<Categoria>().Add(new Categoria
            {
                Id = categoriaId,
                EmpresaId = empresaId,
                Nome = "Geral",
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

        // ── 2. Orquestrador via DI de producao ─────────────────────────────
        await using var provider = BuildProductionProvider();
        FinalizarVendaBalcaoResult result;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(empresaId);

            var balcao = scope.ServiceProvider.GetRequiredService<FinalizarVendaBalcaoUseCase>();

            result = await balcao.ExecuteAsync(new FinalizarVendaBalcaoCommand(
                EmpresaId: empresaId,
                LojaId: lojaId,
                ClienteId: null,
                NovoClienteNome: "Cliente E2E",
                NovoClienteApt: null,
                NovoClienteTelefone: null,
                ClienteNomeAdHoc: null,
                Itens: new[]
                {
                    new FinalizarVendaBalcaoItemInput(
                        Nome: "Produto E2E",
                        Quantidade: 3,
                        PrecoUnitario: 25m,
                        ProdutoId: null,
                        NovoProduto: true,
                        CategoriaId: categoriaId,
                        CustoReferencia: 10m)
                },
                Pagou: true,
                FormaPagamento: "dinheiro",
                Observacoes: null,
                CriadoPorUserId: null,
                CriadoPorNome: "Operador E2E"));
        }

        result.Pago.Should().BeTrue();
        result.Total.Should().Be(75m);
        result.FormaPagamento.Should().Be("dinheiro");
        result.PedidoId.Should().NotBeEmpty();

        // ── 3. Asserts em contexto fresco ──────────────────────────────────
        await using (var assert = fixture.CreateDbContext())
        {
            assert.SetMobileTenantContext(empresaId);

            (await assert.Set<Produto>().CountAsync()).Should().Be(1);
            (await assert.Set<Cliente>().CountAsync()).Should().Be(1);

            var pedido = await assert.Pedidos
                .Include(p => p.Pagamentos)
                .SingleAsync();
            pedido.Status.Should().Be("entregue");
            pedido.Total.Valor.Should().Be(75m);
            pedido.LojaId.Should().Be(lojaId);
            pedido.Id.Should().Be(result.PedidoId);

            var item = await assert.ItensEstoque.SingleAsync();
            item.LojaId.Should().Be(lojaId);
            item.QuantidadeAtual!.Value.Should().Be(0m);

            var movs = await assert.MovimentacoesEstoque.ToListAsync();
            movs.Should().HaveCount(2);
            movs.Should().ContainSingle(m =>
                m.Tipo == TipoMovimentacaoEstoque.Entrada &&
                m.Natureza == NaturezaMovimentacaoEstoque.Compra &&
                m.Quantidade.Value == 3m);
            movs.Should().ContainSingle(m =>
                m.Tipo == TipoMovimentacaoEstoque.Saida &&
                m.Natureza == NaturezaMovimentacaoEstoque.Venda &&
                m.Quantidade.Value == 3m);

            pedido.Pagamentos.Should().ContainSingle();
            var pag = pedido.Pagamentos.Single();
            pag.Valor.Should().Be(75m);
            pag.Metodo.Should().Be("dinheiro");

            var caixaRepo = new EasyStock.Infra.Postgre.Repositories.CaixaRepository(assert);
            var totalDia = await caixaRepo.GetTotalPagamentosPedidosDoDiaAsync(
                empresaId, DateOnly.FromDateTime(DateTime.UtcNow), lojaId);
            totalDia.Should().Be(75m);
        }
    }

    private ServiceProvider BuildProductionProvider()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        services.AddEasyStockPostgreInfrastructure(fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        return services.BuildServiceProvider();
    }
}
