using EasyStock.Application.Common;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.UseCases.AbrirCaixa;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.FecharCaixa;
using EasyStock.Application.UseCases.ListarFechamentosCaixa;
using EasyStock.Application.UseCases.ObterCaixaDia;
using EasyStock.Application.UseCases.RegistrarPagamentoPedido;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Notifications.Templating;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

/// <summary>
/// #2084 (banca #2073, itens B1, B3, B4 e D1): o dinheiro que fica na gaveta passa de um caixa para o
/// seguinte sem contar em dobro e sem pedir motivo de diferença que o próprio sistema criou. Postgres real,
/// DI de produção.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class CaixaGavetaEntreDiasIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task D1_fechar_sessao_esquecida_separa_o_dia_da_abertura_do_que_fica_hoje()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var (empresaId, lojaId) = await SemearEmpresaAsync();
        var ontemUtc = DateTime.UtcNow.AddDays(-1);
        var ontem = HorarioBrasil.DataOperacional(ontemUtc);
        var hoje = HorarioBrasil.Hoje();

        // Cenário do revisor D: abertura 100 e entrada 50 ontem; entrada 70 hoje, sem fechar ontem.
        await using (var seed = fixture.CreateDbContext())
        {
            seed.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "abertura", 100m, ontemUtc, lojaId));
            seed.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "entrada", 50m, ontemUtc, lojaId));
            seed.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "entrada", 70m, DateTime.UtcNow, lojaId));
            await seed.SaveChangesAsync();
        }

        await using var provider = BuildProductionProvider();

        // A leitura que a tela usa antes de fechar: o esperado do dia da abertura e, à parte, o que entrou depois.
        var antes = await NoEscopoAsync(provider, empresaId, sp =>
            sp.GetRequiredService<ObterCaixaDiaUseCase>().ExecuteAsync(new ObterCaixaDiaQuery(empresaId, hoje, lojaId)));
        antes.AberturaPendenteCrossDay.Should().BeTrue();
        GavetaCaixa.DinheiroNaGaveta(antes).Should().Be(220m, "a gaveta física tem a sessão inteira");
        antes.DinheiroEsperadoNoFechamento.Should().Be(150m, "o fechamento é do dia da abertura (ADR-0034)");
        antes.DinheiroEntrouDepois.Should().Be(70m, "o que entrou hoje fica no caixa de hoje");

        // Ela conta 220 na gaveta e fecha.
        var fech = await NoEscopoAsync(provider, empresaId, sp =>
            sp.GetRequiredService<FecharCaixaUseCase>().ExecuteAsync(
                new FecharCaixaCommand(empresaId, LojaId: lojaId, ValorContado: 220m)));
        fech.Data.Should().Be(ontem);
        fech.SaldoFinal.Should().Be(150m);
        fech.ValorContado.Should().Be(220m);
        fech.DinheiroEsperado.Should().Be(150m);
        fech.DinheiroEntrouDepois.Should().Be(70m);

        // A abertura de hoje é sugerida com o que ficou de ontem, sem os 70 de hoje.
        var lista = await NoEscopoAsync(provider, empresaId, sp =>
            sp.GetRequiredService<ListarFechamentosCaixaUseCase>().ExecuteAsync(new ListarFechamentosCaixaQuery(empresaId)));
        var gravado = lista.Items.Should().ContainSingle().Subject;
        gravado.ValorContado.Should().Be(220m);
        gravado.DinheiroEsperado.Should().Be(150m);
        gravado.DinheiroEntrouDepois.Should().Be(70m);
        gravado.DinheiroNaGaveta.Should().Be(150m, "contado 220 menos os 70 que já estão no caixa de hoje");

        // Ela abre hoje com o sugerido: o caixa de hoje espera 220 na gaveta, não 290.
        await NoEscopoAsync(provider, empresaId, sp =>
            sp.GetRequiredService<AbrirCaixaUseCase>().ExecuteAsync(new AbrirCaixaCommand(empresaId, gravado.DinheiroNaGaveta!.Value, lojaId)));
        var diaDeHoje = await NoEscopoAsync(provider, empresaId, sp =>
            sp.GetRequiredService<ObterCaixaDiaUseCase>().ExecuteAsync(new ObterCaixaDiaQuery(empresaId, hoje, lojaId)));
        diaDeHoje.SaldoInicial.Should().Be(150m);
        GavetaCaixa.DinheiroNaGaveta(diaDeHoje).Should().Be(220m, "os 70 de hoje contam uma vez só");
    }

    [SkippableFact]
    public async Task B3_fechamento_grava_o_contado_e_a_abertura_seguinte_parte_dele()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var (empresaId, lojaId) = await SemearEmpresaAsync();
        await using var provider = BuildProductionProvider();

        await NoEscopoAsync(provider, empresaId, sp =>
            sp.GetRequiredService<AbrirCaixaUseCase>().ExecuteAsync(new AbrirCaixaCommand(empresaId, 322.60m, lojaId)));
        var fech = await NoEscopoAsync(provider, empresaId, sp =>
            sp.GetRequiredService<FecharCaixaUseCase>().ExecuteAsync(
                new FecharCaixaCommand(empresaId, LojaId: lojaId, ValorContado: 320m)));
        fech.ValorContado.Should().Be(320m);
        fech.DinheiroEsperado.Should().Be(322.60m);

        await using (var conferir = fixture.CreateDbContext())
        {
            conferir.SetMobileTenantContext(empresaId);
            var salvo = await conferir.FechamentosCaixa.SingleAsync(f => f.EmpresaId == empresaId);
            salvo.ValorContado.Should().Be(320m, "o contado é coluna, não só texto em observações");
        }

        var lista = await NoEscopoAsync(provider, empresaId, sp =>
            sp.GetRequiredService<ListarFechamentosCaixaUseCase>().ExecuteAsync(new ListarFechamentosCaixaQuery(empresaId)));
        lista.Items.Single().DinheiroNaGaveta.Should().Be(320m, "a abertura seguinte parte do que foi contado");
    }

    [SkippableFact]
    public async Task B4_dinheiro_recebido_depois_de_fechar_entra_na_gaveta_sugerida()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var (empresaId, lojaId) = await SemearEmpresaAsync();
        var pedidoDinheiro = NovoPedidoOperacional(empresaId, lojaId, 12m);
        var pedidoPix = NovoPedidoOperacional(empresaId, lojaId, 40m);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Pedidos.AddRange(pedidoDinheiro, pedidoPix);
            await seed.SaveChangesAsync();
        }
        await using var provider = BuildProductionProvider();

        await NoEscopoAsync(provider, empresaId, sp =>
            sp.GetRequiredService<AbrirCaixaUseCase>().ExecuteAsync(new AbrirCaixaCommand(empresaId, 449m, lojaId)));
        await NoEscopoAsync(provider, empresaId, sp =>
            sp.GetRequiredService<FecharCaixaUseCase>().ExecuteAsync(new FecharCaixaCommand(empresaId, LojaId: lojaId)));

        // Loja segue aberta: um pedido pago em dinheiro e outro no Pix depois do fechamento.
        await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<RegistrarPagamentoPedidoUseCase>().ExecuteAsync(
            new RegistrarPagamentoPedidoCommand(empresaId, pedidoDinheiro.Id, "dinheiro", 12m, Origem: "balcao")));
        await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<RegistrarPagamentoPedidoUseCase>().ExecuteAsync(
            new RegistrarPagamentoPedidoCommand(empresaId, pedidoPix.Id, "pix", 40m, Origem: "balcao")));
        // Lançamento em dinheiro que chega depois de fechar (aparelho sincronizando, #2030). Com o corte do #2082 (D9)
        // o retrato do dia não o lista; a gaveta seguinte tem de contá-lo do mesmo jeito.
        await using (var seed = fixture.CreateDbContext())
        {
            var tardio = MovimentoCaixa.Criar(empresaId, "entrada", 5m, DateTime.UtcNow, lojaId);
            tardio.Metodo = "dinheiro";
            seed.MovimentosCaixa.Add(tardio);
            await seed.SaveChangesAsync();
        }

        var dia = await NoEscopoAsync(provider, empresaId, sp =>
            sp.GetRequiredService<ObterCaixaDiaUseCase>().ExecuteAsync(new ObterCaixaDiaQuery(empresaId, HorarioBrasil.Hoje(), lojaId)));
        dia.DinheiroPosFechamento.Should().Be(17m, "o dinheiro do pedido e o lançamento tardio; o Pix não");

        var lista = await NoEscopoAsync(provider, empresaId, sp =>
            sp.GetRequiredService<ListarFechamentosCaixaUseCase>().ExecuteAsync(new ListarFechamentosCaixaQuery(empresaId)));
        var f = lista.Items.Single();
        f.DinheiroPosFechamento.Should().Be(17m);
        f.DinheiroNaGaveta.Should().Be(466m, "449 do fechamento mais os 17 em dinheiro recebidos depois");
    }

    [SkippableFact]
    public async Task B1_abertura_automatica_herda_a_gaveta_do_ultimo_fechamento_e_espera_confirmacao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var (empresaId, lojaId) = await SemearEmpresaAsync();
        var pedido = NovoPedidoOperacional(empresaId, lojaId, 106m);
        await using (var seed = fixture.CreateDbContext())
        {
            // Ontem abriu com 130 e não teve movimento; fechou deixando 130 na gaveta.
            seed.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "abertura", 130m, DateTime.UtcNow.AddDays(-1), lojaId));
            seed.Pedidos.Add(pedido);
            await seed.SaveChangesAsync();
        }
        await using var provider = BuildProductionProvider();
        await NoEscopoAsync(provider, empresaId, sp =>
            sp.GetRequiredService<FecharCaixaUseCase>().ExecuteAsync(new FecharCaixaCommand(empresaId, LojaId: lojaId)));

        // Pedido pago em dinheiro antes de abrir o caixa de hoje: o caixa abre sozinho com a gaveta de ontem.
        await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<RegistrarPagamentoPedidoUseCase>().ExecuteAsync(
            new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "dinheiro", 106m, RegistradoPorNome: "Operadora")));

        var hoje = HorarioBrasil.Hoje();
        var dia = await NoEscopoAsync(provider, empresaId, sp =>
            sp.GetRequiredService<ObterCaixaDiaUseCase>().ExecuteAsync(new ObterCaixaDiaQuery(empresaId, hoje, lojaId)));
        dia.Aberto.Should().BeTrue();
        dia.SaldoInicial.Should().Be(130m, "herda o dinheiro deixado no último fechamento");
        dia.AberturaAConfirmar.Should().BeTrue("a gerente confere a gaveta depois");
        GavetaCaixa.DinheiroNaGaveta(dia).Should().Be(236m);

        // A gerente confere pela abertura de sempre, informando a gaveta contada agora (A1): a abertura automática é
        // confirmada, não duplicada, e volta a 130 (236 contados menos os 106 do dia).
        await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<AbrirCaixaUseCase>().ExecuteAsync(
            new AbrirCaixaCommand(empresaId, 236m, lojaId, RegistradoPorNome: "Gerente", Observacoes: "Conferido")));
        var depois = await NoEscopoAsync(provider, empresaId, sp =>
            sp.GetRequiredService<ObterCaixaDiaUseCase>().ExecuteAsync(new ObterCaixaDiaQuery(empresaId, hoje, lojaId)));
        depois.AberturaAConfirmar.Should().BeFalse();
        depois.SaldoInicial.Should().Be(130m);
        depois.Movimentos.Count(m => m.Tipo == "abertura").Should().Be(1);
    }

    [SkippableFact]
    public async Task A1_confirmar_abertura_automatica_com_a_gaveta_contada_agora_nao_conta_em_dobro()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var (empresaId, lojaId) = await SemearEmpresaAsync();
        var pedido = NovoPedidoOperacional(empresaId, lojaId, 106m);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "abertura", 130m, DateTime.UtcNow.AddDays(-1), lojaId));
            seed.Pedidos.Add(pedido);
            await seed.SaveChangesAsync();
        }
        await using var provider = BuildProductionProvider();
        await NoEscopoAsync(provider, empresaId, sp =>
            sp.GetRequiredService<FecharCaixaUseCase>().ExecuteAsync(new FecharCaixaCommand(empresaId, LojaId: lojaId)));

        // Abre sozinho herdando 130 com o pagamento de 106; depois entram 30 e 20 em dinheiro.
        await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<RegistrarPagamentoPedidoUseCase>().ExecuteAsync(
            new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "dinheiro", 106m)));
        await using (var seed = fixture.CreateDbContext())
        {
            foreach (var valor in new[] { 30m, 20m })
            {
                var entrada = MovimentoCaixa.Criar(empresaId, "entrada", valor, DateTime.UtcNow, lojaId);
                entrada.Metodo = "dinheiro";
                seed.MovimentosCaixa.Add(entrada);
            }
            await seed.SaveChangesAsync();
        }

        // A gerente confere ao meio-dia contando a gaveta inteira: 280 (faltam 6 dos 286 esperados).
        var confirmada = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<AbrirCaixaUseCase>().ExecuteAsync(
            new AbrirCaixaCommand(empresaId, 280m, lojaId, RegistradoPorNome: "Gerente")));
        confirmada.Valor.Should().Be(124m, "a abertura é o contado menos o dinheiro que entrou no dia (280 - 156)");
        confirmada.Descricao.Should().Contain("130,00", "o valor herdado do fechamento fica registrado");

        var dia = await NoEscopoAsync(provider, empresaId, sp =>
            sp.GetRequiredService<ObterCaixaDiaUseCase>().ExecuteAsync(new ObterCaixaDiaQuery(empresaId, HorarioBrasil.Hoje(), lojaId)));
        GavetaCaixa.DinheiroNaGaveta(dia).Should().Be(280m, "o esperado bate com a gaveta contada, sem dobrar os 156");
        dia.AberturaAConfirmar.Should().BeFalse();
    }

    [SkippableFact]
    public async Task A2_abertura_automatica_herda_a_gaveta_da_propria_loja()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var (empresaId, centroId) = await SemearEmpresaAsync();
        var mercadao = Loja.Criar(empresaId, "Mercadão");
        var pedido = NovoPedidoOperacional(empresaId, mercadao.Id, 25m);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Lojas.Add(mercadao);
            // Mercadão fechou anteontem com 40; o Centro fechou ontem com 150 (o fechamento mais recente da empresa).
            seed.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "abertura", 40m, DateTime.UtcNow.AddDays(-2), mercadao.Id));
            seed.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "abertura", 150m, DateTime.UtcNow.AddDays(-1), centroId));
            seed.Pedidos.Add(pedido);
            await seed.SaveChangesAsync();
        }
        await using var provider = BuildProductionProvider();
        foreach (var loja in new[] { mercadao.Id, centroId })
            await NoEscopoAsync(provider, empresaId, sp =>
                sp.GetRequiredService<FecharCaixaUseCase>().ExecuteAsync(new FecharCaixaCommand(empresaId, LojaId: loja)));

        await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<RegistrarPagamentoPedidoUseCase>().ExecuteAsync(
            new RegistrarPagamentoPedidoCommand(empresaId, pedido.Id, "dinheiro", 25m)));

        var dia = await NoEscopoAsync(provider, empresaId, sp =>
            sp.GetRequiredService<ObterCaixaDiaUseCase>().ExecuteAsync(new ObterCaixaDiaQuery(empresaId, HorarioBrasil.Hoje(), mercadao.Id)));
        dia.SaldoInicial.Should().Be(40m, "o Mercadão herda a própria gaveta, não a do Centro");
    }

    private async Task<(Guid EmpresaId, Guid LojaId)> SemearEmpresaAsync()
    {
        var empresaId = Guid.NewGuid();
        var loja = Loja.Criar(empresaId, "Loja gaveta");
        await using var seed = fixture.CreateDbContext();
        seed.Empresas.Add(new Empresa
        {
            Id = empresaId, Nome = "Empresa gaveta", Documento = $"{Random.Shared.Next(100000, 999999)}",
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
        });
        seed.Lojas.Add(loja);
        await seed.SaveChangesAsync();
        return (empresaId, loja.Id);
    }

    private static Pedido NovoPedidoOperacional(Guid empresaId, Guid lojaId, decimal valor)
    {
        var pedido = Pedido.Criar(empresaId, lojaId: lojaId);
        pedido.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = "Item gaveta", Quantidade = 1,
            PrecoUnitario = valor, Subtotal = valor, CriadoEm = DateTime.UtcNow
        });
        pedido.RecalcularTotal();
        return pedido;
    }

    private static async Task<T> NoEscopoAsync<T>(ServiceProvider provider, Guid empresaId, Func<IServiceProvider, Task<T>> acao)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(empresaId);
        return await acao(scope.ServiceProvider);
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
        services.AddSingleton<IRendererTemplate, ScribanRenderer>();
        return services.BuildServiceProvider();
    }
}
