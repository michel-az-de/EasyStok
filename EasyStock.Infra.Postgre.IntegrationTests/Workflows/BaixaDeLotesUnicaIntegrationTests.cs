using EasyStock.Api.Mobile.Services;
using EasyStock.Application.Common;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.UseCases.AtualizarStatusPedido;
using EasyStock.Application.UseCases.RegistrarSaidaEstoque;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
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
/// #2003 (estoque-01, 06, 07, 17): a venda (<see cref="RegistrarSaidaEstoqueUseCase"/>), a esteira do pedido
/// (<c>PedidoEstoqueIntegrationService</c>, via <see cref="AtualizarStatusPedidoUseCase"/>) e o PWA
/// (<see cref="MobileStockReconciler"/>) escolhiam os lotes cada um do seu jeito. Estes testes rodam as três
/// rotas contra Postgres real, com o mesmo estoque, e exigem a mesma resposta: FEFO sem vencido na frente,
/// empate pelo lote que entrou antes e <c>FifoAtivo</c> da loja.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class BaixaDeLotesUnicaIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    public enum Rota { Venda, Pedido, Pwa }

    private const string Lasanha = "Lasanha";
    // O dia que decide o vencimento e o de Brasilia: entre 21h e meia-noite o dia UTC ja virou e "ontem" nao venceu.
    private static readonly DateTime Hoje = DateTime.SpecifyKind(HorarioBrasil.DataOperacional(DateTime.UtcNow).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

    private sealed record Semente(Guid EmpresaId, Guid ProdutoId, Guid? LojaId);

    /// <summary>Um lote de lasanha: rótulo (gravado em CodigoInterno), saldo, dias até vencer e dias desde a entrada.</summary>
    private sealed record Lote(string Rotulo, decimal Saldo, int? DiasParaVencer, int DiasDesdeAEntrada = 1);

    public static TheoryData<Rota> TodasAsRotas => new() { Rota.Venda, Rota.Pedido, Rota.Pwa };
    public static TheoryData<Rota> RotasDeConsumo => new() { Rota.Pedido, Rota.Pwa };

    [SkippableTheory]
    [MemberData(nameof(RotasDeConsumo))]
    public async Task LoteVencido_NaoSaiAntesDoQueAindaVale(Rota rota)
    {
        // estoque-01: A venceu ontem e ninguém lançou a perda; B vale. O pedido de 2 sai de B. O cardápio e o
        // Estoque do dia ignoram o vencido: baixar dele esconde o vencido real e deixa o saldo vendável a menos.
        var (s, provider) = await ComecarAsync(
            new Lote("A", 3m, DiasParaVencer: -1), new Lote("B", 10m, DiasParaVencer: 20));
        await using var _ = provider;

        await BaixarAsync(rota, provider, s, 2m);

        (await SaldosAsync(s)).Should().Equal(new Dictionary<string, decimal> { ["A"] = 3m, ["B"] = 8m });
    }

    [SkippableFact]
    public async Task Venda_NaoBaixaLoteVencido()
    {
        // Já era a regra da venda (#983); fica de caracterização para as outras rotas se alinharem a ela.
        var (s, provider) = await ComecarAsync(
            new Lote("A", 3m, DiasParaVencer: -1), new Lote("B", 10m, DiasParaVencer: 20));
        await using var _ = provider;

        await BaixarAsync(Rota.Venda, provider, s, 2m);

        (await SaldosAsync(s)).Should().Equal(new Dictionary<string, decimal> { ["A"] = 3m, ["B"] = 8m });
    }

    [SkippableTheory]
    [MemberData(nameof(RotasDeConsumo))]
    public async Task SemLoteQueValha_OVencidoAindaBaixa_AntesDeVirarDescoberto(Rota rota)
    {
        // O pedido já foi feito e a cozinha entregou: sem outro lote, baixa do vencido (como já era) em vez de
        // deixar tudo a descoberto.
        var (s, provider) = await ComecarAsync(new Lote("A", 3m, DiasParaVencer: -1));
        await using var _ = provider;

        await BaixarAsync(rota, provider, s, 2m);

        (await SaldosAsync(s)).Should().Equal(new Dictionary<string, decimal> { ["A"] = 1m });
    }

    [SkippableTheory]
    [MemberData(nameof(TodasAsRotas))]
    public async Task ValidadeIgual_SaiOLoteQueEntrouAntes(Rota rota)
    {
        // estoque-01/17: a esteira desempatava pela ordem da consulta (o mais novo primeiro); o PWA e a venda,
        // pelo mais antigo. Vale o mais antigo.
        var (s, provider) = await ComecarAsync(
            new Lote("NOVO", 5m, DiasParaVencer: 10, DiasDesdeAEntrada: 1),
            new Lote("VELHO", 5m, DiasParaVencer: 10, DiasDesdeAEntrada: 6));
        await using var _ = provider;

        await BaixarAsync(rota, provider, s, 2m);

        (await SaldosAsync(s)).Should().Equal(new Dictionary<string, decimal> { ["NOVO"] = 5m, ["VELHO"] = 3m });
    }

    [SkippableTheory]
    [MemberData(nameof(TodasAsRotas))]
    public async Task FefoPadrao_SaiOQueVenceAntes_MesmoQueTenhaEntradoDepois(Rota rota)
    {
        var (s, provider) = await ComecarAsync(
            new Lote("VENCE_ANTES", 5m, DiasParaVencer: 3, DiasDesdeAEntrada: 1),
            new Lote("VENCE_DEPOIS", 5m, DiasParaVencer: 30, DiasDesdeAEntrada: 9));
        await using var _ = provider;

        await BaixarAsync(rota, provider, s, 2m);

        (await SaldosAsync(s)).Should().Equal(new Dictionary<string, decimal> { ["VENCE_ANTES"] = 3m, ["VENCE_DEPOIS"] = 5m });
    }

    [SkippableTheory]
    [MemberData(nameof(TodasAsRotas))]
    public async Task FifoDesligadoNaLoja_SaiPelaEntrada_IgnorandoAValidade(Rota rota)
    {
        // estoque-07: a venda lia a configuração com o id da EMPRESA e nunca achava a da loja; a esteira e o PWA
        // nem liam. Com "FIFO puro" ligado na loja única, sai o lote mais antigo, vença ele antes ou depois.
        var (s, provider) = await ComecarAsync(fifoAtivo: false,
            new Lote("ENTROU_ANTES", 5m, DiasParaVencer: 30, DiasDesdeAEntrada: 9),
            new Lote("VENCE_ANTES", 5m, DiasParaVencer: 3, DiasDesdeAEntrada: 1));
        await using var _ = provider;

        await BaixarAsync(rota, provider, s, 2m);

        (await SaldosAsync(s)).Should().Equal(new Dictionary<string, decimal> { ["ENTROU_ANTES"] = 3m, ["VENCE_ANTES"] = 5m });
    }

    [SkippableFact]
    public async Task Venda_DuasLinhasDoMesmoProduto_SegueParaOProximoLote()
    {
        // estoque-06: a linha 1 esvazia A em memória (o banco ainda diz saldo > 0); na linha 2, A entrava na lista
        // com saldo 0 e estourava "estoque insuficiente" mesmo havendo saldo em B.
        var (s, provider) = await ComecarAsync(
            new Lote("A", 2m, DiasParaVencer: 5), new Lote("B", 5m, DiasParaVencer: 20));
        await using var _ = provider;

        await Escopo(provider, s.EmpresaId, sp => sp.GetRequiredService<RegistrarSaidaEstoqueUseCase>().ExecuteAsync(
            new RegistrarSaidaEstoqueCommand(
                s.EmpresaId,
                [
                    new RegistrarSaidaEstoqueItemCommand(s.ProdutoId, null, 2m, 30m, null),
                    new RegistrarSaidaEstoqueItemCommand(s.ProdutoId, null, 1m, 30m, null)
                ],
                DateTime.UtcNow, DateTime.UtcNow, null, null, NaturezaMovimentacaoEstoque.Venda, CanalVenda.LojaPropria, null)));

        (await SaldosAsync(s)).Should().Equal(new Dictionary<string, decimal> { ["A"] = 0m, ["B"] = 4m });
    }

    [SkippableFact]
    public async Task Perda_SemLote_BaixaPrimeiroOQueJaVenceu()
    {
        // Baixa de perda/descarte (PermiteBaixaDeLoteVencido) é o contrário da venda: o vencido sai na frente.
        var (s, provider) = await ComecarAsync(
            new Lote("VENCIDO", 3m, DiasParaVencer: -2), new Lote("VALE", 10m, DiasParaVencer: 20));
        await using var _ = provider;

        await Escopo(provider, s.EmpresaId, sp => sp.GetRequiredService<RegistrarSaidaEstoqueUseCase>().ExecuteAsync(
            new RegistrarSaidaEstoqueCommand(
                s.EmpresaId, [new RegistrarSaidaEstoqueItemCommand(s.ProdutoId, null, 2m, 0m, "perda")],
                DateTime.UtcNow, DateTime.UtcNow, null, null, NaturezaMovimentacaoEstoque.Perda, CanalVenda.Outro, null)));

        (await SaldosAsync(s)).Should().Equal(new Dictionary<string, decimal> { ["VENCIDO"] = 1m, ["VALE"] = 10m });
    }

    [SkippableTheory]
    [MemberData(nameof(RotasDeConsumo))]
    public async Task SaldoNaoBasta_AFaltaViraDescobertoNoUltimoLoteQueBaixou(Rota rota)
    {
        var (s, provider) = await ComecarAsync(
            new Lote("A", 1m, DiasParaVencer: 5), new Lote("B", 2m, DiasParaVencer: 20));
        await using var _ = provider;

        await BaixarAsync(rota, provider, s, 5m);

        (await SaldosAsync(s)).Should().Equal(new Dictionary<string, decimal> { ["A"] = 0m, ["B"] = 0m });
        (await DescobertosAsync(s)).Should().Equal(new Dictionary<string, decimal> { ["A"] = 0m, ["B"] = 2m });
    }

    // ---------------------------------------------------------------------------------------------------------

    private Task BaixarAsync(Rota rota, ServiceProvider provider, Semente s, decimal quantidade) => rota switch
    {
        Rota.Venda => Escopo(provider, s.EmpresaId, sp => sp.GetRequiredService<RegistrarSaidaEstoqueUseCase>().ExecuteAsync(
            new RegistrarSaidaEstoqueCommand(
                s.EmpresaId, [new RegistrarSaidaEstoqueItemCommand(s.ProdutoId, null, quantidade, 30m, null)],
                DateTime.UtcNow, DateTime.UtcNow, null, null, NaturezaMovimentacaoEstoque.Venda, CanalVenda.LojaPropria, null,
                PermitirDescoberto: true))),
        Rota.Pedido => PedidoVaiAPronto(provider, s, quantidade),
        _ => Escopo(provider, s.EmpresaId, async sp =>
        {
            var db = sp.GetRequiredService<EasyStockDbContext>();
            var produtoDoAparelho = await db.Set<Product>().IgnoreQueryFilters().SingleAsync(p => p.ErpProductId == s.ProdutoId);
            (await sp.GetRequiredService<MobileStockReconciler>().BaixarVendaAsync(
                produtoDoAparelho, (int)quantidade, "o-1", s.LojaId, "Venda PWA")).Should().BeTrue();
            await db.SaveChangesAsync();
        }),
    };

    private async Task PedidoVaiAPronto(ServiceProvider provider, Semente s, decimal quantidade)
    {
        var pedido = Pedido.Criar(s.EmpresaId, lojaId: s.LojaId);
        pedido.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, ProdutoId = s.ProdutoId, Nome = Lasanha,
            Quantidade = quantidade, PrecoUnitario = 30m, Subtotal = 30m * quantidade, CriadoEm = DateTime.UtcNow
        });
        pedido.RecalcularTotal();
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(s.EmpresaId);
            db.Pedidos.Add(pedido);
            await db.SaveChangesAsync();
        }

        foreach (var status in new[] { "preparando", "pronto" })
            await Escopo(provider, s.EmpresaId, sp => sp.GetRequiredService<AtualizarStatusPedidoUseCase>().ExecuteAsync(
                new AtualizarStatusPedidoCommand(s.EmpresaId, pedido.Id, status, UsuarioNome: "Bia", NivelSolicitante: NivelAcesso.Gerente)));
    }

    private async Task<Dictionary<string, decimal>> SaldosAsync(Semente s)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(s.EmpresaId);
        return (await db.ItensEstoque.Where(i => i.ProdutoId == s.ProdutoId).ToListAsync())
            .ToDictionary(i => i.CodigoInterno!, i => i.QuantidadeAtual.Value);
    }

    private async Task<Dictionary<string, decimal>> DescobertosAsync(Semente s)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(s.EmpresaId);
        return (await db.ItensEstoque.Where(i => i.ProdutoId == s.ProdutoId).ToListAsync())
            .ToDictionary(i => i.CodigoInterno!, i => i.QuantidadeDescoberta.Value);
    }

    private Task<(Semente, ServiceProvider)> ComecarAsync(params Lote[] lotes) => ComecarAsync(true, lotes);

    /// <summary>
    /// Empresa com uma loja (e a configuração dela), a lasanha e os lotes. O lote não leva loja: o pedido sem loja
    /// (#1534) e a venda enxergam todos os lotes da empresa, como no console da Casa da Baba.
    /// </summary>
    private async Task<(Semente, ServiceProvider)> ComecarAsync(bool fifoAtivo, params Lote[] lotes)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var agora = DateTime.UtcNow;
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var loja = Loja.Criar(empresaId, "Casa da Baba");
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Set<Empresa>().Add(new Empresa
            {
                Id = empresaId, Nome = "Empresa FEFO", Documento = $"{Random.Shared.Next(100000, 999999)}", CriadoEm = agora, AlteradoEm = agora
            });
            seed.Set<Categoria>().Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Massas", CriadoEm = agora, AlteradoEm = agora });
            seed.Set<Loja>().Add(loja);
            var configuracao = ConfiguracaoLoja.CriarPadrao(loja.Id);
            configuracao.FifoAtivo = fifoAtivo;
            seed.Set<ConfiguracaoLoja>().Add(configuracao);
            seed.Set<Produto>().Add(new Produto
            {
                Id = produtoId, EmpresaId = empresaId, CategoriaId = categoriaId, Nome = Lasanha, Status = StatusProduto.Ativo,
                CriadoEm = agora, AlteradoEm = agora
            });
            seed.Set<Product>().Add(new Product { Id = "p-lasanha", Name = Lasanha, Category = "massa", EmpresaId = empresaId, ErpProductId = produtoId });
            foreach (var lote in lotes)
            {
                var entrada = agora.AddDays(-lote.DiasDesdeAEntrada);
                seed.Set<ItemEstoque>().Add(new ItemEstoque
                {
                    Id = Guid.NewGuid(), EmpresaId = empresaId, ProdutoId = produtoId, CodigoInterno = lote.Rotulo,
                    QuantidadeInicial = Quantidade.From(lote.Saldo), QuantidadeAtual = Quantidade.From(lote.Saldo),
                    CustoUnitario = Dinheiro.FromDecimal(4m), Status = StatusItemEstoque.Ok,
                    ValidadeEm = lote.DiasParaVencer is { } dias ? Validade.From(Hoje.AddDays(dias)) : null,
                    EntradaEm = entrada, CriadoEm = entrada, AlteradoEm = entrada
                });
            }
            await seed.SaveChangesAsync();
        }
        return (new Semente(empresaId, produtoId, null), BuildProductionProvider());
    }

    private static async Task Escopo(ServiceProvider provider, Guid empresaId, Func<IServiceProvider, Task> acao)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
        await acao(scope.ServiceProvider);
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
        services.AddSingleton(Substitute.For<EasyStock.Application.Ports.Output.ICacheService>());
        services.AddEasyStockPostgreInfrastructure(fixture.ConnectionString, config);
        services.AddEasyStockApplication();
        services.AddSingleton(Substitute.For<ICepLookupClient>());
        services.AddSingleton(Substitute.For<IGeocodingClient>());
        services.AddScoped<MobileSystemUserResolver>();
        services.AddScoped<MobileStockReconciler>();
        return services.BuildServiceProvider();
    }
}
