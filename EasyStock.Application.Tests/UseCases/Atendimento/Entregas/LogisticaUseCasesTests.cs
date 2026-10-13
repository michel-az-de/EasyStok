using EasyStock.Application.Ports.Output.Integrations;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Entregas;
using EasyStock.Domain.Entities.Atendimento;
using Microsoft.Extensions.Time.Testing;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Entregas;

public sealed class LogisticaUseCasesTests
{
    private readonly Guid empresa = Guid.NewGuid();
    private readonly Guid usuario = Guid.NewGuid();
    private readonly FakeTimeProvider tempo = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly ILogisticaRepository repo = Substitute.For<ILogisticaRepository>();
    private readonly IUnitOfWork uow = Substitute.For<IUnitOfWork>();
    private readonly ICaixaRepository caixa = Substitute.For<ICaixaRepository>();
    private readonly IFreteProvider provider = Substitute.For<IFreteProvider>();
    private readonly List<LogisticaPedido> pedidos = [];
    private readonly List<CotacaoLogistica> cotacoes = [];
    private readonly List<DespachoLogistica> despachos = [];
    private readonly LogisticaUseCases sut;

    public LogisticaUseCasesTests()
    {
        uow.SetupExecuteInTransactionForward<DespachoLogisticaResult>();
        uow.SetupExecuteInTransactionForward<PoliticaLogisticaInput>();
        repo.PedidosAsync(empresa, Arg.Any<DateOnly?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(_ => pedidos);
        repo.DespachosAsync(empresa, Arg.Any<CancellationToken>()).Returns(_ => despachos);
        repo.LimiteSaidaAsync(empresa, Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(1000m);
        repo.CotacaoAsync(empresa, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(c => cotacoes.SingleOrDefault(x => x.Id == c.ArgAt<Guid>(1)));
        repo.DespachoAsync(empresa, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(c => despachos.SingleOrDefault(x => x.Id == c.ArgAt<Guid>(1)));
        repo.When(r => r.Adicionar(Arg.Any<CotacaoLogistica>())).Do(c => cotacoes.Add(c.Arg<CotacaoLogistica>()));
        repo.When(r => r.Adicionar(Arg.Any<DespachoLogistica>())).Do(c => despachos.Add(c.Arg<DespachoLogistica>()));
        provider.Provedor.Returns("lalamove");
        provider.CotarAsync(Arg.Any<FreteSolicitacao>(), Arg.Any<CancellationToken>()).Returns(_ => new FreteCotacao("lalamove", "q1", 1600, 30, tempo.GetUtcNow().UtcDateTime.AddMinutes(5), true, null));
        provider.ContratarAsync(Arg.Any<FreteSolicitacao>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new FreteContratacao("order1", null, "agendado", true));
        sut = new(repo, uow, caixa, [provider], new(empresa, new("Cozinha", "Rua de demonstração 1", "+5500000000000", -23.55, -46.63)), tempo);
        pedidos.Add(Pedido());
    }

    [Fact]
    public async Task Empresa_divergente_nao_acessa_nem_consulta_provedor()
    {
        await FluentActions.Invoking(() => sut.ContextoAsync(Guid.NewGuid(), null, null)).Should().ThrowAsync<UseCaseValidationException>();
        await provider.DidNotReceiveWithAnyArgs().CotarAsync(default!, default);
    }

    [Fact]
    public async Task Configuracao_vazia_nao_abre_demo_em_qualquer_empresa()
    {
        var fechado = new LogisticaUseCases(repo, uow, caixa, [provider], new(Guid.Empty, new("", "", "", 0, 0)), tempo);
        await FluentActions.Invoking(() => fechado.CotarAsync(empresa, new([pedidos[0].Id], false))).Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task Contexto_da_ficha_inclui_outros_pedidos_do_dia_para_agrupar()
    {
        var principal = pedidos[0]; pedidos.Add(Pedido());
        repo.PedidosAsync(empresa, null, principal.Id, Arg.Any<CancellationToken>()).Returns(new[] { principal });
        var contexto = await sut.ContextoAsync(empresa, null, principal.Id);
        contexto.Pedidos.Should().HaveCount(2);
        await repo.Received(1).PedidosAsync(empresa, DateOnly.FromDateTime(principal.JanelaInicio.AddHours(-3)), null, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("endereco")]
    [InlineData("bloqueado")]
    [InlineData("cancelado")]
    [InlineData("localizacao")]
    public async Task Pedido_invalido_nao_chama_api(string caso)
    {
        pedidos[0] = caso switch { "endereco" => pedidos[0] with { Endereco = "" }, "bloqueado" => pedidos[0] with { ClienteBloqueado = true }, "cancelado" => pedidos[0] with { Status = "cancelado" }, _ => pedidos[0] with { Latitude = null } };
        await FluentActions.Invoking(() => sut.CotarAsync(empresa, new([pedidos[0].Id], false))).Should().ThrowAsync<UseCaseValidationException>();
        await provider.DidNotReceiveWithAnyArgs().CotarAsync(default!, default);
    }

    [Fact]
    public async Task Janelas_sem_intersecao_exigem_separar_viagens()
    {
        pedidos.Add(Pedido() with { JanelaInicio = pedidos[0].JanelaFim, JanelaFim = pedidos[0].JanelaFim.AddHours(1) });
        await FluentActions.Invoking(() => sut.CotarAsync(empresa, new(pedidos.Select(p => p.Id).ToList(), true))).Should().ThrowAsync<UseCaseValidationException>().WithMessage("*janelas*");
    }

    [Fact]
    public async Task Cotacao_expirada_nao_contrata()
    {
        var cotacao = (await Cotar()).Single(); tempo.Advance(TimeSpan.FromMinutes(6));
        await FluentActions.Invoking(() => sut.ContratarAsync(empresa, usuario, new(cotacao.Id, "requisicao"))).Should().ThrowAsync<UseCaseValidationException>().WithMessage("*venceu*");
    }

    [Fact]
    public async Task Contato_alterado_exige_nova_cotacao()
    {
        var cotacao = (await Cotar()).Single(); pedidos[0] = pedidos[0] with { Telefone = "+5511999999999" };
        await FluentActions.Invoking(() => sut.ContratarAsync(empresa, usuario, new(cotacao.Id, "requisicao"))).Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task Repetir_contratacao_e_pagamento_nao_duplica_corrida_nem_saida()
    {
        var cotacao = (await Cotar()).Single();
        var a = await sut.ContratarAsync(empresa, usuario, new(cotacao.Id, "contratar1"));
        var b = await sut.ContratarAsync(empresa, usuario, new(cotacao.Id, "contratar1"));
        a.Id.Should().Be(b.Id);
        await provider.Received(1).ContratarAsync(Arg.Any<FreteSolicitacao>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        var pagamento = new PagamentoLogisticaInput("pagar1", "pix", "comprovante-demo");
        (await sut.ConfirmarPagamentoAsync(empresa, usuario, a.Id, pagamento)).Pago.Should().BeTrue();
        await sut.ConfirmarPagamentoAsync(empresa, usuario, a.Id, pagamento);
        await caixa.Received(1).AddMovimentoAsync(Arg.Is<MovimentoCaixa>(m => m.EmpresaId == empresa && m.Tipo == "saida" && m.Valor == 16));
    }

    [Fact]
    public async Task Outro_despacho_do_mesmo_pedido_e_viagem_ativa_sao_barrados()
    {
        var cotacao = (await Cotar()).Single();
        repo.TemViagemAtivaAsync(empresa, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns(true);
        await FluentActions.Invoking(() => sut.ContratarAsync(empresa, usuario, new(cotacao.Id, "1"))).Should().ThrowAsync<UseCaseValidationException>().WithMessage("*viagem*");
        repo.TemViagemAtivaAsync(empresa, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns(false);
        await sut.ContratarAsync(empresa, usuario, new(cotacao.Id, "1"));
        await FluentActions.Invoking(() => sut.ContratarAsync(empresa, usuario, new(cotacao.Id, "2"))).Should().ThrowAsync<UseCaseValidationException>().WithMessage("*já está*");
    }

    [Fact]
    public async Task Pedido_em_preparo_nao_sai_e_evento_entregue_atualiza_ciclo_canonico()
    {
        var despacho = await Contratar(); pedidos[0] = pedidos[0] with { Status = "preparando" };
        await FluentActions.Invoking(() => sut.RegistrarEventoAsync(empresa, despacho.Id, new("a_caminho", null, "sair"))).Should().ThrowAsync<UseCaseValidationException>();
        pedidos[0] = pedidos[0] with { Status = "pronto" };
        await sut.RegistrarEventoAsync(empresa, despacho.Id, new("a_caminho", null, "sair"));
        await sut.RegistrarEventoAsync(empresa, despacho.Id, new("entregue", null, "entregar"));
        await sut.RegistrarEventoAsync(empresa, despacho.Id, new("entregue", null, "entregar"));
        await repo.Received(1).AtualizarViagemAsync(Arg.Any<DespachoLogistica>(), "entregue", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Devolucao_exige_motivo_e_preserva_custo_pago()
    {
        var despacho = await Contratar();
        await sut.ConfirmarPagamentoAsync(empresa, usuario, despacho.Id, new("pg", "pix", "ref"));
        await sut.RegistrarEventoAsync(empresa, despacho.Id, new("a_caminho", null, "sair"));
        await FluentActions.Invoking(() => sut.RegistrarEventoAsync(empresa, despacho.Id, new("devolvendo", null, "voltar"))).Should().ThrowAsync<UseCaseValidationException>();
        await sut.RegistrarEventoAsync(empresa, despacho.Id, new("devolvendo", "Cliente ausente", "voltar"));
        var final = await sut.RegistrarEventoAsync(empresa, despacho.Id, new("devolvido", null, "fim"));
        final.Status.Should().Be("devolvido"); final.Pago.Should().BeTrue();
        await caixa.Received(1).AddMovimentoAsync(Arg.Any<MovimentoCaixa>());
    }

    [Fact]
    public async Task Desligar_politica_durante_cotacao_impede_contratacao()
    {
        repo.PoliticaAsync(empresa, Arg.Any<CancellationToken>()).Returns(new PoliticaLogistica { EmpresaId = empresa, Automatico = true, TetoCentavos = 2000 }, new PoliticaLogistica { EmpresaId = empresa, Automatico = false, TetoCentavos = 2000 });
        await FluentActions.Invoking(() => sut.ContratarAutomaticoAsync(empresa, usuario, new([pedidos[0].Id], false), "auto1")).Should().ThrowAsync<UseCaseValidationException>().WithMessage("*mudou*");
        await provider.DidNotReceiveWithAnyArgs().ContratarAsync(default!, default!, default);
    }

    [Fact]
    public async Task Em_maos_nao_chama_provedor_nem_cria_pagamento_zero()
    {
        var cotacao = (await sut.CotarAsync(empresa, new([pedidos[0].Id], false, 1200, true))).Single(c => c.Provedor == "em_maos");
        var despacho = await sut.ContratarAsync(empresa, usuario, new(cotacao.Id, "maos1"));
        despacho.ValorCentavos.Should().Be(0); despacho.Simulado.Should().BeFalse();
        await provider.DidNotReceiveWithAnyArgs().ContratarAsync(default!, default!, default);
        await FluentActions.Invoking(() => sut.ConfirmarPagamentoAsync(empresa, usuario, despacho.Id, new("pg", "pix", "ref"))).Should().ThrowAsync<UseCaseValidationException>();
        await caixa.DidNotReceiveWithAnyArgs().AddMovimentoAsync(default!);
    }

    [Fact]
    public async Task Saldo_insuficiente_nao_confirma_nem_grava_saida()
    {
        var despacho = await Contratar();
        repo.LimiteSaidaAsync(empresa, Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(5m);
        await FluentActions.Invoking(() => sut.ConfirmarPagamentoAsync(empresa, usuario, despacho.Id, new("pg", "dinheiro", "ref"))).Should().ThrowAsync<UseCaseValidationException>().WithMessage("*gaveta*");
        await caixa.DidNotReceiveWithAnyArgs().AddMovimentoAsync(default!);
    }

    [Fact]
    public async Task Provedor_indisponivel_vira_opcao_indisponivel_sem_apagar_contexto()
    {
        provider.CotarAsync(Arg.Any<FreteSolicitacao>(), Arg.Any<CancellationToken>()).Returns<FreteCotacao>(_ => throw new HttpRequestException("offline"));
        var cotacao = (await Cotar()).Single();
        cotacao.Indisponibilidade.Should().Contain("não respondeu");
        await FluentActions.Invoking(() => sut.ContratarAsync(empresa, usuario, new(cotacao.Id, "contratar"))).Should().ThrowAsync<UseCaseValidationException>();
    }

    private Task<IReadOnlyList<CotacaoLogisticaResult>> Cotar() => sut.CotarAsync(empresa, new([pedidos[0].Id], false));
    private async Task<DespachoLogisticaResult> Contratar() => await sut.ContratarAsync(empresa, usuario, new((await Cotar()).Single().Id, "contratar1"));
    private LogisticaPedido Pedido() => new(Guid.NewGuid(), "DEMO01", "Cliente teste", "Rua teste 10", "Centro",
        tempo.GetUtcNow().UtcDateTime, tempo.GetUtcNow().UtcDateTime.AddHours(1), "pronto", 1000, "+5500000000000", -23.56, -46.64, false);
}
