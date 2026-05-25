using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Checkout;
using EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;
using EasyStock.Domain.Entities.Storefront;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace EasyStock.Application.Tests.UseCases.Storefront.Checkout.Idempotency;

public class CheckoutIdempotencyServiceTests
{
    private static readonly Guid Key = Guid.NewGuid();
    private const string Hash = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2";
    private const string HashDiferente = "f6e5d4c3b2a1f6e5d4c3b2a1f6e5d4c3b2a1f6e5d4c3b2a1f6e5d4c3b2a1f6e5";
    private static readonly Guid FaturaId = Guid.NewGuid();
    private const string InitPoint = "https://mp.com/checkout/pref-xyz";

    private static CheckoutIdempotencyService BuildService(ICheckoutIdempotencyRepository repo) =>
        new(repo, NullLogger<CheckoutIdempotencyService>.Instance);

    // ── TentarReservarAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task TentarReservar_KeyNovo_ReservaERetornaNull()
    {
        var repo = Substitute.For<ICheckoutIdempotencyRepository>();
        repo.GetByKeyAsync(Key, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CheckoutIdempotency>() as IReadOnlyList<CheckoutIdempotency>);

        var proposta = CheckoutIdempotency.Criar(Key, Hash);
        repo.TentarReservarAsync(Arg.Any<CheckoutIdempotency>(), Arg.Any<CancellationToken>())
            .Returns((true, proposta));

        var svc = BuildService(repo);

        var result = await svc.TentarReservarAsync(Key, Hash);

        result.Should().BeNull();
        await repo.Received(1).TentarReservarAsync(Arg.Any<CheckoutIdempotency>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TentarReservar_ReplayExato_RetornaDTO()
    {
        var registro = CheckoutIdempotency.Criar(Key, Hash);
        registro.VincularFatura(FaturaId, InitPoint);

        var repo = Substitute.For<ICheckoutIdempotencyRepository>();
        repo.GetByKeyAsync(Key, Arg.Any<CancellationToken>())
            .Returns(new List<CheckoutIdempotency> { registro } as IReadOnlyList<CheckoutIdempotency>);

        var svc = BuildService(repo);

        var result = await svc.TentarReservarAsync(Key, Hash);

        result.Should().NotBeNull();
        result!.PedidoId.Should().Be(FaturaId);
        result.InitPointUrl.Should().Be(InitPoint);
        await repo.DidNotReceive().TentarReservarAsync(Arg.Any<CheckoutIdempotency>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TentarReservar_HashMismatch_LancaIdempotencyMismatchException()
    {
        // Key existe com hash diferente
        var registroExistente = CheckoutIdempotency.Criar(Key, Hash);
        registroExistente.VincularFatura(FaturaId, InitPoint);

        var repo = Substitute.For<ICheckoutIdempotencyRepository>();
        repo.GetByKeyAsync(Key, Arg.Any<CancellationToken>())
            .Returns(new List<CheckoutIdempotency> { registroExistente } as IReadOnlyList<CheckoutIdempotency>);

        var svc = BuildService(repo);

        await svc.Invoking(s => s.TentarReservarAsync(Key, HashDiferente))
            .Should().ThrowAsync<IdempotencyMismatchException>();
    }

    [Fact]
    public async Task TentarReservar_InFlight_SemFaturaId_RetornaNull()
    {
        // Registro existe mas FaturaId ainda não vinculado (Fase 3 não concluiu)
        var registroSemResposta = CheckoutIdempotency.Criar(Key, Hash);

        var repo = Substitute.For<ICheckoutIdempotencyRepository>();
        repo.GetByKeyAsync(Key, Arg.Any<CancellationToken>())
            .Returns(new List<CheckoutIdempotency> { registroSemResposta } as IReadOnlyList<CheckoutIdempotency>);

        var svc = BuildService(repo);

        var result = await svc.TentarReservarAsync(Key, Hash);

        result.Should().BeNull();
        await repo.DidNotReceive().TentarReservarAsync(Arg.Any<CheckoutIdempotency>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TentarReservar_RaceReplayComFatura_RetornaDTO()
    {
        // Race: outra request inseriu e já finalizou
        var registroVencedor = CheckoutIdempotency.Criar(Key, Hash);
        registroVencedor.VincularFatura(FaturaId, InitPoint);

        var repo = Substitute.For<ICheckoutIdempotencyRepository>();
        repo.GetByKeyAsync(Key, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CheckoutIdempotency>() as IReadOnlyList<CheckoutIdempotency>);
        repo.TentarReservarAsync(Arg.Any<CheckoutIdempotency>(), Arg.Any<CancellationToken>())
            .Returns((false, registroVencedor));

        var svc = BuildService(repo);

        var result = await svc.TentarReservarAsync(Key, Hash);

        result.Should().NotBeNull();
        result!.PedidoId.Should().Be(FaturaId);
    }

    // ── RegistrarRespostaAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task RegistrarResposta_RegistroExistente_VinculaEAtualiza()
    {
        var registro = CheckoutIdempotency.Criar(Key, Hash);

        var repo = Substitute.For<ICheckoutIdempotencyRepository>();
        repo.GetByKeyHashAsync(Key, Hash, Arg.Any<CancellationToken>())
            .Returns(registro);

        var svc = BuildService(repo);

        await svc.RegistrarRespostaAsync(Key, Hash, FaturaId, InitPoint);

        registro.FaturaId.Should().Be(FaturaId);
        registro.InitPoint.Should().Be(InitPoint);
        await repo.Received(1).UpdateAsync(registro, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegistrarResposta_RegistroNaoEncontrado_NoOp()
    {
        var repo = Substitute.For<ICheckoutIdempotencyRepository>();
        repo.GetByKeyHashAsync(Key, Hash, Arg.Any<CancellationToken>())
            .Returns((CheckoutIdempotency?)null);

        var svc = BuildService(repo);

        // Não deve lançar
        await svc.Invoking(s => s.RegistrarRespostaAsync(Key, Hash, FaturaId, InitPoint))
            .Should().NotThrowAsync();

        await repo.DidNotReceive().UpdateAsync(Arg.Any<CheckoutIdempotency>(), Arg.Any<CancellationToken>());
    }
}
