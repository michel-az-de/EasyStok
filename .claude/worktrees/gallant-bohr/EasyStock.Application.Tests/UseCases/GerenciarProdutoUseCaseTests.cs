using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.GerenciarProduto;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

public class GerenciarProdutoUseCaseTests
{
    [Fact]
    public async Task Deve_falhar_ao_remover_produto_com_estoque()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var categoriaRepository = Substitute.For<ICategoriaRepository>();
        var variacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
        var itemEstoqueRepository = Substitute.For<IItemEstoqueRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var useCase = new GerenciarProdutoUseCase(
            produtoRepository,
            categoriaRepository,
            variacaoRepository,
            itemEstoqueRepository,
            movimentacaoRepository,
            unitOfWork);

        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();

        produtoRepository.GetByIdAsync(empresaId, produtoId).Returns(new Produto
        {
            Id = produtoId,
            EmpresaId = empresaId,
            Nome = "Produto",
            Status = StatusProduto.Ativo
        });
        itemEstoqueRepository.ExisteEstoqueDoProdutoAsync(empresaId, produtoId).Returns(true);

        var act = () => useCase.RemoverAsync(empresaId, produtoId);

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*estoque disponivel*");
        await produtoRepository.DidNotReceive().UpdateAsync(Arg.Any<Produto>());
    }
}
