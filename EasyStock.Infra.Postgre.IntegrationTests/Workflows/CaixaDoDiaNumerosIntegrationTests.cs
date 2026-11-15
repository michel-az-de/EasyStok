using EasyStock.Application.Common;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.EstornarMovimentoCaixa;
using EasyStock.Application.UseCases.FecharCaixa;
using EasyStock.Application.UseCases.ListarLinhasCaixa;
using EasyStock.Application.UseCases.ObterCaixaDia;
using EasyStock.Application.UseCases.RegistrarMovimentoCaixa;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Sales;
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
        // #2324: o estorno vai em agora+1min; no último minuto do dia ele cairia no dia seguinte.
        var agora = DateTime.UtcNow;
        if (HorarioBrasil.DataOperacional(agora.AddMinutes(1)) != HorarioBrasil.DataOperacional(agora))
            agora = agora.AddMinutes(-2);
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

    // A3 da homologação (#1769): a sangria estornada sumia do Caixa do dia; o console já sabe mostrar "Estornado por X".
    [SkippableFact]
    public async Task Sangria_estornada_volta_marcada_na_lista_sem_mudar_nenhum_total()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await NovaEmpresaAsync();
        await SemearAsync(empresaId, db => db.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "abertura", 100m, DateTime.UtcNow.AddMinutes(-5))));
        await using var provider = BuildProductionProvider();
        await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<RegistrarMovimentoCaixaUseCase>()
            .ExecuteAsync(new RegistrarMovimentoCaixaCommand(empresaId, "entrada", 20m, Descricao: "Suprimento", Metodo: "dinheiro")));
        var sangria = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<RegistrarMovimentoCaixaUseCase>()
            .ExecuteAsync(new RegistrarMovimentoCaixaCommand(empresaId, "saida", 30m, Descricao: "Sangria", Metodo: "dinheiro")));
        await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<EstornarMovimentoCaixaUseCase>()
            .ExecuteAsync(new EstornarMovimentoCaixaCommand(empresaId, sangria.Id, "lançada em dobro", UsuarioNome: "Gerente")));

        var hoje = HorarioBrasil.Hoje();
        var semEstornados = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<ObterCaixaDiaUseCase>()
            .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, hoje)));
        var comEstornados = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<ObterCaixaDiaUseCase>()
            .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, hoje, IncluirEstornados: true)));

        var estornada = comEstornados.Movimentos.Should().ContainSingle(m => m.Id == sangria.Id).Subject;
        estornada.EstornadoEm.Should().NotBeNull();
        estornada.EstornadoPorNome.Should().Be("Gerente");
        estornada.MotivoEstorno.Should().Be("lançada em dobro");
        // Sem pedir, a lista segue como antes (Web, guarda do estorno duplo).
        semEstornados.Movimentos.Should().NotContain(m => m.Id == sangria.Id);

        // Nenhum total muda: o estornado aparece, mas não entra em gaveta, saídas, entradas nem saldo.
        comEstornados.TotalSaidasExtras.Should().Be(0m).And.Be(semEstornados.TotalSaidasExtras);
        comEstornados.TotalEntradasExtras.Should().Be(20m).And.Be(semEstornados.TotalEntradasExtras);
        comEstornados.SaldoEsperado.Should().Be(120m).And.Be(semEstornados.SaldoEsperado);
        GavetaCaixa.DinheiroNaGaveta(comEstornados).Should().Be(120m).And.Be(GavetaCaixa.DinheiroNaGaveta(semEstornados));
        GavetaCaixa.TetoSaidaEmDinheiro(comEstornados).Should().Be(GavetaCaixa.TetoSaidaEmDinheiro(semEstornados));
    }

    [SkippableFact]
    public async Task Lancamento_gravado_depois_do_fechamento_some_do_retrato_e_o_estorno_diz_o_motivo_certo()
    {
        // #2098 R1: o corte do retrato tira o lançamento posterior da lista do dia fechado. O estorno não pode
        // concluir que ele "já foi estornado" por não achá-lo ali: o movimento é conferido pelo id, no banco.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await NovaEmpresaAsync();
        var aberturaEm = DateTime.UtcNow.AddMinutes(-5);
        var dia = HorarioBrasil.DataOperacional(aberturaEm);
        await SemearAsync(empresaId, db => db.MovimentosCaixa.Add(MovimentoCaixa.Criar(empresaId, "abertura", 100m, aberturaEm)));
        await using var provider = BuildProductionProvider();
        await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<FecharCaixaUseCase>().ExecuteAsync(new FecharCaixaCommand(empresaId)));
        var tardio = MovimentoCaixa.Criar(empresaId, "entrada", 30m, DateTime.UtcNow.AddSeconds(5));
        tardio.Metodo = "dinheiro";
        await SemearAsync(empresaId, db => db.MovimentosCaixa.Add(tardio));

        var caixa = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<ObterCaixaDiaUseCase>()
            .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, dia)));
        caixa.Movimentos.Should().NotContain(m => m.Id == tardio.Id);
        caixa.PosFechamento.Should().Be(30m);

        var estornar = () => NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<EstornarMovimentoCaixaUseCase>()
            .ExecuteAsync(new EstornarMovimentoCaixaCommand(empresaId, tardio.Id, "errado")));
        (await estornar.Should().ThrowAsync<UseCaseValidationException>()).Which.Message.Should().NotContain("já foi estornado");
    }

    [SkippableFact]
    public async Task Linha_de_pagamento_de_pedido_traz_numero_do_dia_cliente_e_quem_registrou()
    {
        // #2082 B16/C10: "Pagamento de pedido · Pix · R$ 59" não dizia de quem era nem quem recebeu. A linha
        // passa a levar o número do pedido no dia, o cliente e o registrador, todos no mesmo SELECT do dia.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await NovaEmpresaAsync();
        var agora = DateTime.UtcNow;
        var dia = HorarioBrasil.DataOperacional(agora);
        var doProvedor = NovoPedidoComPagamento(empresaId, 12, "Maria", 59m, "pix", "Mercado Pago", agora.AddMinutes(-3));
        var baixaManual = NovoPedidoComPagamento(empresaId, 13, "João", 30m, "dinheiro", "Joana", agora.AddMinutes(-2));
        var semDados = NovoPedidoComPagamento(empresaId, null, null, 10m, "pix", null, agora.AddMinutes(-1));
        await SemearAsync(empresaId, db => db.Pedidos.AddRange(doProvedor, baixaManual, semDados));
        await using var provider = BuildProductionProvider();

        var caixa = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<ObterCaixaDiaUseCase>()
            .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, dia)));

        var linhas = caixa.LinhasExtras!.Where(l => l.Tipo == "pagamento").OrderBy(l => l.Hora).ToList();
        linhas.Select(l => (l.Valor, l.Metodo, l.Descricao, l.PedidoNumeroDoDia, l.ClienteNome, l.RegistradoPorNome))
            .Should().Equal(
                (59m, "pix", "Pagamento de pedido", (int?)12, "Maria", "Mercado Pago"),
                (30m, "dinheiro", "Pagamento de pedido", (int?)13, "João", "Joana"),
                (10m, "pix", "Pagamento de pedido", (int?)null, null, null));
        caixa.TotalPagamentosPedidos.Should().Be(linhas.Sum(l => l.Valor), "as linhas continuam somando o total do dia");
    }

    [SkippableFact]
    public async Task Sinal_fica_no_dia_em_que_entrou_e_a_entrega_pelo_PWA_traz_so_o_restante()
    {
        // #2708 (2b da #1630): sinal de 30 no dia D; o PWA entrega em D+3 e cria a Venda de 100 ligada ao pedido. Como o
        // pedido já tinha pagamento, a entrega não grava recebimento do restante. Antes, o filtro VendaId == null tirava o
        // sinal de D e a venda inteira caía em D+3. Regra: o sinal fica em D, D+3 mostra só os 70, e a soma é 100.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await NovaEmpresaAsync();
        var entregaEm = DateTime.UtcNow.AddMinutes(-1);
        var sinalEm = entregaEm.AddDays(-3);
        var venda = NovaVenda(empresaId, 100m, entregaEm, "pix");
        var pedido = NovoPedidoComPagamento(empresaId, 7, "Ana", 30m, "pix", "Joana", sinalEm);
        pedido.VendaId = venda.Id;
        await SemearAsync(empresaId, db => { db.Vendas.Add(venda); db.Pedidos.Add(pedido); });
        await using var provider = BuildProductionProvider();

        var diaDoSinal = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<ObterCaixaDiaUseCase>()
            .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, HorarioBrasil.DataOperacional(sinalEm))));
        var diaDaEntrega = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<ObterCaixaDiaUseCase>()
            .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, HorarioBrasil.DataOperacional(entregaEm))));

        var recebidoNoSinal = diaDoSinal.TotalVendas + diaDoSinal.TotalPagamentosPedidos;
        var recebidoNaEntrega = diaDaEntrega.TotalVendas + diaDaEntrega.TotalPagamentosPedidos;
        recebidoNoSinal.Should().Be(30m, "o sinal conta no dia em que o dinheiro entrou e não sai mais de lá");
        recebidoNaEntrega.Should().Be(70m, "no dia da entrega entra só o restante");
        (recebidoNoSinal + recebidoNaEntrega).Should().Be(100m, "a venda não dobra nem some na soma do período");
        diaDoSinal.LinhasExtras!.Sum(l => l.Valor).Should().Be(recebidoNoSinal, "as linhas do dia do sinal somam o total");
        diaDaEntrega.LinhasExtras!.Sum(l => l.Valor).Should().Be(recebidoNaEntrega, "as linhas do dia da entrega somam o total");
    }

    [SkippableFact]
    public async Task Restante_pago_em_dinheiro_na_entrega_entra_na_gaveta_so_pelo_que_falta()
    {
        // #2708 (b): sinal de 30 em Pix no dia D; o restante é pago em dinheiro na entrega em D+3, e o PWA grava a Venda
        // com a forma da entrega. A gaveta de D não ganha nada; a de D+3 ganha os 70, não os 100. O Pix fica no dia D.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await NovaEmpresaAsync();
        var entregaEm = DateTime.UtcNow.AddMinutes(-1);
        var sinalEm = entregaEm.AddDays(-3);
        var venda = NovaVenda(empresaId, 100m, entregaEm, "dinheiro");
        var pedido = NovoPedidoComPagamento(empresaId, 9, "Cida", 30m, "pix", "Joana", sinalEm);
        pedido.VendaId = venda.Id;
        await SemearAsync(empresaId, db => { db.Vendas.Add(venda); db.Pedidos.Add(pedido); });
        await using var provider = BuildProductionProvider();

        var diaDoSinal = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<ObterCaixaDiaUseCase>()
            .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, HorarioBrasil.DataOperacional(sinalEm))));
        var diaDaEntrega = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<ObterCaixaDiaUseCase>()
            .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, HorarioBrasil.DataOperacional(entregaEm))));

        (GavetaCaixa.DinheiroNaGaveta(diaDoSinal) - diaDoSinal.SaldoInicial).Should().Be(0m, "o sinal foi em Pix");
        diaDoSinal.LinhasExtras!.Where(l => l.Metodo == "pix").Sum(l => l.Valor).Should().Be(30m);
        (GavetaCaixa.DinheiroNaGaveta(diaDaEntrega) - diaDaEntrega.SaldoInicial).Should().Be(70m,
            "na entrega entrou em dinheiro só o que faltava");
        diaDaEntrega.LinhasExtras!.Where(l => l.Metodo == "pix").Sum(l => l.Valor).Should().Be(0m);
    }

    [SkippableFact]
    public async Task Restante_ja_gravado_na_entrega_nao_soma_com_a_venda_abatida()
    {
        // #2708 + #2711: quando a entrega passar a gravar o recebimento do restante (70, em D+1, mesmo dia da Venda),
        // ele é representado pela venda abatida e não soma por cima dela. Nunca 130 nem 170.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await NovaEmpresaAsync();
        var entregaEm = DateTime.UtcNow.AddMinutes(-1);
        var sinalEm = entregaEm.AddDays(-1);
        var venda = NovaVenda(empresaId, 100m, entregaEm, "dinheiro");
        var pedido = NovoPedidoComPagamento(empresaId, 11, "Eva", 30m, "pix", "Joana", sinalEm);
        pedido.Pagamentos.Add(new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = pedido.Id, Valor = 70m, Metodo = "dinheiro",
            PagoEm = entregaEm, RegistradoPorNome = "Joana" });
        pedido.VendaId = venda.Id;
        await SemearAsync(empresaId, db => { db.Vendas.Add(venda); db.Pedidos.Add(pedido); });
        await using var provider = BuildProductionProvider();

        var diaDoSinal = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<ObterCaixaDiaUseCase>()
            .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, HorarioBrasil.DataOperacional(sinalEm))));
        var diaDaEntrega = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<ObterCaixaDiaUseCase>()
            .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, HorarioBrasil.DataOperacional(entregaEm))));

        var recebidoNoSinal = diaDoSinal.TotalVendas + diaDoSinal.TotalPagamentosPedidos;
        var recebidoNaEntrega = diaDaEntrega.TotalVendas + diaDaEntrega.TotalPagamentosPedidos;
        recebidoNoSinal.Should().Be(30m);
        recebidoNaEntrega.Should().Be(70m, "a venda abatida já representa o restante recebido no dia");
        (recebidoNoSinal + recebidoNaEntrega).Should().Be(100m);
        diaDaEntrega.LinhasExtras!.Sum(l => l.Valor).Should().Be(recebidoNaEntrega);
        (GavetaCaixa.DinheiroNaGaveta(diaDaEntrega) - diaDaEntrega.SaldoInicial).Should().Be(70m);
    }

    [SkippableFact]
    public async Task Sinal_e_entrega_no_mesmo_dia_contam_o_total_uma_vez()
    {
        // #926, guarda da #2708: sinal de 30 e entrega pelo PWA no mesmo dia operacional. A venda de 100 representa o
        // dia inteiro; o sinal não soma por cima dela nem é abatido dela.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await NovaEmpresaAsync();
        var entregaEm = DateTime.UtcNow.AddMinutes(-1);
        var sinalEm = entregaEm.AddSeconds(-20);
        var venda = NovaVenda(empresaId, 100m, entregaEm, "pix");
        var pedido = NovoPedidoComPagamento(empresaId, 10, "Duda", 30m, "pix", "Joana", sinalEm);
        pedido.VendaId = venda.Id;
        await SemearAsync(empresaId, db => { db.Vendas.Add(venda); db.Pedidos.Add(pedido); });
        await using var provider = BuildProductionProvider();

        var caixa = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<ObterCaixaDiaUseCase>()
            .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, HorarioBrasil.DataOperacional(entregaEm))));

        (caixa.TotalVendas + caixa.TotalPagamentosPedidos).Should().Be(100m, "o total da venda conta uma vez");
        caixa.LinhasExtras!.Sum(l => l.Valor).Should().Be(100m);
    }

    [SkippableFact]
    public async Task Sinal_em_Pix_e_restante_em_dinheiro_no_mesmo_dia_poem_na_gaveta_so_o_dinheiro()
    {
        // Sinal de 30 em Pix e entrega pelo PWA no mesmo dia com o restante em dinheiro. A Venda de 100 vem com a forma
        // da entrega e representa o dia (#926). A gaveta só pode ganhar os 70 em dinheiro; os 30 ficam no Pix.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await NovaEmpresaAsync();
        var entregaEm = DateTime.UtcNow.AddMinutes(-1);
        var sinalEm = entregaEm.AddSeconds(-20);
        var venda = NovaVenda(empresaId, 100m, entregaEm, "dinheiro");
        var pedido = NovoPedidoComPagamento(empresaId, 12, "Fê", 30m, "pix", "Joana", sinalEm);
        pedido.VendaId = venda.Id;
        await SemearAsync(empresaId, db => { db.Vendas.Add(venda); db.Pedidos.Add(pedido); });
        await using var provider = BuildProductionProvider();

        var caixa = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<ObterCaixaDiaUseCase>()
            .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, HorarioBrasil.DataOperacional(entregaEm))));

        (caixa.TotalVendas + caixa.TotalPagamentosPedidos).Should().Be(100m, "o total do dia segue 100");
        (GavetaCaixa.DinheiroNaGaveta(caixa) - caixa.SaldoInicial).Should().Be(70m, "só 70 entraram em dinheiro");
        caixa.LinhasExtras!.Where(l => l.Metodo == "pix").Sum(l => l.Valor).Should().Be(30m, "o sinal foi em Pix");
    }

    [SkippableTheory]
    [InlineData(0, "dinheiro", 100)]
    [InlineData(0, "pix", 70)]
    [InlineData(1, "dinheiro", 100)]
    [InlineData(1, "pix", 70)]
    [InlineData(0, " Dinheiro ", 100)]
    public async Task Sinal_de_30_e_entrega_em_dinheiro_somam_100_e_a_gaveta_so_o_dinheiro(int diasAntes, string formaDoSinal,
        decimal dinheiroNoPeriodo)
    {
        // #2714, matriz mesmo dia/outro dia x mesma forma/forma diferente: o total do período é 100 e a gaveta recebe só
        // o que entrou em dinheiro. A forma é comparada sem espaço nem caixa (" Dinheiro " = "dinheiro").
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await NovaEmpresaAsync();
        var entregaEm = DateTime.UtcNow.AddMinutes(-1);
        var sinalEm = diasAntes == 0 ? entregaEm.AddSeconds(-20) : entregaEm.AddDays(-diasAntes);
        var venda = NovaVenda(empresaId, 100m, entregaEm, "dinheiro");
        var pedido = NovoPedidoComPagamento(empresaId, 13, "Gal", 30m, formaDoSinal, "Joana", sinalEm);
        pedido.VendaId = venda.Id;
        await SemearAsync(empresaId, db => { db.Vendas.Add(venda); db.Pedidos.Add(pedido); });
        await using var provider = BuildProductionProvider();

        var dias = new[] { HorarioBrasil.DataOperacional(sinalEm), HorarioBrasil.DataOperacional(entregaEm) }.Distinct().ToList();
        var caixas = new List<CaixaDiaResult>();
        foreach (var dia in dias)
            caixas.Add(await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<ObterCaixaDiaUseCase>()
                .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, dia))));

        caixas.Sum(c => c.TotalVendas + c.TotalPagamentosPedidos).Should().Be(100m, "o total do período nunca dobra nem some");
        caixas.Sum(c => GavetaCaixa.DinheiroNaGaveta(c) - c.SaldoInicial).Should().Be(dinheiroNoPeriodo,
            "a gaveta só recebe o que entrou em dinheiro");
        foreach (var c in caixas)
            c.LinhasExtras!.Sum(l => l.Valor).Should().Be(c.TotalVendas + c.TotalPagamentosPedidos, "as linhas somam o total do dia");
    }

    [SkippableFact]
    public async Task Sinal_e_entrega_no_mesmo_dia_e_mesma_forma_seguem_uma_linha_de_venda_no_dia_e_no_extrato()
    {
        // #926, guarda da #2714: mesmo dia e mesma forma, a lista do dia e o Extrato mostram uma linha "venda" de 100,
        // sem linha de pagamento de pedido.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await NovaEmpresaAsync();
        var entregaEm = DateTime.UtcNow.AddMinutes(-1);
        var venda = NovaVenda(empresaId, 100m, entregaEm, "dinheiro");
        var pedido = NovoPedidoComPagamento(empresaId, 14, "Hel", 30m, "dinheiro", "Joana", entregaEm.AddSeconds(-20));
        pedido.VendaId = venda.Id;
        await SemearAsync(empresaId, db => { db.Vendas.Add(venda); db.Pedidos.Add(pedido); });
        await using var provider = BuildProductionProvider();

        var caixa = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<ObterCaixaDiaUseCase>()
            .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, HorarioBrasil.DataOperacional(entregaEm))));
        var extrato = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<ListarLinhasCaixaUseCase>()
            .ExecuteAsync(new ListarLinhasCaixaQuery(empresaId, entregaEm.AddHours(-1), DateTime.UtcNow)));

        caixa.LinhasExtras!.Select(l => (l.Tipo, l.Valor)).Should().Equal(("venda", 100m));
        extrato.Select(l => (l.Tipo, l.Valor)).Should().Equal(("venda", 100m));
    }

    [SkippableFact]
    public async Task Venda_sem_forma_segue_representando_o_sinal_do_mesmo_dia()
    {
        // #2714: Venda sem forma (PWA antigo) fica como antes. O sinal do mesmo dia não vira linha separada e a linha
        // "venda" não muda para "pagamento".
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await NovaEmpresaAsync();
        var entregaEm = DateTime.UtcNow.AddMinutes(-1);
        var venda = NovaVenda(empresaId, 100m, entregaEm, null);
        var pedido = NovoPedidoComPagamento(empresaId, 15, "Iara", 30m, "pix", "Joana", entregaEm.AddSeconds(-20));
        pedido.VendaId = venda.Id;
        await SemearAsync(empresaId, db => { db.Vendas.Add(venda); db.Pedidos.Add(pedido); });
        await using var provider = BuildProductionProvider();

        var caixa = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<ObterCaixaDiaUseCase>()
            .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, HorarioBrasil.DataOperacional(entregaEm))));

        (caixa.TotalVendas + caixa.TotalPagamentosPedidos).Should().Be(100m);
        caixa.LinhasExtras!.Select(l => (l.Tipo, l.Valor)).Should().Equal(("venda", 100m));
    }

    [SkippableFact]
    public async Task Pedido_sem_sinal_entregue_pelo_PWA_conta_uma_vez_no_dia_da_entrega()
    {
        // #926, guarda da #2708: sem sinal, a entrega grava o recebimento de 100 e a Venda de 100 ligada ao pedido.
        // O dinheiro conta uma vez só.
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var empresaId = await NovaEmpresaAsync();
        var entregaEm = DateTime.UtcNow.AddMinutes(-1);
        var venda = NovaVenda(empresaId, 100m, entregaEm, "pix");
        var pedido = NovoPedidoComPagamento(empresaId, 8, "Bia", 100m, "pix", "Joana", entregaEm);
        pedido.VendaId = venda.Id;
        await SemearAsync(empresaId, db => { db.Vendas.Add(venda); db.Pedidos.Add(pedido); });
        await using var provider = BuildProductionProvider();

        var caixa = await NoEscopoAsync(provider, empresaId, sp => sp.GetRequiredService<ObterCaixaDiaUseCase>()
            .ExecuteAsync(new ObterCaixaDiaQuery(empresaId, HorarioBrasil.DataOperacional(entregaEm))));

        (caixa.TotalVendas + caixa.TotalPagamentosPedidos).Should().Be(100m);
        caixa.LinhasExtras!.Sum(l => l.Valor).Should().Be(100m);
    }

    private static Pedido NovoPedidoComPagamento(Guid empresaId, int? numeroDoDia, string? cliente, decimal valor, string metodo,
        string? registradoPor, DateTime pagoEm)
    {
        var pedido = Pedido.Criar(empresaId);
        pedido.Status = StatusPedidoMapper.Aguardando;
        pedido.ClienteNome = cliente;
        if (numeroDoDia is { } n) pedido.DefinirNumeroDoDia(HorarioBrasil.DataOperacional(pagoEm), n);
        pedido.Pagamentos.Add(new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = pedido.Id, Valor = valor, Metodo = metodo,
            PagoEm = pagoEm, RegistradoPorNome = registradoPor });
        return pedido;
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

    private static Venda NovaVenda(Guid empresaId, decimal valor, DateTime em, string? forma) => new()
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
