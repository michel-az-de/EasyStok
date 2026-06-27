using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Inventario;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Inventario;

public class RegistrarItemContagemUseCaseTests
{
    [Fact]
    public async Task Conta_lote_existente_capturando_baseline_do_sistema()
    {
        var empresaId = Guid.NewGuid();
        var loteId = Guid.NewGuid();
        var item = new ItemContagem
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, ContagemId = Guid.NewGuid(),
            ProdutoId = Guid.NewGuid(), ItemEstoqueId = loteId,
        };
        var lote = new ItemEstoque
        {
            Id = loteId, EmpresaId = empresaId, ProdutoId = item.ProdutoId,
            QuantidadeInicial = Quantidade.From(8), QuantidadeAtual = Quantidade.From(8),
            CustoUnitario = Dinheiro.FromDecimal(12m), Status = StatusItemEstoque.Ok,
            EntradaEm = DateTime.UtcNow, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow,
        };

        var repo = Substitute.For<IContagemRepository>();
        repo.GetItemAsync(empresaId, item.Id).Returns(item);
        var itemEstoqueRepo = Substitute.For<IItemEstoqueRepository>();
        itemEstoqueRepo.GetByIdAsync(empresaId, loteId).Returns(lote);
        var uow = Substitute.For<IUnitOfWork>();

        var useCase = new RegistrarItemContagemUseCase(repo, itemEstoqueRepo, uow, Substitute.For<ILogger<RegistrarItemContagemUseCase>>());
        var result = await useCase.ExecuteAsync(new RegistrarItemContagemCommand(empresaId, Guid.NewGuid(), item.Id, 5m));

        result.QtdSistemaNoMomento.Should().Be(8);
        result.QtdContada.Should().Be(5);
        result.Divergencia.Should().Be(-3); // falta: contado 5 - sistema 8
        result.Conferido.Should().BeTrue();
        item.Conferido.Should().BeTrue();
        await uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Lote_descoberto_usa_baseline_zero()
    {
        var empresaId = Guid.NewGuid();
        var item = new ItemContagem
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, ContagemId = Guid.NewGuid(),
            ProdutoId = Guid.NewGuid(), ItemEstoqueId = null,
        };
        var repo = Substitute.For<IContagemRepository>();
        repo.GetItemAsync(empresaId, item.Id).Returns(item);

        var useCase = new RegistrarItemContagemUseCase(
            repo, Substitute.For<IItemEstoqueRepository>(), Substitute.For<IUnitOfWork>(),
            Substitute.For<ILogger<RegistrarItemContagemUseCase>>());
        var result = await useCase.ExecuteAsync(new RegistrarItemContagemCommand(empresaId, Guid.NewGuid(), item.Id, 4m));

        result.QtdSistemaNoMomento.Should().Be(0);
        result.Divergencia.Should().Be(4); // sobra descoberta
    }

    [Fact]
    public async Task Qtd_negativa_lanca()
    {
        var useCase = new RegistrarItemContagemUseCase(
            Substitute.For<IContagemRepository>(), Substitute.For<IItemEstoqueRepository>(),
            Substitute.For<IUnitOfWork>(), Substitute.For<ILogger<RegistrarItemContagemUseCase>>());

        Func<Task> act = () => useCase.ExecuteAsync(new RegistrarItemContagemCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), -1m));
        await act.Should().ThrowAsync<UseCaseValidationException>();
    }
}
