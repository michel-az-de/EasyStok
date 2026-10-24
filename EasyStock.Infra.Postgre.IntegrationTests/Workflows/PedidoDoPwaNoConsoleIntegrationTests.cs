using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Mobile.Services.Linkers;
using EasyStock.Api.Services.Operacao;
using EasyStock.Application.Common;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.UseCases.AdicionarItemPedido;
using EasyStock.Application.UseCases.AtualizarStatusPedido;
using EasyStock.Application.UseCases.CancelarPedido;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
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
/// #1571: o pedido que nasce no PWA vira Pedido do ERP, e aí dois caminhos baixam a mesma venda. O
/// aparelho baixa com o id do pedido de lá no documento; a esteira do ERP (console, KDS), com
/// <c>{pedido}:{item}</c>. Uma idempotência não enxergava a outra: avançar dos dois lados baixava duas
/// vezes, e cancelar de um lado não devolvia a baixa do outro. Contra Postgres real, com o envio
/// (<see cref="SyncMutationDispatcher"/>), o vínculo (<see cref="OrderLinker"/>) e os casos de uso do
/// console resolvidos pelo DI.
/// </summary>
public class PedidoDoPwaNoConsoleIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string PedidoNoAparelho = "o-1";
    private const string Lasanha = "Lasanha";
    private long _relogioDoAparelho = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [SkippableFact]
    public async Task ConsoleAvancaPrimeiro_OAparelhoNaoBaixaDeNovo_ECancelarPeloAparelhoDevolveUmaVez()
    {
        var (s, provider) = await ComecarAsync();
        await using var _ = provider;
        await AparelhoManda(provider, s, "aguardando");

        await ConsoleAvanca(provider, s, "preparando");
        await ConsoleAvanca(provider, s, "pronto");
        (await Saldo(s)).Should().Be(8m, "a esteira do ERP baixou as 2 porções");

        // O aparelho não ficou sabendo e o operador dá o pedido como pronto lá também.
        await AparelhoManda(provider, s, "pronto");
        (await Saldo(s)).Should().Be(8m, "a mesma venda não sai duas vezes");
        (await SaidasDeVendaAbertas(s)).Should().Be(1);

        await AparelhoManda(provider, s, "cancelado");
        (await StatusNoErp(s)).Should().Be("cancelado");
        (await Saldo(s)).Should().Be(10m, "o cancelamento do aparelho devolve o que a esteira baixou");

        await AparelhoManda(provider, s, "cancelado");
        (await Saldo(s)).Should().Be(10m, "uma vez só");
    }

    [SkippableFact]
    public async Task EntregueOfflineDepoisDoFechamento_RecebimentoEntraNoDiaDoRegistro()
    {
        // #2030: entregue no aparelho ontem, sem rede, e sincronizado hoje com o caixa de ontem fechado no console.
        // Na hora do aparelho o recebimento cairia no dia fechado: o retrato não o contou e ele não poderia ser desfeito.
        var (s, provider) = await ComecarAsync();
        await using var _ = provider;
        var ontem = DateTime.UtcNow.AddDays(-1);
        _relogioDoAparelho = new DateTimeOffset(ontem).ToUnixTimeMilliseconds();
        await AparelhoManda(provider, s, "pronto");
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(s.EmpresaId);
            db.Add(FechamentoCaixa.Criar(s.EmpresaId, HorarioBrasil.DataOperacional(ontem), 0, 0, 0, 0, 0, lojaId: null));
            await db.SaveChangesAsync();
        }
        var antes = DateTime.UtcNow;

        await AparelhoManda(provider, s, "entregue");

        var pedidoId = await PedidoDoErp(s);
        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(s.EmpresaId);
        var pagoEm = await leitura.Set<PedidoPagamento>().Where(p => p.PedidoId == pedidoId).Select(p => p.PagoEm).SingleAsync();
        pagoEm.Should().BeOnOrAfter(antes);
    }

    [SkippableFact]
    public async Task AparelhoBaixaPrimeiro_CancelarPeloConsoleDevolveUmaVez()
    {
        var (s, provider) = await ComecarAsync();
        await using var _ = provider;

        await AparelhoManda(provider, s, "pronto");
        (await Saldo(s)).Should().Be(8m, "o aparelho baixou as 2 porções");
        await ConsoleAvanca(provider, s, "entregue");

        await ConsoleCancela(provider, s);
        (await StatusNoErp(s)).Should().Be("cancelado");
        (await Saldo(s)).Should().Be(10m, "o cancelamento do console devolve o que o aparelho baixou");

        // O aparelho, que ainda tinha o pedido como pronto, cancela depois.
        await AparelhoManda(provider, s, "cancelado");
        (await Saldo(s)).Should().Be(10m, "o que já voltou não volta de novo");
    }

    [SkippableFact]
    public async Task AparelhoBaixou_EOStatusNaoChegouAoErp_ConsoleAvancaSemBaixarDeNovo()
    {
        // O envio do aparelho gravou (e baixou), mas o vínculo que leva o status ao ERP não rodou.
        // O console vê o pedido em "aguardando" e o avança: a esteira tem de enxergar a baixa do aparelho.
        var (s, provider) = await ComecarAsync();
        await using var _ = provider;
        await AparelhoManda(provider, s, "aguardando");
        await AparelhoManda(provider, s, "pronto", comVinculo: false);
        ((await Saldo(s)), (await StatusNoErp(s))).Should().Be((8m, "aguardando"));

        await ConsoleAvanca(provider, s, "preparando");
        await ConsoleAvanca(provider, s, "pronto");

        (await Saldo(s)).Should().Be(8m, "as 2 porções já tinham saído pelo aparelho");
    }

    [SkippableFact]
    public async Task AparelhoBaixou_ConsoleAcrescentaItemDoMesmoProduto_SaiSoOItemNovo()
    {
        // Acrescentar item a um pedido pronto baixa "o que falta". Sem enxergar a baixa do aparelho, a
        // esteira dava as 2 porções do pedido por não baixadas e tirava 3 em vez de 1.
        var (s, provider) = await ComecarAsync();
        await using var _ = provider;
        await AparelhoManda(provider, s, "pronto");
        (await Saldo(s)).Should().Be(8m);

        await ConsoleAcrescentaUmaLasanha(provider, s);
        (await Saldo(s)).Should().Be(7m, "só a porção acrescentada sai");

        await ConsoleCancela(provider, s);
        (await Saldo(s)).Should().Be(10m, "o cancelamento devolve a baixa do aparelho e a da esteira");
    }

    [SkippableFact]
    public async Task ConsoleBaixaECancela_OAparelhoRefaz_ItemAcrescentadoDepoisSaiInteiro()
    {
        // O item antigo fica com documento da esteira (estornado). O item novo não pode ser dado por
        // coberto pela baixa que o aparelho refez: o aparelho não sabe dele.
        var (s, provider) = await ComecarAsync();
        await using var _ = provider;
        await AparelhoManda(provider, s, "aguardando");
        await ConsoleAvanca(provider, s, "preparando");
        await ConsoleAvanca(provider, s, "pronto");
        await ConsoleCancela(provider, s);
        (await Saldo(s)).Should().Be(10m);
        await AparelhoManda(provider, s, "pronto");
        ((await Saldo(s)), (await StatusNoErp(s))).Should().Be((8m, "pronto"));

        await ConsoleAcrescentaUmaLasanha(provider, s);

        (await Saldo(s)).Should().Be(7m, "2 do aparelho e 1 do item que o console acrescentou");
    }

    [SkippableFact]
    public async Task AparelhoAtrasadoLevaOStatusDeVolta_CancelarPeloConsoleAindaDevolve()
    {
        // No pedido do aparelho o status do ERP espelha o dele. Um aparelho atrasado reenvia o pedido
        // em "aguardando" depois de o console ter dado pronto: a baixa fica e o status regride. O
        // cancelamento devolvia pelo status e deixava a baixa para trás.
        var (s, provider) = await ComecarAsync();
        await using var _ = provider;
        await AparelhoManda(provider, s, "aguardando");
        await ConsoleAvanca(provider, s, "preparando");
        await ConsoleAvanca(provider, s, "pronto");
        await AparelhoManda(provider, s, "aguardando");
        ((await Saldo(s)), (await StatusNoErp(s))).Should().Be((8m, "aguardando"));

        await ConsoleCancela(provider, s);

        (await Saldo(s)).Should().Be(10m);
    }

    [SkippableFact]
    public async Task ConsoleCancela_OAparelhoQueNaoSabiaEntrega_OPedidoVoltaAValerEBaixaDeNovo()
    {
        var (s, provider) = await ComecarAsync();
        await using var _ = provider;
        await AparelhoManda(provider, s, "pronto");
        await ConsoleCancela(provider, s);
        (await Saldo(s)).Should().Be(10m);

        // No pedido que nasceu no aparelho o aparelho manda: a entrega aconteceu, então o estoque sai.
        await AparelhoManda(provider, s, "entregue");
        (await StatusNoErp(s)).Should().Be("entregue");
        (await Saldo(s)).Should().Be(8m, "a baixa que o cancelamento devolveu é refeita");

        await AparelhoManda(provider, s, "entregue");
        (await Saldo(s)).Should().Be(8m, "o reenvio não baixa de novo");
    }

    [SkippableTheory]
    [InlineData("Erro de lançamento")]
    [InlineData("Pedido cancelado")] // motivo plausível de alguém digitar; não é o da devolução automática
    public async Task EstornoFeitoAMaoNoErp_NaoVoltaComOReenvioDoPedido(string motivoDigitado)
    {
        var (s, provider) = await ComecarAsync();
        await using var _ = provider;
        await AparelhoManda(provider, s, "pronto");
        await Escopo(provider, s.EmpresaId, async sp =>
        {
            // Como o estorno manual de saída grava: o motivo digitado vai na descrição e no motivo.
            var db = sp.GetRequiredService<EasyStockDbContext>();
            var agora = DateTime.UtcNow;
            foreach (var saida in await db.Set<MovimentacaoEstoque>()
                         .Where(m => m.DocumentoReferencia == PedidoNoAparelho && m.Natureza == NaturezaMovimentacaoEstoque.Venda).ToListAsync())
            {
                (await db.Set<ItemEstoque>().SingleAsync(i => i.Id == saida.ItemEstoqueId)).RestaurarSaidaEstornada(saida.Quantidade, agora);
                saida.MarcarComoEstornada(agora);
                db.Add(MovimentacaoEstoque.CriarEstorno(Guid.NewGuid(), saida, agora, motivoDigitado, agora, motivoDigitado));
            }
            await db.SaveChangesAsync();
        });
        (await Saldo(s)).Should().Be(10m);

        await AparelhoManda(provider, s, "entregue");

        (await Saldo(s)).Should().Be(10m, "o que alguém desfez à mão no ERP não é refeito pelo reenvio do pedido");
    }

    [SkippableFact]
    public async Task PedidoComVariosProdutos_CadaItemDoErpFicaComOSeuProduto_EAEsteiraBaixaOProdutoCerto()
    {
        // O vínculo ligava o produto ao item do ERP por posição, na ordem do id do item, que é
        // sorteado: com mais de um produto o par saía trocado e o console baixava o produto errado.
        var (s, provider) = await ComecarAsync();
        await using var _ = provider;
        var pratos = new (string Nome, int Qtd)[] { ("Nhoque", 1), ("Risoto", 3), ("Escondidinho", 4), ("Strogonoff", 5) };
        foreach (var (nome, _) in pratos) await SemearProdutoAsync(provider, s, nome, estoque: 10m);
        var itens = pratos.Select(p => new OrderItemDto(NoAparelho(p.Nome), p.Nome, null, "un", p.Qtd, 30m)).ToList();

        await AparelhoManda(provider, s, "aguardando", itens);
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(s.EmpresaId);
            var doErp = await db.Set<PedidoItem>().Where(i => i.Pedido!.MobileOrderId == PedidoNoAparelho)
                .Select(i => new { i.Nome, i.ProdutoId }).ToListAsync();
            doErp.Should().HaveCount(4).And.OnlyContain(i => i.ProdutoId == s.Produtos[i.Nome], "cada item leva o produto do seu prato");
        }

        await ConsoleAvanca(provider, s, "preparando");
        await ConsoleAvanca(provider, s, "pronto");

        foreach (var (nome, qtd) in pratos)
            (await Saldo(s, nome)).Should().Be(10m - qtd, $"{nome} sai na quantidade dele");
    }

    private sealed record Semente(Guid EmpresaId, Guid CategoriaId, Dictionary<string, Guid> Produtos);

    private static string NoAparelho(string prato) => $"p-{prato.ToLowerInvariant()}";

    /// <summary>Empresa com a lasanha (10 em estoque) já ligada ao produto do aparelho.</summary>
    private async Task<(Semente, ServiceProvider)> ComecarAsync()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var s = new Semente(Guid.NewGuid(), Guid.NewGuid(), []);
        var agora = DateTime.UtcNow;
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Set<Empresa>().Add(new Empresa
            {
                Id = s.EmpresaId, Nome = "Empresa PWA", Documento = $"{Random.Shared.Next(100000, 999999)}", CriadoEm = agora, AlteradoEm = agora
            });
            seed.Set<Categoria>().Add(new Categoria { Id = s.CategoriaId, EmpresaId = s.EmpresaId, Nome = "Massas", CriadoEm = agora, AlteradoEm = agora });
            await seed.SaveChangesAsync();
        }
        var provider = BuildProductionProvider();
        await SemearProdutoAsync(provider, s, Lasanha, estoque: 10m);
        return (s, provider);
    }

    private async Task SemearProdutoAsync(ServiceProvider provider, Semente s, string nome, decimal estoque)
    {
        var id = s.Produtos[nome] = Guid.NewGuid();
        var agora = DateTime.UtcNow;
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Set<Produto>().Add(new Produto
            {
                Id = id, EmpresaId = s.EmpresaId, CategoriaId = s.CategoriaId, Nome = nome, Status = StatusProduto.Ativo,
                CriadoEm = agora, AlteradoEm = agora
            });
            seed.Set<Product>().Add(new Product { Id = NoAparelho(nome), Name = nome, Category = "massa", EmpresaId = s.EmpresaId, ErpProductId = id });
            await seed.SaveChangesAsync();
        }
        await Escopo(provider, s.EmpresaId, sp => sp.GetRequiredService<RegistrarEntradaEstoqueUseCase>().ExecuteAsync(
            new RegistrarEntradaEstoqueCommand(s.EmpresaId, id, null, estoque, 4m, null, DateTime.UtcNow.AddDays(-1),
                NaturezaMovimentacaoEstoque.Compra, null, null, null, null, null, null, null, null, null, null, null, null, null)));
    }

    /// <summary>
    /// O envio do aparelho como no <c>SyncController</c>: a mutation num SaveChanges só, e depois o
    /// vínculo. Sem <paramref name="itens"/>, o pedido tem 2 lasanhas.
    /// </summary>
    private Task AparelhoManda(ServiceProvider provider, Semente s, string status, List<OrderItemDto>? itens = null, bool comVinculo = true) =>
        Escopo(provider, s.EmpresaId, async sp =>
        {
            var ts = ++_relogioDoAparelho;
            itens ??= [new OrderItemDto(NoAparelho(Lasanha), Lasanha, null, "un", 2, 30m)];
            var pedido = new OrderDto(
                PedidoNoAparelho, null, new ClientSnapshotDto("Ana", null), itens,
                null, itens.Sum(i => i.Qty * i.UnitPrice), status, ts, ts);
            var mutation = new MutationDto($"m-{ts}-{Guid.NewGuid():N}", "dev-a", "order.upsert", JsonSerializer.SerializeToElement(pedido, Json), ts);

            await sp.GetRequiredService<SyncMutationDispatcher>().ApplyMutationAsync(mutation, "dev-a", "Thati", s.EmpresaId, null);
            await sp.GetRequiredService<EasyStockDbContext>().SaveChangesAsync();
            if (comVinculo) await sp.GetRequiredService<OrderLinker>().ExecuteAsync([PedidoNoAparelho], s.EmpresaId);
        });

    private Task ConsoleAvanca(ServiceProvider provider, Semente s, string status) =>
        Escopo(provider, s.EmpresaId, async sp => await sp.GetRequiredService<AtualizarStatusPedidoUseCase>().ExecuteAsync(
            new AtualizarStatusPedidoCommand(s.EmpresaId, await PedidoDoErp(s), status, UsuarioNome: "Bia", NivelSolicitante: NivelAcesso.Gerente)));

    private Task ConsoleCancela(ServiceProvider provider, Semente s) =>
        Escopo(provider, s.EmpresaId, async sp => await sp.GetRequiredService<CancelarPedidoUseCase>().ExecuteAsync(
            new CancelarPedidoCommand(s.EmpresaId, await PedidoDoErp(s), Motivo: "cliente desistiu", NivelSolicitante: NivelAcesso.Gerente)));

    private Task ConsoleAcrescentaUmaLasanha(ServiceProvider provider, Semente s) =>
        Escopo(provider, s.EmpresaId, async sp => await sp.GetRequiredService<AdicionarItemPedidoUseCase>().ExecuteAsync(
            new AdicionarItemPedidoCommand(s.EmpresaId, await PedidoDoErp(s), Lasanha, 1m, 30m, s.Produtos[Lasanha])));

    private async Task<Guid> PedidoDoErp(Semente s)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(s.EmpresaId);
        return await db.Set<Pedido>().Where(p => p.MobileOrderId == PedidoNoAparelho).Select(p => p.Id).SingleAsync();
    }

    private async Task<string?> StatusNoErp(Semente s)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(s.EmpresaId);
        return await db.Set<Pedido>().Where(p => p.MobileOrderId == PedidoNoAparelho).Select(p => p.Status).SingleAsync();
    }

    private async Task<decimal> Saldo(Semente s, string prato = Lasanha)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(s.EmpresaId);
        var produtoId = s.Produtos[prato];
        return (await db.Set<ItemEstoque>().Where(i => i.ProdutoId == produtoId).ToListAsync()).Sum(i => i.QuantidadeAtual.Value);
    }

    private async Task<int> SaidasDeVendaAbertas(Semente s)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(s.EmpresaId);
        var produtoId = s.Produtos[Lasanha];
        return await db.Set<MovimentacaoEstoque>().CountAsync(m =>
            m.ProdutoId == produtoId && m.Natureza == NaturezaMovimentacaoEstoque.Venda
            && m.Tipo == TipoMovimentacaoEstoque.Saida && m.EstornadaEm == null);
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
        // Criar pedido registra o endereço de entrega; CEP e geocoding são registrados pela API (#1560).
        services.AddSingleton(Substitute.For<ICepLookupClient>());
        services.AddSingleton(Substitute.For<IGeocodingClient>());
        services.AddSingleton<OperacaoEventBroker>();
        services.AddScoped<MobileSystemUserResolver>();
        services.AddScoped<MobileStockReconciler>();
        services.AddScoped<MobileSaleSyncService>();
        services.AddScoped<LoteMobileEstadoReconciler>();
        services.AddScoped<SyncMutationDispatcher>();
        services.AddScoped<OrderLinker>();
        return services.BuildServiceProvider();
    }
}
