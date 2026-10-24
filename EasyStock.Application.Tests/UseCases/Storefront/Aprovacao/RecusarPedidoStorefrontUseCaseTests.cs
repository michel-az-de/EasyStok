using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.Storefront.Aprovacao;
using EasyStock.Application.UseCases.Storefront.Aprovacao.Exceptions;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Storefront.Aprovacao;

/// <summary>
/// Testes unitários do <see cref="RecusarPedidoStorefrontUseCase"/> (TASK-EZ-APROVAR-001, #1289).
///
/// <para>Cobertura:</para>
/// <list type="bullet">
///   <item>Happy path com cada <see cref="MotivoRecusa"/> — Cancelado.</item>
///   <item>#1289: pedido pago estorna na hora (chave idempotente), cobrança Estornada, vaga liberada,
///     <c>pedido.mudou_status</c> no outbox e aviso na conversa; nenhum evento sem handler.</item>
///   <item>Estorno recusado → <see cref="EstornoAutomaticoFalhouException"/> e o pedido não muda.</item>
///   <item>Tenant mismatch → <see cref="PedidoNaoEncontradoException"/>.</item>
///   <item>Status mismatch → <see cref="PedidoJaResolvidoException"/>.</item>
///   <item>MensagemCliente excedendo 280 chars → ArgumentException (422 no controller).</item>
///   <item>MotivoRecusa fora do enum (cast int) → ArgumentOutOfRangeException.</item>
///   <item>SELECT FOR UPDATE — uso dentro de transação.</item>
/// </list>
/// </summary>
public class RecusarPedidoStorefrontUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid PedidoId = Guid.NewGuid();
    private static readonly Guid UsuarioId = Guid.NewGuid();
    private const string UsuarioNome = "Babá Maria";

    private sealed class Sut
    {
        public required RecusarPedidoStorefrontUseCase UseCase { get; init; }
        public required IPedidoStorefrontRepository PedidoRepo { get; init; }
        public required IPublicadorEventoIntegracao Publicador { get; init; }
        public required IUnitOfWork UnitOfWork { get; init; }
        public required IEstornoPedidoGateway Estorno { get; init; }
        public required IVagaOcupadaRepository VagaRepo { get; init; }
        public required IConversaRepository ConversaRepo { get; init; }
        public required List<CobrancaPedido> Cobrancas { get; init; }
        public required IOperacaoEventPublisher Operacao { get; init; }
        public required ICreditoClienteComandaRepository Creditos { get; init; }
        public required IContaReceberRepository ContaReceber { get; init; }
        public required IPedidoRepository PedidoDominioRepo { get; init; }

        /// <summary>Cobrança do Mercado Pago já paga (pedido RequerAprovacao pago, aguardando a dona).</summary>
        public CobrancaPedido AdicionarCobrancaPaga(string pagamentoId, decimal valor = 120m, Guid? conversaId = null)
        {
            var agora = DateTime.UtcNow;
            var c = CobrancaPedido.CriarOnline(EmpresaId, PedidoId, valor, $"pref-{pagamentoId}",
                $"https://mp.test/{pagamentoId}", agora.AddMinutes(30), 1, agora.AddMinutes(-5), conversaId);
            c.MarcarPaga(pagamentoId, valor, "pix", agora.AddMinutes(-4));
            Cobrancas.Add(c);
            return c;
        }
    }

    private static Sut BuildSut(Pedido? pedidoStub = null)
    {
        var pedidoRepo = Substitute.For<IPedidoStorefrontRepository>();
        pedidoRepo.GetForUpdateAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(pedidoStub);

        var publicador = Substitute.For<IPublicadorEventoIntegracao>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<RecusarPedidoStorefrontResult>>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var action = callInfo.Arg<Func<CancellationToken, Task<RecusarPedidoStorefrontResult>>>();
                return action(callInfo.Arg<CancellationToken>());
            });

        var estorno = Substitute.For<IEstornoPedidoGateway>();
        estorno.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(EstornoPedidoResult.Ok("estorno-1"));
        var vagaRepo = Substitute.For<IVagaOcupadaRepository>();
        var conversaRepo = Substitute.For<IConversaRepository>();
        var cobrancas = new List<CobrancaPedido>();
        var cobrancaRepo = Substitute.For<ICobrancaPedidoRepository>();
        cobrancaRepo.ListarDoPedidoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => cobrancas.ToList());

        var aviso = new AvisoCobrancaConversa(conversaRepo, new ResolvedorCanal(Array.Empty<ICanalMensageria>()), uow,
            NullLogger<AvisoCobrancaConversa>.Instance);

        var operacao = Substitute.For<IOperacaoEventPublisher>();
        var creditos = Substitute.For<ICreditoClienteComandaRepository>();
        var contaReceber = Substitute.For<IContaReceberRepository>();
        var pedidoDominioRepo = Substitute.For<IPedidoRepository>();
        var saldos = new SaldoFinanceiroPedido(pedidoDominioRepo, Substitute.For<IEstornoOnlineRepository>(), creditos);
        var efeitos = new EfeitosCancelamentoPedido(contaReceber, vagaRepo, NullLogger<EfeitosCancelamentoPedido>.Instance, saldos, creditos);
        var useCase = new RecusarPedidoStorefrontUseCase(
            pedidoRepo, cobrancaRepo, estorno, efeitos, aviso, publicador, uow,
            NullLogger<RecusarPedidoStorefrontUseCase>.Instance, operacao, saldos);
        return new Sut
        {
            UseCase = useCase,
            PedidoRepo = pedidoRepo,
            Publicador = publicador,
            UnitOfWork = uow,
            Estorno = estorno,
            VagaRepo = vagaRepo,
            ConversaRepo = conversaRepo,
            Cobrancas = cobrancas,
            Operacao = operacao,
            Creditos = creditos,
            ContaReceber = contaReceber,
            PedidoDominioRepo = pedidoDominioRepo,
        };
    }

    private static Pedido PedidoAguardandoAprovacaoBaba(Guid? empresaId = null) => new()
    {
        Id = PedidoId,
        EmpresaId = empresaId ?? EmpresaId,
        ClienteId = Guid.NewGuid(),
        ClienteNome = "Cliente Teste",
        ClienteTelefone = "11999990000",
        Status = StatusPedidoMapper.AguardandoAprovacaoBaba,
        Total = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(120m),
        Origem = "storefront",
        CriadoEm = DateTime.UtcNow,
        AlteradoEm = DateTime.UtcNow,
    };

    private static RecusarPedidoStorefrontInput InputValido(
        Guid? empresaId = null,
        MotivoRecusa motivo = MotivoRecusa.EstoqueInsuficiente,
        string? mensagem = "Item esgotou. Posso te oferecer outra opção?",
        NivelAcesso nivel = NivelAcesso.Operador) =>
        new(
            PedidoId: PedidoId,
            EmpresaId: empresaId ?? EmpresaId,
            UsuarioId: UsuarioId,
            Motivo: motivo,
            MensagemCliente: mensagem,
            UsuarioNome: UsuarioNome,
            NivelSolicitante: nivel);

    // ── Happy path ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(MotivoRecusa.EstoqueInsuficiente, "estoque_insuficiente")]
    [InlineData(MotivoRecusa.Operacional, "operacional")]
    [InlineData(MotivoRecusa.Outro, "outro")]
    public async Task Recusar_pedido_AguardandoAprovacaoBaba_com_motivo_valido_retorna_Cancelado(
        MotivoRecusa motivo, string motivoCanonical)
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        var sut = BuildSut(pedido);

        var result = await sut.UseCase.ExecuteAsync(InputValido(motivo: motivo));

        result.Status.Should().Be(StatusPedidoMapper.Cancelado);
        result.Motivo.Should().Be(motivoCanonical);
        result.VagaLiberada.Should().BeTrue();

        pedido.Status.Should().Be(StatusPedidoMapper.Cancelado);
        pedido.CanceladoEm.Should().NotBeNull();
        pedido.RecusadoEm.Should().NotBeNull();
        pedido.RecusadoPorUsuarioId.Should().Be(UsuarioId);
        pedido.MotivoRecusa.Should().Be(motivoCanonical);
    }

    // ── #1289: recusa de pedido já pago ────────────────────────────────────

    [Fact]
    public async Task Recusar_pedido_pago_estorna_o_pagamento_com_chave_idempotente()
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        var sut = BuildSut(pedido);
        var cobranca = sut.AdicionarCobrancaPaga("pay-1");

        var result = await sut.UseCase.ExecuteAsync(InputValido(nivel: NivelAcesso.Gerente));

        await sut.Estorno.Received(1).EstornarAsync("pay-1", 120m, $"recusa-{PedidoId}-pay-1", Arg.Any<CancellationToken>());
        cobranca.Status.Should().Be(StatusCobrancaPedido.Estornada);
        cobranca.Motivo.Should().Contain("recusa").And.Contain("pay-1").And.Contain("estorno-1");
        result.Refund.Enfileirado.Should().BeTrue("o estorno foi aceito pelo gateway");
    }

    [Fact]
    public async Task Recusar_pedido_sem_pagamento_nao_estorna()
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        var sut = BuildSut(pedido);

        var result = await sut.UseCase.ExecuteAsync(InputValido());

        await sut.Estorno.DidNotReceive().EstornarAsync(
            Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        result.Refund.Enfileirado.Should().BeFalse("não havia pagamento a devolver");
    }

    [Fact]
    public async Task Recusar_estorno_recusado_lanca_e_nao_cancela_o_pedido()
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        var sut = BuildSut(pedido);
        var cobranca = sut.AdicionarCobrancaPaga("pay-1");
        sut.Estorno.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(EstornoPedidoResult.Falha("estorno_recusado"));

        var act = async () => await sut.UseCase.ExecuteAsync(InputValido(nivel: NivelAcesso.Gerente));

        await act.Should().ThrowAsync<EstornoAutomaticoFalhouException>();
        pedido.Status.Should().Be(StatusPedidoMapper.AguardandoAprovacaoBaba, "sem estorno a dona decide de novo");
        cobranca.Status.Should().Be(StatusCobrancaPedido.Paga);
        await sut.PedidoRepo.DidNotReceive().UpdateAsync(Arg.Any<Pedido>(), Arg.Any<CancellationToken>());
        await sut.VagaRepo.DidNotReceive().LiberarPorPedidoAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recusar_libera_a_vaga_na_mesma_transacao()
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        var sut = BuildSut(pedido);

        await sut.UseCase.ExecuteAsync(InputValido(motivo: MotivoRecusa.Operacional));

        await sut.VagaRepo.Received(1).LiberarPorPedidoAsync(
            PedidoId, Arg.Is<string>(m => m.Contains("recusado_baba") && m.Contains("operacional")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recusar_publica_mudou_status_e_nenhum_evento_sem_handler()
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        var sut = BuildSut(pedido);

        await sut.UseCase.ExecuteAsync(InputValido());

        await sut.Publicador.Received(1).PublicarAsync(
            EmpresaId, "pedido.mudou_status", "pedido", PedidoId,
            Arg.Is<PedidoMudouStatusEvent>(e =>
                e.StatusAntigo == StatusPedidoMapper.AguardandoAprovacaoBaba
                && e.StatusNovo == StatusPedidoMapper.Cancelado
                && e.UsuarioId == UsuarioId),
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        sut.Publicador.ReceivedCalls()
            .Select(c => c.GetArguments()[1] as string)
            .Should().OnlyContain(tipo => tipo == "pedido.mudou_status",
                "storefront.pedido.cancelado, storefront.pagamento.estorno_solicitado e " +
                "storefront.pedido.recusado_notificar_cliente não têm handler e ficariam no outbox para sempre");
    }

    [Fact]
    public async Task Recusar_pedido_pago_avisa_o_cliente_na_conversa_da_cobranca()
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        var sut = BuildSut(pedido);
        var conversaId = Guid.NewGuid();
        sut.AdicionarCobrancaPaga("pay-1", conversaId: conversaId);

        await sut.UseCase.ExecuteAsync(InputValido(nivel: NivelAcesso.Gerente));

        await sut.ConversaRepo.Received(1).ObterPorIdAsync(EmpresaId, conversaId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recusar_chama_GetForUpdateAsync_dentro_de_transacao()
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        var sut = BuildSut(pedido);

        await sut.UseCase.ExecuteAsync(InputValido());

        await sut.PedidoRepo.Received(1).GetForUpdateAsync(EmpresaId, PedidoId, Arg.Any<CancellationToken>());
    }

    // #1265-8: a recusa cancela o pedido; a operação move o card sem recarregar, depois do commit.
    [Fact]
    public async Task Recusar_publica_pedido_mudou_status_na_operacao_depois_do_commit()
    {
        var sut = BuildSut(PedidoAguardandoAprovacaoBaba());

        await sut.UseCase.ExecuteAsync(InputValido());

        await sut.Operacao.Received(1).PublicarAsync(EventosOperacao.PedidoMudouStatus, EmpresaId,
            Arg.Is<PedidoMudouStatusOperacao>(e => e.PedidoId == PedidoId
                && e.StatusAntigo == StatusPedidoMapper.AguardandoAprovacaoBaba && e.StatusNovo == StatusPedidoMapper.Cancelado),
            Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            sut.UnitOfWork.ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<RecusarPedidoStorefrontResult>>>(), Arg.Any<CancellationToken>());
            sut.Operacao.PublicarAsync(EventosOperacao.PedidoMudouStatus, EmpresaId, Arg.Any<object>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Recusar_salva_PedidoEvento_audit_trail_com_motivo_e_mensagem()
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        var sut = BuildSut(pedido);

        await sut.UseCase.ExecuteAsync(InputValido(mensagem: "Esgotou"));

        await sut.PedidoRepo.Received(1).AddEventoAsync(
            Arg.Is<PedidoEvento>(e =>
                e.PedidoId == PedidoId
                && e.Tipo == "recusado_storefront"
                && e.StatusAntigo == StatusPedidoMapper.AguardandoAprovacaoBaba
                && e.StatusNovo == StatusPedidoMapper.Cancelado
                && e.UsuarioId == UsuarioId
                && e.Detalhes!.Contains("Esgotou")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recusar_mensagem_cliente_nula_omite_mensagem_no_audit_trail()
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        var sut = BuildSut(pedido);

        await sut.UseCase.ExecuteAsync(InputValido(mensagem: null));

        await sut.PedidoRepo.Received(1).AddEventoAsync(
            Arg.Is<PedidoEvento>(e => e.Detalhes == "estoque_insuficiente"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Operador_nao_recusa_com_pagamento_online_ou_manual_parcial(bool online)
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        var sut = BuildSut(pedido);
        if (online) sut.AdicionarCobrancaPaga("pay-parcial", 10m);
        else pedido.Pagamentos.Add(new PedidoPagamento { Valor = 10m, Metodo = "dinheiro" });

        await FluentActions.Invoking(() => sut.UseCase.ExecuteAsync(InputValido()))
            .Should().ThrowAsync<UnauthorizedAccessException>();

        pedido.Status.Should().Be(StatusPedidoMapper.AguardandoAprovacaoBaba);
        await sut.Estorno.DidNotReceiveWithAnyArgs().EstornarAsync(default!, default, default!, default);
        await sut.PedidoRepo.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
        await sut.Publicador.DidNotReceiveWithAnyArgs().PublicarAsync(default, default!, default!, default, default(object)!, default, default, default, default);
    }

    // ── PEDIDO-02 (banca 23/10): a recusa aplica os mesmos efeitos do cancelamento ──────

    [Fact]
    public async Task Recusar_devolve_o_cupom_usado_e_cancela_a_conta_a_receber()
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        pedido.Pagamentos.Add(new PedidoPagamento { Valor = 80m, Metodo = "dinheiro" });
        var sut = BuildSut(pedido);
        sut.Creditos.TotaisDoPedidoAsync(EmpresaId, PedidoId, Arg.Any<CancellationToken>()).Returns((0m, 20m));

        await sut.UseCase.ExecuteAsync(InputValido(nivel: NivelAcesso.Gerente));

        await sut.Creditos.Received(1).AddAsync(
            Arg.Is<CreditoClienteComanda>(c => c.Valor == 20m && c.ClienteId == pedido.ClienteId && c.OperacaoId == PedidoId),
            Arg.Any<CancellationToken>());
        await sut.ContaReceber.Received(1).GetByOrigemAsync(EmpresaId, OrigemContaFinanceira.Pedido, PedidoId);
    }

    [Fact]
    public async Task Recusar_cancela_a_cobranca_pendente_na_entrega()
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        var sut = BuildSut(pedido);
        var naEntrega = CobrancaPedido.CriarNaEntrega(EmpresaId, PedidoId, 100m, DateTime.UtcNow.AddMinutes(-3));
        sut.Cobrancas.Add(naEntrega);

        await sut.UseCase.ExecuteAsync(InputValido());

        naEntrega.Status.Should().Be(StatusCobrancaPedido.Cancelada, "pedido recusado nao tem mais nada a cobrar na porta");
    }

    [Fact]
    public async Task Recusar_registra_a_devolucao_a_fazer_do_dinheiro_recebido_por_fora_do_estorno_online()
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        pedido.Pagamentos.Add(new PedidoPagamento { Valor = 30m, Metodo = "dinheiro" });
        var sut = BuildSut(pedido);

        await sut.UseCase.ExecuteAsync(InputValido(nivel: NivelAcesso.Gerente));

        await sut.PedidoRepo.Received(1).AddEventoAsync(
            Arg.Is<PedidoEvento>(e => e.Tipo == "devolucao_a_fazer" && e.Detalhes!.Contains(30m.ToString("C", Cultura.PtBr))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recusar_desconta_da_devolucao_a_fazer_o_que_ja_foi_devolvido_antes()
    {
        // Revisao da PR: R$ 30 em dinheiro (R$ 20 ja devolvidos antes) + R$ 120 online estornados agora: sobram R$ 10.
        var pedido = PedidoAguardandoAprovacaoBaba();
        pedido.Pagamentos.Add(new PedidoPagamento { Id = Guid.NewGuid(), Valor = 30m, Metodo = "dinheiro" });
        pedido.Pagamentos.Add(new PedidoPagamento { Id = Guid.NewGuid(), Valor = 120m, Metodo = "pix" });
        var sut = BuildSut(pedido);
        sut.AdicionarCobrancaPaga("pay-1");
        sut.PedidoDominioRepo.ListarEstornosManuaisAsync(EmpresaId, PedidoId, Arg.Any<CancellationToken>())
            .Returns([new PedidoEstornoManual { Id = Guid.NewGuid(), EmpresaId = EmpresaId, PedidoId = PedidoId,
                PagamentoId = pedido.Pagamentos.First().Id, Valor = 20m, Metodo = "dinheiro", Motivo = "parcial" }]);

        await sut.UseCase.ExecuteAsync(InputValido(nivel: NivelAcesso.Gerente));

        await sut.PedidoRepo.Received(1).AddEventoAsync(
            Arg.Is<PedidoEvento>(e => e.Tipo == "devolucao_a_fazer" && e.Detalhes!.Contains(10m.ToString("C", Cultura.PtBr))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recusar_pago_so_online_e_estornado_nao_registra_devolucao_a_fazer()
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        pedido.Pagamentos.Add(new PedidoPagamento { Valor = 120m, Metodo = "pix" });
        var sut = BuildSut(pedido);
        sut.AdicionarCobrancaPaga("pay-1");

        await sut.UseCase.ExecuteAsync(InputValido(nivel: NivelAcesso.Gerente));

        await sut.PedidoRepo.DidNotReceive().AddEventoAsync(
            Arg.Is<PedidoEvento>(e => e.Tipo == "devolucao_a_fazer"), Arg.Any<CancellationToken>());
    }

    // ── Falhas ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Recusar_pedido_inexistente_lanca_PedidoNaoEncontrado()
    {
        var sut = BuildSut(pedidoStub: null);

        var act = async () => await sut.UseCase.ExecuteAsync(InputValido());

        await act.Should().ThrowAsync<PedidoNaoEncontradoException>();
    }

    [Fact]
    public async Task Recusar_pedido_de_outro_tenant_lanca_PedidoNaoEncontrado()
    {
        var pedido = PedidoAguardandoAprovacaoBaba(empresaId: Guid.NewGuid());
        var sut = BuildSut(pedido);

        var act = async () => await sut.UseCase.ExecuteAsync(InputValido(empresaId: EmpresaId));

        await act.Should().ThrowAsync<PedidoNaoEncontradoException>();
    }

    [Theory]
    [InlineData("cancelado")]
    [InlineData("aprovado_baba")]
    [InlineData("aguardando_pagamento")]
    [InlineData("preparando")]
    public async Task Recusar_pedido_em_outro_status_lanca_PedidoJaResolvido(string statusAtual)
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        pedido.Status = statusAtual;
        var sut = BuildSut(pedido);

        var act = async () => await sut.UseCase.ExecuteAsync(InputValido());

        await act.Should().ThrowAsync<PedidoJaResolvidoException>();
    }

    [Fact]
    public async Task Recusar_mensagem_acima_280_chars_lanca_ArgumentException()
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        var sut = BuildSut(pedido);
        var mensagemGigante = new string('a', 281);

        var act = async () => await sut.UseCase.ExecuteAsync(InputValido(mensagem: mensagemGigante));

        await act.Should().ThrowAsync<ArgumentException>()
            .Where(ex => ex.Message.Contains("280"));
    }

    [Fact]
    public async Task Recusar_motivo_int_invalido_lanca_ArgumentOutOfRange()
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        var sut = BuildSut(pedido);
        var motivoInvalido = (MotivoRecusa)999;

        var act = async () => await sut.UseCase.ExecuteAsync(InputValido(motivo: motivoInvalido));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Recusar_input_null_lanca_ArgumentNullException()
    {
        var sut = BuildSut();
        var act = async () => await sut.UseCase.ExecuteAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task Recusar_input_com_Guid_Empty_lanca_ArgumentException(
        bool pedidoEmpty, bool empresaEmpty, bool usuarioEmpty)
    {
        var sut = BuildSut();
        var input = new RecusarPedidoStorefrontInput(
            PedidoId: pedidoEmpty ? Guid.Empty : Guid.NewGuid(),
            EmpresaId: empresaEmpty ? Guid.Empty : Guid.NewGuid(),
            UsuarioId: usuarioEmpty ? Guid.Empty : Guid.NewGuid(),
            Motivo: MotivoRecusa.Outro);

        var act = async () => await sut.UseCase.ExecuteAsync(input);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
