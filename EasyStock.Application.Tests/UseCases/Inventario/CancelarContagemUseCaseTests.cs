using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Inventario;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Inventario;

public class CancelarContagemUseCaseTests
{
    [Fact]
    public async Task Cancela_contagem_em_andamento()
    {
        var empresaId = Guid.NewGuid();
        var contagem = Contagem.Criar(empresaId, EscopoContagem.Todos, null, ModoContagem.Visivel, EstrategiaLoteContagem.Guiado, Guid.NewGuid());
        contagem.Iniciar(DateTime.UtcNow);

        var repo = Substitute.For<IContagemRepository>();
        repo.GetByIdComItensAsync(empresaId, contagem.Id).Returns(contagem);
        var uow = Substitute.For<IUnitOfWork>();

        var useCase = new CancelarContagemUseCase(repo, uow, Substitute.For<ILogger<CancelarContagemUseCase>>());
        var result = await useCase.ExecuteAsync(new CancelarContagemCommand(empresaId, contagem.Id));

        result.Status.Should().Be(StatusContagem.Cancelada);
        await uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Contagem_aplicada_nao_pode_cancelar()
    {
        var empresaId = Guid.NewGuid();
        var contagem = Contagem.Criar(empresaId, EscopoContagem.Todos, null, ModoContagem.Visivel, EstrategiaLoteContagem.Guiado, Guid.NewGuid());
        contagem.Iniciar(DateTime.UtcNow);
        contagem.Finalizar(DateTime.UtcNow);
        contagem.Aplicar(Guid.NewGuid(), DateTime.UtcNow);

        var repo = Substitute.For<IContagemRepository>();
        repo.GetByIdComItensAsync(empresaId, contagem.Id).Returns(contagem);

        var useCase = new CancelarContagemUseCase(repo, Substitute.For<IUnitOfWork>(), Substitute.For<ILogger<CancelarContagemUseCase>>());
        Func<Task> act = () => useCase.ExecuteAsync(new CancelarContagemCommand(empresaId, contagem.Id));

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
    }
}
