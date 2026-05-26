using EasyStock.Application.Events.Storefront;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Aprovacao;
using EasyStock.Application.UseCases.Storefront.Aprovacao.Exceptions;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Events.Storefront;
using EasyStock.Domain.Sales;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace EasyStock.Application.Tests.UseCases.Storefront.Aprovacao;

/// <summary>
/// Testes unitários do <see cref="RecusarPedidoStorefrontUseCase"/> (TASK-EZ-APROVAR-001).
///
/// <para>Cobertura mínima esperada (DoD ≥ 90%):</para>
/// <list type="bullet">
///   <item>Happy path com cada <see cref="MotivoRecusa"/> — Cancelado + 3 eventos enfileirados.</item>
///   <item>Recusa: PedidoCanceladoEvent (handler libera vaga), EstornarPagamentoAutomatico, NotificarCliente.</item>
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

    private sealed record Sut(
        RecusarPedidoStorefrontUseCase UseCase,
        IPedidoStorefrontRepository PedidoRepo,
        IPublicadorEventoIntegracao Publicador,
        IUnitOfWork UnitOfWork);

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

        var useCase = new RecusarPedidoStorefrontUseCase(
            pedidoRepo, publicador, uow, NullLogger<RecusarPedidoStorefrontUseCase>.Instance);
        return new Sut(useCase, pedidoRepo, publicador, uow);
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
        result.Refund.Enfileirado.Should().BeTrue();
        result.Refund.Evento.Should().Be(nameof(EstornarPagamentoAutomaticoEvent));
        result.NotificacaoCliente.Enfileirada.Should().BeTrue();

        pedido.Status.Should().Be(StatusPedidoMapper.Cancelado);
        pedido.CanceladoEm.Should().NotBeNull();
        pedido.RecusadoEm.Should().NotBeNull();
        pedido.RecusadoPorUsuarioId.Should().Be(UsuarioId);
        pedido.MotivoRecusa.Should().Be(motivoCanonical);
    }

    [Fact]
    public async Task Recusar_enfileira_3_eventos_na_MESMA_transacao()
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        var sut = BuildSut(pedido);

        await sut.UseCase.ExecuteAsync(InputValido());

        // 1. PedidoCanceladoEvent
        await sut.Publicador.Received(1).PublicarAsync(
            EmpresaId, "storefront.pedido.cancelado", "pedido", PedidoId,
            Arg.Any<PedidoCanceladoEvent>(),
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());

        // 2. EstornarPagamentoAutomaticoEvent
        await sut.Publicador.Received(1).PublicarAsync(
            EmpresaId, "storefront.pagamento.estorno_solicitado", "pedido", PedidoId,
            Arg.Any<EstornarPagamentoAutomaticoEvent>(),
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());

        // 3. NotificarClientePagamentoRecusadoEvent
        await sut.Publicador.Received(1).PublicarAsync(
            EmpresaId, "storefront.pedido.recusado_notificar_cliente", "pedido", PedidoId,
            Arg.Any<NotificarClientePagamentoRecusadoEvent>(),
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());

        // Todos eventos rodaram DENTRO da mesma transação
        await sut.UnitOfWork.Received(1).ExecuteInTransactionAsync(
            Arg.Any<Func<CancellationToken, Task<RecusarPedidoStorefrontResult>>>(),
            Arg.Any<CancellationToken>());
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
    public async Task Recusar_PedidoCanceladoEvent_motivo_inclui_recusado_baba_prefix()
    {
        var pedido = PedidoAguardandoAprovacaoBaba();
        var sut = BuildSut(pedido);

        await sut.UseCase.ExecuteAsync(InputValido(motivo: MotivoRecusa.Operacional));

        await sut.Publicador.Received(1).PublicarAsync(
            Arg.Any<Guid>(), "storefront.pedido.cancelado", Arg.Any<string>(), Arg.Any<Guid>(),
            Arg.Is<PedidoCanceladoEvent>(e => e.Motivo.Contains("recusado_baba") && e.Motivo.Contains("operacional")),
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
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
