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

    // #2487: data pura é o dia de Brasília; o "até" inclui o dia inteiro em vez de parar em 00:00 UTC.
    [Fact]
    public async Task GetAll_data_pura_inclui_o_ultimo_dia_inteiro_de_Brasilia()
    {
        var empresaId = Guid.NewGuid();
        _currentUser.EmpresaId.Returns(empresaId);
        _movRepo.GetByEmpresaAsync(default, default, default, default, default, default, default)
            .ReturnsForAnyArgs((Enumerable.Empty<EasyStock.Domain.Entities.MovimentacaoEstoque>(), 0));

        await _controller.GetAll(empresaId, de: new DateTime(2026, 11, 1), ate: new DateTime(2026, 11, 13),
            tipo: null, natureza: null);

        await _movRepo.Received(1).GetByEmpresaAsync(empresaId,
            new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 14, 3, 0, 0, DateTimeKind.Utc).AddTicks(-10),
            null, null, 1, 20);
    }
}
