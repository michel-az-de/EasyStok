using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Inventario;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Inventario;

public class IniciarContagemUseCaseTests
{
    [Fact]
    public async Task Materializa_um_item_por_lote_e_passa_para_em_andamento()
    {
        var empresaId = Guid.NewGuid();
        var contagem = Contagem.Criar(empresaId, EscopoContagem.Todos, null, ModoContagem.Visivel, EstrategiaLoteContagem.Guiado, Guid.NewGuid());

        var repo = Substitute.For<IContagemRepository>();
        repo.GetByIdComItensAsync(empresaId, contagem.Id).Returns(contagem);
        repo.GetLotesDoEscopoAsync(empresaId, EscopoContagem.Todos, null).Returns(new List<ItemEstoque>
        {
            LoteEstoque(empresaId), LoteEstoque(empresaId), LoteEstoque(empresaId),
        });
        var uow = Substitute.For<IUnitOfWork>();

        var useCase = new IniciarContagemUseCase(repo, uow, Substitute.For<ILogger<IniciarContagemUseCase>>());
        var result = await useCase.ExecuteAsync(new IniciarContagemCommand(empresaId, contagem.Id));

        result.Status.Should().Be(StatusContagem.EmAndamento);
        result.TotalItens.Should().Be(3);
        contagem.Itens.Should().HaveCount(3);
        contagem.Itens.Should().OnlyContain(i => i.ItemEstoqueId != null && !i.Conferido);
        await uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Contagem_inexistente_lanca()
    {
        var repo = Substitute.For<IContagemRepository>();
        repo.GetByIdComItensAsync(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns((Contagem?)null);

        var useCase = new IniciarContagemUseCase(repo, Substitute.For<IUnitOfWork>(), Substitute.For<ILogger<IniciarContagemUseCase>>());
        Func<Task> act = () => useCase.ExecuteAsync(new IniciarContagemCommand(Guid.NewGuid(), Guid.NewGuid()));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    private static ItemEstoque LoteEstoque(Guid empresaId) => new()
    {
        Id = Guid.NewGuid(),
        EmpresaId = empresaId,
        ProdutoId = Guid.NewGuid(),
        QuantidadeInicial = Quantidade.From(5),
        QuantidadeAtual = Quantidade.From(5),
        CustoUnitario = Dinheiro.FromDecimal(10m),
        Status = StatusItemEstoque.Ok,
        EntradaEm = DateTime.UtcNow,
        CriadoEm = DateTime.UtcNow,
        AlteradoEm = DateTime.UtcNow,
    };
}
