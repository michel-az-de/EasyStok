using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Lotes;

namespace EasyStock.Application.Tests.UseCases.Lotes;

/// <summary>#2199 (D4): o lote antigo ganha número curto sob demanda; repetir devolve o mesmo.</summary>
public class GarantirNumeroCurtoLoteUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid LoteId = Guid.NewGuid();

    private readonly ILoteNumeroCurtoRepository _repo = Substitute.For<ILoteNumeroCurtoRepository>();

    [Fact]
    public async Task Devolve_o_numero_do_lote()
    {
        _repo.GarantirAsync(EmpresaId, LoteId, Arg.Any<CancellationToken>()).Returns("00000012");

        var resultado = await new GarantirNumeroCurtoLoteUseCase(_repo)
            .ExecuteAsync(new GarantirNumeroCurtoLoteCommand(EmpresaId, LoteId));

        resultado.Should().Be(new NumeroCurtoLoteResult(LoteId, "00000012"));
    }

    [Fact]
    public async Task Lote_inexistente_ou_de_outra_empresa_devolve_nulo()
    {
        _repo.GarantirAsync(EmpresaId, LoteId, Arg.Any<CancellationToken>()).Returns((string?)null);

        var resultado = await new GarantirNumeroCurtoLoteUseCase(_repo)
            .ExecuteAsync(new GarantirNumeroCurtoLoteCommand(EmpresaId, LoteId));

        resultado.Should().BeNull();
    }

    [Fact]
    public async Task EmpresaId_vazio_e_recusado()
    {
        var acao = () => new GarantirNumeroCurtoLoteUseCase(_repo)
            .ExecuteAsync(new GarantirNumeroCurtoLoteCommand(Guid.Empty, LoteId));

        await acao.Should().ThrowAsync<UseCaseValidationException>();
    }
}
