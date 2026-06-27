using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Inventario;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Inventario;

public class FinalizarContagemUseCaseTests
{
    [Fact]
    public async Task Finaliza_quando_todos_conferidos()
    {
        var empresaId = Guid.NewGuid();
        var contagem = EmAndamentoComItens(empresaId, conferidos: true, qtd: 2);
        var repo = Substitute.For<IContagemRepository>();
        repo.GetByIdComItensAsync(empresaId, contagem.Id).Returns(contagem);
        var uow = Substitute.For<IUnitOfWork>();

        var useCase = new FinalizarContagemUseCase(repo, uow, Substitute.For<ILogger<FinalizarContagemUseCase>>());
        var result = await useCase.ExecuteAsync(new FinalizarContagemCommand(empresaId, contagem.Id));

        result.Status.Should().Be(StatusContagem.Finalizada);
        await uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Bloqueia_finalizar_com_lote_nao_conferido()
    {
        var empresaId = Guid.NewGuid();
        var contagem = EmAndamentoComItens(empresaId, conferidos: false, qtd: 1);
        var repo = Substitute.For<IContagemRepository>();
        repo.GetByIdComItensAsync(empresaId, contagem.Id).Returns(contagem);

        var useCase = new FinalizarContagemUseCase(repo, Substitute.For<IUnitOfWork>(), Substitute.For<ILogger<FinalizarContagemUseCase>>());
        Func<Task> act = () => useCase.ExecuteAsync(new FinalizarContagemCommand(empresaId, contagem.Id));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    private static Contagem EmAndamentoComItens(Guid empresaId, bool conferidos, int qtd)
    {
        var c = Contagem.Criar(empresaId, EscopoContagem.Todos, null, ModoContagem.Visivel, EstrategiaLoteContagem.Guiado, Guid.NewGuid());
        c.Iniciar(DateTime.UtcNow);
        for (var i = 0; i < qtd; i++)
            c.Itens.Add(new ItemContagem
            {
                Id = Guid.NewGuid(), EmpresaId = empresaId, ContagemId = c.Id,
                ProdutoId = Guid.NewGuid(), ItemEstoqueId = Guid.NewGuid(), Conferido = conferidos,
            });
        return c;
    }
}
