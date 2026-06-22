using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>QA v1.10 BUG-05: intervalo de periodo invertido (de > ate) deve ser barrado.</summary>
public class MovimentacaoControllerTests
{
    private readonly IMovimentacaoEstoqueRepository _movRepo = Substitute.For<IMovimentacaoEstoqueRepository>();
    private readonly IItemEstoqueRepository _itemRepo = Substitute.For<IItemEstoqueRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly MovimentacaoController _controller;

    public MovimentacaoControllerTests()
    {
        _controller = new MovimentacaoController(_movRepo, _itemRepo, _currentUser);
    }

    [Fact]
    public async Task GetAll_intervalo_invertido_retorna_BadRequest_sem_consultar_repo()
    {
        var empresaId = Guid.NewGuid();
        _currentUser.EmpresaId.Returns(empresaId);

        var result = await _controller.GetAll(
            empresaId,
            de: new DateTime(2026, 12, 31),
            ate: new DateTime(2026, 1, 1),
            tipo: null,
            natureza: null);

        result.Should().BeOfType<BadRequestObjectResult>();
        await _movRepo.DidNotReceiveWithAnyArgs()
            .GetByEmpresaAsync(Guid.Empty, null, null, null, null, 0, 0);
    }
}
