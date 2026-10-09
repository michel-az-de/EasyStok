using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Postgre.Integration;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Api.UnitTests.Integration;

/// <summary>
/// #1507: o lote do outbox de integração divide o DbContext do escopo. Handler que falha no meio deixa entidades
/// sujas rastreadas; sem descartá-las, o commit que marca a falha (e o de todo evento seguinte) reenvia a sujeira.
/// </summary>
public class IntegrationEventDispatcherTests
{
    private const string Tipo = "pedido.teste";

    [Fact]
    public async Task Handler_que_falha_descarta_as_alteracoes_pendentes_antes_de_gravar_a_falha()
    {
        var evento = OutboxEventoIntegracao.Criar(Guid.NewGuid(), Tipo, "Pedido", Guid.NewGuid(), "{}");
        var repo = Substitute.For<IOutboxEventoIntegracaoRepository>();
        repo.ProximosPendentesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        repo.ProximosPendentesAsync(Arg.Is(0), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([evento]);
        var uow = Substitute.For<IUnitOfWork>();
        var chamadas = new List<string>();
        uow.When(u => u.DescartarAlteracoesPendentes()).Do(_ => chamadas.Add("descartar"));
        uow.CommitAsync().Returns(_ => { chamadas.Add("commit"); return 1; });

        var handler = Substitute.For<IIntegrationEventHandler>();
        handler.HandleAsync(evento, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("handler quebrou"));
        var services = new ServiceCollection();
        services.AddKeyedSingleton(Tipo, handler);
        using var provider = services.BuildServiceProvider();

        var dispatcher = new IntegrationEventDispatcher(repo, uow, provider, NullLogger<IntegrationEventDispatcher>.Instance);
        await dispatcher.ExecutarRodadaAsync(10, CancellationToken.None);

        chamadas.Should().Equal("commit", "descartar", "commit");
        evento.Tentativas.Should().Be(1);
        evento.ErroUltimaTentativa.Should().Contain("handler quebrou");
    }
}
