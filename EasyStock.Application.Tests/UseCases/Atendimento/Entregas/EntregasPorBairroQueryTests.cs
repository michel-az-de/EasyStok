using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Entregas;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Entregas;

/// <summary>S44: relatório de entregas por bairro, do maior para o menor.</summary>
public class EntregasPorBairroQueryTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IEntregasPorBairroQuery _query = Substitute.For<IEntregasPorBairroQuery>();

    [Fact]
    public async Task SomaSoEntreguesNoPeriodo()
    {
        var de = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var ate = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc);
        _query.ListarEntreguesAsync(_empresaId, de, ate, Arg.Any<CancellationToken>()).Returns(new List<PedidoEntregueLinha>
        {
            new(" Centro ", 50m),
            new("centro", 30m),
            new("Vila Nova", 100m),
            new(null, 10m),
        });

        var linhas = await new EntregasPorBairroUseCase(_query).ExecuteAsync(_empresaId, de, ate);

        linhas.Should().Equal(
            new EntregasPorBairroLinha("Vila Nova", 1, 100m),
            new EntregasPorBairroLinha("Centro", 2, 80m),
            new EntregasPorBairroLinha(EntregasPorBairroUseCase.SemBairro, 1, 10m));
        await _query.Received(1).ListarEntreguesAsync(_empresaId, de, ate, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PeriodoInvertidoRecusa()
    {
        var act = () => new EntregasPorBairroUseCase(_query).ExecuteAsync(_empresaId, DateTime.UtcNow, DateTime.UtcNow.AddDays(-1));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }
}
