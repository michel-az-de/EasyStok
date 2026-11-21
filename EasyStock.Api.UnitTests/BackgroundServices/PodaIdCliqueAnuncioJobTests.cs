using EasyStock.Api.BackgroundServices;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.BackgroundServices;

public sealed class PodaIdCliqueAnuncioJobTests
{
    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }

    [Fact]
    public async Task Rodada_poda_o_que_foi_capturado_antes_de_agora_menos_90_dias()
    {
        var agora = new DateTimeOffset(2026, 11, 20, 4, 0, 0, TimeSpan.Zero);
        var repositorio = Substitute.For<IOrigemAnuncioRepository>();
        repositorio.PodarIdsDeCliqueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(3);
        var provider = new ServiceCollection().AddSingleton(repositorio).BuildServiceProvider();
        var job = new PodaIdCliqueAnuncioJob(provider, new RelogioFixo(agora), NullLogger<PodaIdCliqueAnuncioJob>.Instance);

        var podados = await job.PodarAsync(CancellationToken.None);

        podados.Should().Be(3);
        OrigemAnuncio.RetencaoIdClique.Should().Be(TimeSpan.FromDays(90));
        await repositorio.Received(1).PodarIdsDeCliqueAsync(agora.AddDays(-90), Arg.Any<CancellationToken>());
    }
}
