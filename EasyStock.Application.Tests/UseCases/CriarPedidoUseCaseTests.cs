using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

public class CriarPedidoUseCaseTests
{
    private readonly IPedidoRepository _pedidoRepo = Substitute.For<IPedidoRepository>();
    private readonly IClienteRepository _clienteRepo = Substitute.For<IClienteRepository>();
    private readonly IProdutoRepository _produtoRepo = Substitute.For<IProdutoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private CriarPedidoUseCase Sut() => new(_pedidoRepo, _clienteRepo, _produtoRepo, _uow,
        Substitute.For<ILogger<CriarPedidoUseCase>>());

    [Fact]
    public async Task DeveNormalizarAgendadoParaEm_ParaUtc_QuandoClienteEnviaDataSemFuso()
    {
        // Regressão (estabilidade): a data agendada do cliente chega Kind=Unspecified
        // e o Postgres (timestamp with time zone) rejeita no save. O use case deve
        // normalizar para UTC antes de persistir.
        var empresaId = Guid.NewGuid();
        Pedido? salvo = null;
        _pedidoRepo.When(r => r.AddAsync(Arg.Any<Pedido>())).Do(ci => salvo = ci.Arg<Pedido>());

        var futuroSemFuso = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(1), DateTimeKind.Unspecified);

        await Sut().ExecuteAsync(new CriarPedidoCommand(empresaId, AgendadoParaEm: futuroSemFuso));

        salvo.Should().NotBeNull();
        salvo!.AgendadoParaEm.Should().NotBeNull();
        salvo.AgendadoParaEm!.Value.Kind.Should().Be(DateTimeKind.Utc);
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task DeveCriarPedido_SemAgendamento_QuandoAgendadoParaEmNull()
    {
        var empresaId = Guid.NewGuid();

        var result = await Sut().ExecuteAsync(new CriarPedidoCommand(empresaId));

        result.AgendadoParaEm.Should().BeNull();
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task DeveLancarValidation_QuandoAgendadoNoPassado()
    {
        var empresaId = Guid.NewGuid();
        var passadoSemFuso = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-1), DateTimeKind.Unspecified);

        var act = () => Sut().ExecuteAsync(new CriarPedidoCommand(empresaId, AgendadoParaEm: passadoSemFuso));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*futuro*");
        await _uow.DidNotReceive().CommitAsync();
    }
}
