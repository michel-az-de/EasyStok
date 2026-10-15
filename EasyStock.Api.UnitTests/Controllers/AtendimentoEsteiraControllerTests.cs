using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Atendimento.Esteira;
using EasyStock.Application.UseCases.Operacao.Kds;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public class AtendimentoEsteiraControllerTests
{
    private static AtendimentoEsteiraController Build()
    {
        // O lote vazio é recusado antes de tocar a esteira: as dependências nem são chamadas.
        var lote = new AtualizarStatusPedidosEmLoteUseCase(null!, null!, NullLogger<AtualizarStatusPedidosEmLoteUseCase>.Instance);
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.EmpresaId.Returns(Guid.NewGuid());
        return new AtendimentoEsteiraController(new LancarLotePapelUseCase(lote), currentUser);
    }

    [Fact]
    public async Task LoteVazioDevolveBadRequest()
    {
        var result = await Build().LancarLote(new LancarLotePapelBody([]), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }
}
