using EasyStock.Application.Common;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.FinalizarVendaBalcao;
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
    private sealed record VendaFeita(Guid EmpresaId, Guid LojaId, FinalizarVendaBalcaoResult Resultado, DateTime Antes, DateTime Depois);

    /// <summary>
    /// Zera o banco e faz a venda balcao paga (3 x R$ 25, cliente e produto novos) pelo DI de producao. Antes e Depois
    /// cercam o use case: o PagoEm que ele grava com UtcNow cai entre os dois.
    /// </summary>
    private async Task<VendaFeita> VenderAsync()
    {
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
        var antes = DateTime.UtcNow;
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
        var depois = DateTime.UtcNow;
        return new VendaFeita(empresaId, lojaId, result, antes, depois);
    }

    [SkippableFact]
    public async Task VendaBalcao_paga_com_produto_novo_persiste_tudo_atomico()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, lojaId, result, antes, depois) = await VenderAsync();

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
            // #3367: o carimbo e do relogio real do use case, entre antes e depois da venda.
            pag.PagoEm.Should().BeOnOrAfter(antes).And.BeOnOrBefore(depois);

            // #822: a query usa JanelaDiaUtc (dia civil BRT). Pedir pelo dia UTC fazia o
            // teste falhar SO entre 21h-24h BRT (pagamento cai no dia BRT anterior) —
            // exatamente a janela classica do caixa (#379). DataOperacional e a regua certa.
            // #3367: o dia sai do PagoEm que o banco gravou, nao de um UtcNow lido depois da venda (ver o helper).
            var totalDia = await TotalDoDiaDoPagamentoAsync(assert, empresaId, lojaId, pag);
            totalDia.Should().Be(75m);
        }
    }

    // #3367: o use case carimba PagoEm com UtcNow e nao ha relogio injetavel nele, entao a virada real nao se forca
    // pela venda. O que a prova controla e o dia que ela pede: leva o PagoEm gravado ao ultimo segundo do dia
    // anterior (venda feita antes da meia-noite de SP) e consulta pelo mesmo caminho da prova da venda. Se esse
    // caminho voltar a pedir o dia de um UtcNow lido depois, o total vem 0 e ela falha.
    [SkippableFact]
    public async Task Total_do_dia_acompanha_o_instante_gravado_quando_a_meia_noite_cai_entre_a_venda_e_a_consulta()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (empresaId, lojaId, _, _, _) = await VenderAsync();

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        var pedido = await db.Pedidos.Include(p => p.Pagamentos).SingleAsync();
        var pagamento = pedido.Pagamentos.Single();
        // Os dias saem de instantes gravados, sem ler o relogio: a prova vale tambem se a virada real cair nela.
        var diaDoCarimbo = HorarioBrasil.DataOperacional(pagamento.PagoEm);
        pagamento.PagoEm = HorarioBrasil.JanelaDiaUtc(diaDoCarimbo).IniUtc.AddSeconds(-1);
        await db.SaveChangesAsync();

        // O que o CI viu: o dia lido depois da meia-noite nao tem o pagamento.
        var caixaRepo = new EasyStock.Infra.Postgre.Repositories.CaixaRepository(db);
        (await caixaRepo.GetTotalPagamentosPedidosDoDiaAsync(empresaId, diaDoCarimbo, lojaId)).Should().Be(0m);
        // O dia do pagamento gravado, que e o que a prova da venda pede, tem.
        (await TotalDoDiaDoPagamentoAsync(db, empresaId, lojaId, pagamento)).Should().Be(75m);
    }

    /// <summary>
    /// Total de pagamentos de pedido do dia em que o banco gravou o pagamento (#3367). O dia sai do PagoEm gravado, nao
    /// de um UtcNow lido depois: com a meia-noite de SP entre a venda e a consulta, o dia lido agora nao tem o pagamento.
    /// </summary>
    private static Task<decimal> TotalDoDiaDoPagamentoAsync(
        EasyStockDbContext db, Guid empresaId, Guid lojaId, PedidoPagamento pagamento) =>
        new EasyStock.Infra.Postgre.Repositories.CaixaRepository(db)
            .GetTotalPagamentosPedidosDoDiaAsync(empresaId, HorarioBrasil.DataOperacional(pagamento.PagoEm), lojaId);

    private ServiceProvider BuildProductionProvider()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<ICurrentUserAccessor>());
        // SubscriptionStatusCache (registrado pela infra) exige IMemoryCache e o
        // ProdutoCacheInvalidator exige ICacheService — em prod a Api registra ambos;
        // aqui o teste registra (pego via #822 quando o CI voltou a rodar).
        services.AddMemoryCache();
        services.AddSingleton(Substitute.For<EasyStock.Application.Ports.Output.ICacheService>());
        services.AddEasyStockPostgreInfrastructure(fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        // #2211: a quitação avisa a equipe; o renderer real vem de AddNotificationsInfra (API e Worker), aqui basta o dublê.
        services.AddSingleton(Substitute.For<EasyStock.Application.Ports.Output.Notifications.IRendererTemplate>());
        // #1560: criar pedido registra o endereco de entrega; CEP e geocoding sao registrados pela API, nao por este provedor.
        services.AddSingleton(Substitute.For<EasyStock.Application.Ports.Output.Lookup.ICepLookupClient>());
        services.AddSingleton(Substitute.For<EasyStock.Application.Ports.Output.Lookup.IGeocodingClient>());
        // #2211: quitar o pedido avisa a equipe pelo motor; o renderer e registrado pela infra de notificacoes.
        services.AddSingleton(Substitute.For<EasyStock.Application.Ports.Output.Notifications.IRendererTemplate>());
        return services.BuildServiceProvider();
    }
}
