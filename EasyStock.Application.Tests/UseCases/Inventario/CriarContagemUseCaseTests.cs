using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Inventario;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Inventario;

public class CriarContagemUseCaseTests
{
    [Fact]
    public async Task Cria_contagem_em_rascunho_e_persiste()
    {
        var repo = Substitute.For<IContagemRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        var useCase = new CriarContagemUseCase(repo, uow, Substitute.For<ILogger<CriarContagemUseCase>>());
        var empresaId = Guid.NewGuid();

        var result = await useCase.ExecuteAsync(new CriarContagemCommand(
            empresaId, Guid.NewGuid(), EscopoContagem.Todos, null, ModoContagem.Cego, EstrategiaLoteContagem.Guiado));

        result.Status.Should().Be(StatusContagem.Rascunho);
        result.EmpresaId.Should().Be(empresaId);
        await repo.Received(1).AddAsync(Arg.Is<Contagem>(c => c.EmpresaId == empresaId && c.Status == StatusContagem.Rascunho));
        await uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Descoberta_e_rejeitada_no_mvp()
    {
        var useCase = new CriarContagemUseCase(
            Substitute.For<IContagemRepository>(), Substitute.For<IUnitOfWork>(), Substitute.For<ILogger<CriarContagemUseCase>>());

        Func<Task> act = () => useCase.ExecuteAsync(new CriarContagemCommand(
            Guid.NewGuid(), Guid.NewGuid(), EscopoContagem.Todos, null, ModoContagem.Visivel, EstrategiaLoteContagem.Descoberta));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task Escopo_categoria_sem_ref_lanca()
    {
        var useCase = new CriarContagemUseCase(
            Substitute.For<IContagemRepository>(), Substitute.For<IUnitOfWork>(), Substitute.For<ILogger<CriarContagemUseCase>>());

        Func<Task> act = () => useCase.ExecuteAsync(new CriarContagemCommand(
            Guid.NewGuid(), Guid.NewGuid(), EscopoContagem.Categoria, null, ModoContagem.Visivel, EstrategiaLoteContagem.Guiado));

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
    }
}
