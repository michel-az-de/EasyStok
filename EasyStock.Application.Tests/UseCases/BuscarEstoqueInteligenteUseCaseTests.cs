using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.BuscarEstoqueInteligente;
using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

public class BuscarEstoqueInteligenteUseCaseTests
{
    [Fact]
    public async Task Deve_unificar_produtos_variacoes_e_itens_ordenados_por_score()
    {
        var empresaId = Guid.NewGuid();
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var variacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var fornecedorRepository = Substitute.For<IFornecedorRepository>();

        produtoRepository.SearchAsync(empresaId, "CAP3426").Returns([
            new Produto { Id = Guid.NewGuid(), Nome = "Galaxy Buds FE", SkuBase = CodigoSku.From("BUDS-FE") }
        ]);

        variacaoRepository.SearchAsync(empresaId, "CAP3426").Returns([
            new ProdutoVariacao { Id = Guid.NewGuid(), ProdutoId = Guid.NewGuid(), Nome = "Grafite", Sku = CodigoSku.From("CAP3426") }
        ]);

        itemRepository.SearchAsync(empresaId, "CAP3426").Returns([
            new ItemEstoque { Id = Guid.NewGuid(), ProdutoId = Guid.NewGuid(), CodigoInterno = "CAP3426", ChavePesquisa = "CAP3426 BUDS-FE" }
        ]);

        fornecedorRepository.SearchAsync(empresaId, "CAP3426").Returns([]);

        var pedidoRepository = Substitute.For<IPedidoFornecedorRepository>();
        var lojaRepository = Substitute.For<ILojaRepository>();
        var usuarioRepository = Substitute.For<IUsuarioRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        pedidoRepository.SearchAsync(empresaId, "CAP3426").Returns(Enumerable.Empty<PedidoFornecedor>());
        lojaRepository.SearchAsync(empresaId, "CAP3426").Returns(Enumerable.Empty<Loja>());
        usuarioRepository.SearchAsync(empresaId, "CAP3426").Returns(Enumerable.Empty<Usuario>());
        movimentacaoRepository.SearchAsync(empresaId, "CAP3426").Returns(Enumerable.Empty<MovimentacaoEstoque>());
        var useCase = new BuscarEstoqueInteligenteUseCase(produtoRepository, variacaoRepository, itemRepository, fornecedorRepository, pedidoRepository, lojaRepository, usuarioRepository, movimentacaoRepository);

        var result = await useCase.ExecuteAsync(new BuscarEstoqueInteligenteQuery(empresaId, "CAP3426"));

        result.Should().HaveCount(3);
        result.First().Tipo.Should().Be(TipoResultadoBuscaInteligente.ItemEstoque);
        result.Select(r => r.Tipo).Should().Contain(TipoResultadoBuscaInteligente.Produto);
        result.Select(r => r.Tipo).Should().Contain(TipoResultadoBuscaInteligente.Variacao);
        result.Select(r => r.Tipo).Should().Contain(TipoResultadoBuscaInteligente.ItemEstoque);
    }
}
