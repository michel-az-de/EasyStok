using EasyStock.Application.Common;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.EstornarMovimentoCaixa;
using EasyStock.Application.UseCases.FecharCaixa;
using EasyStock.Application.UseCases.ObterCaixaDia;
using EasyStock.Application.UseCases.RegistrarMovimentoCaixa;
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
/// #1630 e #1623 contra Postgres real e o DI de produção: o número do dia fechado não muda depois,
/// o caixa esquecido aberto conta o dia da abertura desde a meia-noite de Brasília, as linhas por
/// forma descontam o estorno de venda igual ao total, e estornar um suprimento que a sangria já
/// levou é recusado.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class CaixaDoDiaNumerosIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task Dia_fechado_mostra_o_fechamento_e_o_que_entrou_depois_fica_em_pos_fechamento()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await NovaEmpresaAsync();
        var aberturaEm = DateTime.UtcNow.AddMinutes(-5);
        var dia = HorarioBrasil.DataOperacional(aberturaEm);
        await SemearAsync(empresaId, db => db.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "abertura", 100m, aberturaEm)));
        await using var provider = BuildProductionProvider();

        await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<FecharCaixaUseCase>()
            .ExecuteAsync(new FecharCaixaCommand(empresaId)));
        // Venda que o PWA sincroniza depois de fechar: o caixa fechado não a recusa.
        await SemearAsync(empresaId, db => db.Vendas.Add(NovaVenda(empresaId, 80m, DateTime.UtcNow, "pix")));

        var caixa = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<ObterCaixaDiaUseCase>()
            .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, dia)));

        caixa.Fechado.Should().BeTrue();
        caixa.TotalVendas.Should().Be(0m);
        caixa.SaldoEsperado.Should().Be(100m, "o dia fechado mostra o número do fechamento");
        caixa.SaldoEsperado.Should().Be(caixa.Fechamento!.SaldoFinal);
        caixa.PosFechamento.Should().Be(80m, "a venda depois de fechar aparece à parte");
        caixa.LinhasExtras.Should().BeEmpty("as linhas do dia fechado param no fechamento e batem com o retrato");
        GavetaCaixa.DinheiroNaGaveta(caixa).Should().Be(100m, "a gaveta do fechamento não muda depois");
    }

    [SkippableFact]
    public async Task Caixa_esquecido_aberto_conta_o_que_entrou_no_dia_da_abertura_antes_de_abrir()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await NovaEmpresaAsync();
        var ontem = HorarioBrasil.Hoje().AddDays(-1);
        var entrega = MovimentoCaixa.Criar(empresaId, "entrada", 80m, HorarioBrasil.InstanteUtc(ontem, new TimeOnly(9, 0)));
        entrega.Metodo = "dinheiro";
        await SemearAsync(empresaId, db =>
        {
            db.MovimentosCaixa.Add(entrega);
            db.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "abertura", 100m, HorarioBrasil.InstanteUtc(ontem, new TimeOnly(11, 0))));
        });
        await using var provider = BuildProductionProvider();

        var caixa = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<ObterCaixaDiaUseCase>()
            .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, HorarioBrasil.Hoje())));

        caixa.AberturaPendenteCrossDay.Should().BeTrue();
        caixa.AbertoDesde.Should().Be(ontem);
        caixa.Movimentos.Should().Contain(m => m.Id == entrega.Id, "a entrega das 9h é do dia da abertura");
        caixa.SaldoEsperado.Should().Be(180m);
        GavetaCaixa.DinheiroNaGaveta(caixa).Should().Be(180m);
    }

    [SkippableFact]
    public async Task Venda_estornada_desconta_das_linhas_por_forma_igual_ao_total()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await NovaEmpresaAsync();
        var agora = DateTime.UtcNow;
        var dia = HorarioBrasil.DataOperacional(agora);
        var vendaQueFica = NovaVenda(empresaId, 100m, agora, "dinheiro");
        var vendaEstornada = NovaVenda(empresaId, 50m, agora, "dinheiro");
        await SemearAsync(empresaId, db =>
        {
            var (lote, sementes) = Estoque(empresaId);
            db.AddRange(sementes);
            db.Vendas.AddRange(vendaQueFica, vendaEstornada);
            db.MovimentacoesEstoque.Add(SaidaDaVenda(lote, vendaQueFica, 100m, null));
            db.MovimentacoesEstoque.Add(SaidaDaVenda(lote, vendaEstornada, 50m, agora.AddMinutes(1)));
        });
        await using var provider = BuildProductionProvider();

        var caixa = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<ObterCaixaDiaUseCase>()
            .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, dia)));

        caixa.TotalVendas.Should().Be(100m);
        caixa.LinhasExtras!.Sum(l => l.Valor).Should().Be(caixa.TotalVendas + caixa.TotalPagamentosPedidos,
            "a soma das linhas por forma bate com o total do dia");
        caixa.LinhasExtras!.Where(l => l.Metodo == "dinheiro").Sum(l => l.Valor).Should().Be(100m);
        caixa.LinhasExtras!.Should().ContainSingle(l => l.Tipo == "estorno")
            .Which.Valor.Should().Be(-50m);
    }

    [SkippableFact]
    public async Task Estornar_suprimento_que_a_sangria_levou_e_recusado_e_a_gaveta_nao_fica_negativa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await NovaEmpresaAsync();
        await SemearAsync(empresaId, db => db.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "abertura", 0m, DateTime.UtcNow.AddMinutes(-5))));
        await using var provider = BuildProductionProvider();

        var suprimento = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<RegistrarMovimentoCaixaUseCase>()
            .ExecuteAsync(new RegistrarMovimentoCaixaCommand(empresaId, "entrada", 100m, Descricao: "Suprimento", Metodo: "dinheiro")));
        var sangria = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<RegistrarMovimentoCaixaUseCase>()
            .ExecuteAsync(new RegistrarMovimentoCaixaCommand(empresaId, "saida", 80m, Descricao: "Sangria", Metodo: "dinheiro")));

        var estornarSuprimento = () => NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<EstornarMovimentoCaixaUseCase>()
            .ExecuteAsync(new EstornarMovimentoCaixaCommand(empresaId, suprimento.Id, "lançado errado")));

        await estornarSuprimento.Should().ThrowAsync<UseCaseValidationException>().WithMessage("A gaveta ficaria negativa em R$*80,00*");
        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            (await db.MovimentosCaixa.SingleAsync(m => m.Id == suprimento.Id)).EstornadoEm.Should().BeNull();
        }

        // Desfeita a sangria, o suprimento pode sair.
        await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<EstornarMovimentoCaixaUseCase>()
            .ExecuteAsync(new EstornarMovimentoCaixaCommand(empresaId, sangria.Id, "lançado errado")));
        (await estornarSuprimento())!.EstornadoEm.Should().NotBeNull();
    }

    private async Task<Guid> NovaEmpresaAsync()
    {
        var empresaId = Guid.NewGuid();
        await using var db = fixture.CreateDbContext();
        db.Empresas.Add(new Empresa { Id = empresaId, Nome = "Caixa do dia", Documento = empresaId.ToString("N")[..14],
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return empresaId;
    }

    private async Task SemearAsync(Guid empresaId, Action<EasyStockDbContext> semear)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        semear(db);
        await db.SaveChangesAsync();
    }

    private static async Task<T> NoEscopoAsync<T>(ServiceProvider provider, Guid empresaId, Func<IServiceProvider, Task<T>> acao)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
        return await acao(scope.ServiceProvider);
    }

    private static Venda NovaVenda(Guid empresaId, decimal valor, DateTime em, string forma) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = empresaId, Canal = CanalVenda.LojaPropria,
        Natureza = NaturezaMovimentacaoEstoque.Venda, DataVenda = em, CriadoEm = em,
        ValorTotal = Dinheiro.FromDecimal(valor), FormaPagamentoPrincipal = forma,
    };

    private static (ItemEstoque Lote, object[] Sementes) Estoque(Guid empresaId)
    {
        var agora = DateTime.UtcNow;
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Massas", CriadoEm = agora, AlteradoEm = agora };
        var produto = new Produto
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, CategoriaId = categoria.Id, Nome = "Lasanha",
            Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo, CriadoEm = agora, AlteradoEm = agora,
        };
        var lote = new ItemEstoque
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, ProdutoId = produto.Id,
            QuantidadeInicial = Quantidade.From(10), QuantidadeAtual = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(10m), EntradaEm = agora.AddDays(-1),
            Status = StatusItemEstoque.Ok, CriadoEm = agora, AlteradoEm = agora,
        };
        return (lote, [categoria, produto, lote]);
    }

    private static MovimentacaoEstoque SaidaDaVenda(ItemEstoque lote, Venda venda, decimal valor, DateTime? estornadaEm) => new()
    {
        Id = Guid.NewGuid(), EmpresaId = lote.EmpresaId, ItemEstoqueId = lote.Id, ProdutoId = lote.ProdutoId,
        VendaId = venda.Id, Tipo = TipoMovimentacaoEstoque.Saida, Natureza = NaturezaMovimentacaoEstoque.Venda,
        Quantidade = Quantidade.From(1), ValorUnitario = Dinheiro.FromDecimal(valor), ValorTotal = Dinheiro.FromDecimal(valor),
        DataMovimentacao = venda.DataVenda, CriadoEm = venda.DataVenda, EstornadaEm = estornadaEm,
    };

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
