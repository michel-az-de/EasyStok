using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Application.Tests.UseCases;

public class RegistrarEntradaEstoqueUseCaseTests
{
    [Fact]
    public async Task Deve_registrar_entrada_com_descricao_gerada_e_movimentacao()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var variacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var gerador = Substitute.For<IGeradorDescricaoAnuncio>();
        var logger = Substitute.For<ILogger<RegistrarEntradaEstoqueUseCase>>();
        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Galaxy Buds FE",
            Status = StatusProduto.Ativo,
            PrecoReferencia = Dinheiro.FromDecimal(399.90m),
            SkuBase = CodigoSku.From("BUDS-FE")
        };

        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);
        gerador.GerarAsync(produto, null, null, "Mercado Livre").Returns("Descricao pronta");

        var useCase = new RegistrarEntradaEstoqueUseCase(
            produtoRepository,
            variacaoRepository,
            itemRepository,
            movimentacaoRepository,
            unitOfWork,
            logger,
            gerador);

        var result = await useCase.ExecuteAsync(new RegistrarEntradaEstoqueCommand(
            empresaId,
            produto.Id,
            null,
            10,
            250m,
            null,
            new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            NaturezaMovimentacaoEstoque.Compra,
            "CAP3426",
            null,
            "ML-ABC",
            null,
            null,
            null,
            "Fornecedor XPTO",
            null,
            null,
            null,
            null,
            null,
            "Mercado Livre"));

        await itemRepository.Received(1).AddAsync(Arg.Is<ItemEstoque>(i =>
            i.Id == result.ItemEstoqueId &&
            i.DescricaoAnuncio == "Descricao pronta" &&
            i.QuantidadeAtual.Value == 10 &&
            i.ChavePesquisa != null &&
            i.ChavePesquisa.Contains("CAP3426")));

        await movimentacaoRepository.Received(1).AddAsync(Arg.Is<MovimentacaoEstoque>(m =>
            m.Id == result.MovimentacaoId &&
            m.Tipo == TipoMovimentacaoEstoque.Entrada &&
            m.Natureza == NaturezaMovimentacaoEstoque.Compra));

        await unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Deve_falhar_quando_produto_pertence_a_outra_empresa()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var variacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<RegistrarEntradaEstoqueUseCase>>();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = Guid.NewGuid(),
            Nome = "Galaxy Buds FE",
            Status = StatusProduto.Ativo
        };

        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);

        var useCase = new RegistrarEntradaEstoqueUseCase(
            produtoRepository,
            variacaoRepository,
            itemRepository,
            movimentacaoRepository,
            unitOfWork,
            logger);

        var act = () => useCase.ExecuteAsync(new RegistrarEntradaEstoqueCommand(
            Guid.NewGuid(),
            produto.Id,
            null,
            10,
            250m,
            null,
            new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            NaturezaMovimentacaoEstoque.Compra,
            "CAP3426",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*nao pertence a empresa*");

        await itemRepository.DidNotReceive().AddAsync(Arg.Any<ItemEstoque>());
        await movimentacaoRepository.DidNotReceive().AddAsync(Arg.Any<MovimentacaoEstoque>());
        await unitOfWork.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task Deve_falhar_quando_variacao_esta_inativa()
    {
        var produtoRepository = Substitute.For<IProdutoRepository>();
        var variacaoRepository = Substitute.For<IProdutoVariacaoRepository>();
        var itemRepository = Substitute.For<IItemEstoqueRepository>();
        var movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var logger = Substitute.For<ILogger<RegistrarEntradaEstoqueUseCase>>();
        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Galaxy Buds FE",
            Status = StatusProduto.Ativo
        };

        var variacao = new ProdutoVariacao
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            Nome = "Grafite",
            Ativa = false
        };

        produtoRepository.GetByIdAsync(produto.Id).Returns(produto);
        variacaoRepository.GetByIdAsync(variacao.Id).Returns(variacao);

        var useCase = new RegistrarEntradaEstoqueUseCase(
            produtoRepository,
            variacaoRepository,
            itemRepository,
            movimentacaoRepository,
            unitOfWork,
            logger);

        var act = () => useCase.ExecuteAsync(new RegistrarEntradaEstoqueCommand(
            empresaId,
            produto.Id,
            variacao.Id,
            10,
            250m,
            null,
            new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc),
            NaturezaMovimentacaoEstoque.Compra,
            "CAP3426",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null));

        await act.Should().ThrowAsync<UseCaseValidationException>()
            .WithMessage("*variacao informada esta inativa*");

        await itemRepository.DidNotReceive().AddAsync(Arg.Any<ItemEstoque>());
        await movimentacaoRepository.DidNotReceive().AddAsync(Arg.Any<MovimentacaoEstoque>());
        await unitOfWork.DidNotReceive().CommitAsync();
    }
}
