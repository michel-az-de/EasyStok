using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Events.Storefront.Handlers;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.Storefront.Aprovacao;
using EasyStock.Application.UseCases.Storefront.Aprovacao.Exceptions;
using EasyStock.Domain.Entities.Pagamentos;
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
        pedidoRepo.GetForUpdateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
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
        var liberarVaga = new LiberarVagaOnPedidoCanceladoHandler(vagaRepo,
            NullLogger<LiberarVagaOnPedidoCanceladoHandler>.Instance);

        var useCase = new RecusarPedidoStorefrontUseCase(
            pedidoRepo, cobrancaRepo, estorno, liberarVaga, aviso, publicador, uow,
            NullLogger<RecusarPedidoStorefrontUseCase>.Instance);
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
        string? mensagem = "Item esgotou. Posso te oferecer outra opção?") =>
        new(
            PedidoId: PedidoId,
            EmpresaId: empresaId ?? EmpresaId,
            UsuarioId: UsuarioId,
            Motivo: motivo,
            MensagemCliente: mensagem,
            UsuarioNome: UsuarioNome);

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

        var result = await sut.UseCase.ExecuteAsync(InputValido());

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

        var act = async () => await sut.UseCase.ExecuteAsync(InputValido());

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

        await sut.UseCase.ExecuteAsync(InputValido());

        await sut.ConversaRepo.Received(1).ObterPorIdAsync(EmpresaId, conversaId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recusar_chama_GetForUpdateAsync_dentro_de_transacao()
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        var sut = BuildSut(pedido);

        await sut.UseCase.ExecuteAsync(InputValido());

        await sut.PedidoRepo.Received(1).GetForUpdateAsync(PedidoId, Arg.Any<CancellationToken>());
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
