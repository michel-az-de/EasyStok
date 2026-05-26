using EasyStock.Application.Events.Storefront;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Aprovacao;
using EasyStock.Application.UseCases.Storefront.Aprovacao.Exceptions;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Sales;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace EasyStock.Application.Tests.UseCases.Storefront.Aprovacao;

/// <summary>
/// Testes unitários do <see cref="AprovarPedidoStorefrontUseCase"/> (TASK-EZ-APROVAR-001).
///
/// <para>Cobertura mínima esperada (DoD ≥ 90%):</para>
/// <list type="bullet">
///   <item>Happy path — Pedido transita AguardandoAprovacaoBaba → AprovadoBaba + Outbox enfileirado.</item>
///   <item>Tenant mismatch (EmpresaId divergente) → <see cref="PedidoNaoEncontradoException"/>.</item>
///   <item>Pedido inexistente → <see cref="PedidoNaoEncontradoException"/>.</item>
///   <item>Status mismatch (já aprovado/recusado/cancelado) → <see cref="PedidoJaResolvidoException"/>.</item>
///   <item>Validação de input — Guid.Empty em campos obrigatórios.</item>
///   <item>SELECT FOR UPDATE — uso case roda DENTRO de <see cref="IUnitOfWork.ExecuteInTransactionAsync"/>.</item>
///   <item>PedidoEvento (audit trail) salvo na MESMA TX.</item>
///   <item>Observações: append em campo existente vs primeiro preenchimento.</item>
/// </list>
/// </summary>
public class AprovarPedidoStorefrontUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid PedidoId = Guid.NewGuid();
    private static readonly Guid UsuarioId = Guid.NewGuid();
    private const string UsuarioNome = "Babá Maria";

    // ── Fixture ────────────────────────────────────────────────────────────

    private sealed record Sut(
        AprovarPedidoStorefrontUseCase UseCase,
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
        // Mock: ExecuteInTransactionAsync<T> simplesmente invoca o delegate inline.
        uow.ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<AprovarPedidoStorefrontResult>>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var action = callInfo.Arg<Func<CancellationToken, Task<AprovarPedidoStorefrontResult>>>();
                return action(callInfo.Arg<CancellationToken>());
            });

        var useCase = new AprovarPedidoStorefrontUseCase(
            pedidoRepo, publicador, uow, NullLogger<AprovarPedidoStorefrontUseCase>.Instance);

        return new Sut(useCase, pedidoRepo, publicador, uow);
    }

    private static Pedido PedidoNoStatus(string status, Guid empresaId)
    {
        var agora = DateTime.UtcNow;
        return new Pedido
        {
            Id = PedidoId,
            EmpresaId = empresaId,
            ClienteId = Guid.NewGuid(),
            ClienteNome = "Cliente Teste",
            ClienteTelefone = "11999990000",
            Status = status,
            Total = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(120m),
            Origem = "storefront",
            CriadoEm = agora,
            AlteradoEm = agora,
        };
    }

    private static AprovarPedidoStorefrontInput InputValido(
        Guid? empresaId = null,
        Guid? pedidoId = null,
        string? observacoes = null) =>
        new(
            PedidoId: pedidoId ?? PedidoId,
            EmpresaId: empresaId ?? EmpresaId,
            UsuarioId: UsuarioId,
            UsuarioNome: UsuarioNome,
            Observacoes: observacoes);

    // ── Happy path ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Aprovar_pedido_AguardandoAprovacaoBaba_retorna_AprovadoBaba()
    {
        var pedido = PedidoNoStatus(StatusPedidoMapper.AguardandoAprovacaoBaba, EmpresaId);
        var sut = BuildSut(pedido);

        var result = await sut.UseCase.ExecuteAsync(InputValido());

        result.Should().NotBeNull();
        result.PedidoId.Should().Be(PedidoId);
        result.Status.Should().Be(StatusPedidoMapper.AprovadoBaba);
        result.AprovadoEm.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.AprovadoPor.Should().Be(UsuarioNome);
        result.NotificacaoCliente.Enfileirada.Should().BeTrue();
        result.NotificacaoCliente.Evento.Should().Be(nameof(NotificarClientePedidoAprovadoEvent));

        pedido.Status.Should().Be(StatusPedidoMapper.AprovadoBaba);
        pedido.AprovadoEm.Should().NotBeNull();
        pedido.AprovadoPorUsuarioId.Should().Be(UsuarioId);
    }

    [Fact]
    public async Task Aprovar_chama_GetForUpdateAsync_dentro_de_transacao_explicita()
    {
        var pedido = PedidoNoStatus(StatusPedidoMapper.AguardandoAprovacaoBaba, EmpresaId);
        var sut = BuildSut(pedido);

        await sut.UseCase.ExecuteAsync(InputValido());

        await sut.UnitOfWork.Received(1).ExecuteInTransactionAsync(
            Arg.Any<Func<CancellationToken, Task<AprovarPedidoStorefrontResult>>>(),
            Arg.Any<CancellationToken>());
        await sut.PedidoRepo.Received(1).GetForUpdateAsync(PedidoId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Aprovar_enfileira_evento_NotificarClientePedidoAprovado_no_outbox()
    {
        var pedido = PedidoNoStatus(StatusPedidoMapper.AguardandoAprovacaoBaba, EmpresaId);
        var sut = BuildSut(pedido);

        await sut.UseCase.ExecuteAsync(InputValido());

        await sut.Publicador.Received(1).PublicarAsync(
            EmpresaId,
            "storefront.pedido.aprovado",
            "pedido",
            PedidoId,
            Arg.Any<NotificarClientePedidoAprovadoEvent>(),
            Arg.Any<int>(),
            Arg.Any<string>(),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Aprovar_salva_PedidoEvento_audit_trail()
    {
        var pedido = PedidoNoStatus(StatusPedidoMapper.AguardandoAprovacaoBaba, EmpresaId);
        var sut = BuildSut(pedido);

        await sut.UseCase.ExecuteAsync(InputValido(observacoes: "Estoque ok, produção iniciada."));

        await sut.PedidoRepo.Received(1).AddEventoAsync(
            Arg.Is<PedidoEvento>(e =>
                e.PedidoId == PedidoId
                && e.Tipo == "aprovado_storefront"
                && e.StatusAntigo == StatusPedidoMapper.AguardandoAprovacaoBaba
                && e.StatusNovo == StatusPedidoMapper.AprovadoBaba
                && e.UsuarioId == UsuarioId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Aprovar_com_observacoes_anexa_em_Observacoes_existentes()
    {
        var pedido = PedidoNoStatus(StatusPedidoMapper.AguardandoAprovacaoBaba, EmpresaId);
        pedido.Observacoes = "Anterior";
        var sut = BuildSut(pedido);

        await sut.UseCase.ExecuteAsync(InputValido(observacoes: "Aprovada"));

        pedido.Observacoes.Should().Contain("Anterior").And.Contain("Aprovada");
    }

    [Fact]
    public async Task Aprovar_sem_observacoes_no_pedido_e_input_com_observacoes_preenche()
    {
        var pedido = PedidoNoStatus(StatusPedidoMapper.AguardandoAprovacaoBaba, EmpresaId);
        pedido.Observacoes = null;
        var sut = BuildSut(pedido);

        await sut.UseCase.ExecuteAsync(InputValido(observacoes: "Tudo certo"));

        pedido.Observacoes.Should().Be("Tudo certo");
    }

    // ── Falhas ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Aprovar_pedido_inexistente_lanca_PedidoNaoEncontrado()
    {
        var sut = BuildSut(pedidoStub: null);

        var act = async () => await sut.UseCase.ExecuteAsync(InputValido());

        await act.Should().ThrowAsync<PedidoNaoEncontradoException>()
            .Where(ex => ex.PedidoId == PedidoId);
    }

    [Fact]
    public async Task Aprovar_pedido_de_outro_tenant_lanca_PedidoNaoEncontrado_nao_403()
    {
        // Pedido existe mas EmpresaId != input.EmpresaId → 404 (não vaza existência).
        var pedido = PedidoNoStatus(StatusPedidoMapper.AguardandoAprovacaoBaba, Guid.NewGuid());
        var sut = BuildSut(pedido);

        var act = async () => await sut.UseCase.ExecuteAsync(InputValido(empresaId: EmpresaId));

        await act.Should().ThrowAsync<PedidoNaoEncontradoException>();
    }

    [Theory]
    [InlineData("aprovado_baba")]
    [InlineData("cancelado")]
    [InlineData("aguardando_pagamento")]
    [InlineData("preparando")]
    [InlineData("pronto")]
    [InlineData("entregue")]
    public async Task Aprovar_pedido_em_outro_status_lanca_PedidoJaResolvido_com_StatusAtual(string statusAtual)
    {
        var pedido = PedidoNoStatus(statusAtual, EmpresaId);
        var sut = BuildSut(pedido);

        var act = async () => await sut.UseCase.ExecuteAsync(InputValido());

        var ex = await act.Should().ThrowAsync<PedidoJaResolvidoException>();
        ex.Which.StatusAtualString.Should().Be(statusAtual);
        ex.Which.PedidoId.Should().Be(PedidoId);
    }

    [Fact]
    public async Task Aprovar_input_null_lanca_ArgumentNullException()
    {
        var sut = BuildSut();
        var act = async () => await sut.UseCase.ExecuteAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(true, false, false, "PedidoId")]
    [InlineData(false, true, false, "EmpresaId")]
    [InlineData(false, false, true, "UsuarioId")]
    public async Task Aprovar_input_com_Guid_Empty_lanca_ArgumentException(
        bool pedidoEmpty, bool empresaEmpty, bool usuarioEmpty, string campo)
    {
        var sut = BuildSut();
        var input = new AprovarPedidoStorefrontInput(
            PedidoId: pedidoEmpty ? Guid.Empty : Guid.NewGuid(),
            EmpresaId: empresaEmpty ? Guid.Empty : Guid.NewGuid(),
            UsuarioId: usuarioEmpty ? Guid.Empty : Guid.NewGuid());

        var act = async () => await sut.UseCase.ExecuteAsync(input);

        await act.Should().ThrowAsync<ArgumentException>()
            .Where(ex => ex.Message.Contains(campo));
    }

    [Fact]
    public async Task Aprovar_quando_publicador_falha_propaga_exception_e_nao_completa_TX()
    {
        var pedido = PedidoNoStatus(StatusPedidoMapper.AguardandoAprovacaoBaba, EmpresaId);
        var sut = BuildSut(pedido);
        sut.Publicador
            .PublicarAsync(
                Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(),
                Arg.Any<NotificarClientePedidoAprovadoEvent>(),
                Arg.Any<int>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("outbox down"));

        var act = async () => await sut.UseCase.ExecuteAsync(InputValido());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("outbox down");
    }
}
