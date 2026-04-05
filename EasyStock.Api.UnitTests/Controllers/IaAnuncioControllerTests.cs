using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AnuncioIa;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

public class IaAnuncioControllerTests
{
    private readonly IAnuncioIaRepository _anuncioRepo = Substitute.For<IAnuncioIaRepository>();
    private readonly IUsoIaRepository _usoRepo = Substitute.For<IUsoIaRepository>();
    private readonly IAssinaturaEmpresaRepository _assinaturaRepo = Substitute.For<IAssinaturaEmpresaRepository>();
    private readonly IProdutoRepository _produtoRepo = Substitute.For<IProdutoRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    private readonly IaAnuncioController _controller;
    private readonly Guid _empresaId = Guid.NewGuid();

    public IaAnuncioControllerTests()
    {
        _currentUser.EmpresaId.Returns(_empresaId);
        _currentUser.Nivel.Returns(NivelAcesso.Operador);

        var salvarUseCase = new SalvarRascunhoAnuncioUseCase(_produtoRepo, _anuncioRepo, _unitOfWork);
        var listarUseCase = new ListarAnunciosUseCase(_anuncioRepo);
        var excluirUseCase = new ExcluirAnuncioUseCase(_anuncioRepo, _unitOfWork);
        var obterUsoUseCase = new ObterUsoIaUseCase(_usoRepo, _assinaturaRepo);

        // GerarAnuncioStreamingUseCase not needed for these tests
        _controller = new IaAnuncioController(
            null!,
            salvarUseCase,
            listarUseCase,
            excluirUseCase,
            obterUsoUseCase,
            _currentUser);
    }

    [Fact]
    public async Task ListarAnuncios_DeveRetornarEnvelope_ComDataList()
    {
        var produtoId = Guid.NewGuid();
        _anuncioRepo.GetByProdutoAsync(_empresaId, produtoId).Returns(new List<AnuncioIa>
        {
            new() { Id = Guid.NewGuid(), EmpresaId = _empresaId, ProdutoId = produtoId, Titulo = "Anuncio 1", Conteudo = "desc", CriadoEm = DateTime.UtcNow, Salvo = true }
        });

        var result = await _controller.ListarAnuncios(produtoId, _empresaId);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<AnuncioIaResult>>>().Subject;
        envelope.Data.Should().HaveCount(1);
        envelope.Meta.Should().NotBeNull();
    }

    [Fact]
    public async Task ExcluirAnuncio_DeveRetornarNotFound_QuandoNaoExistir()
    {
        _anuncioRepo.GetByIdAsync(_empresaId, Arg.Any<Guid>()).Returns((AnuncioIa?)null);

        var act = async () => await _controller.ExcluirAnuncio(Guid.NewGuid(), _empresaId);

        await act.Should().ThrowAsync<EasyStock.Application.UseCases.Common.UseCaseValidationException>();
    }

    [Fact]
    public async Task ObterUso_DeveRetornarEnvelope_ComUsoDoMes()
    {
        _usoRepo.GetAsync(_empresaId, Arg.Any<int>(), Arg.Any<int>()).Returns(new UsoIa
        {
            Id = Guid.NewGuid(),
            EmpresaId = _empresaId,
            TotalGeracoes = 5,
            TotalTokens = 2500,
            Ano = DateTime.UtcNow.Year,
            Mes = DateTime.UtcNow.Month
        });
        _assinaturaRepo.GetAtivaAsync(_empresaId).Returns((AssinaturaEmpresa?)null);

        var result = await _controller.ObterUso(_empresaId);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<UsoIaResult>>().Subject;
        envelope.Data.TotalGeracoes.Should().Be(5);
        envelope.Data.TotalTokens.Should().Be(2500);
        envelope.Data.Ilimitado.Should().BeTrue();
    }

    [Fact]
    public async Task SalvarAnuncio_DeveRetornarCreated_ComEnvelope()
    {
        var produtoId = Guid.NewGuid();
        _produtoRepo.GetByIdAsync(_empresaId, produtoId).Returns(new Produto
        {
            Id = produtoId,
            EmpresaId = _empresaId,
            Nome = "Produto Teste"
        });

        var request = new SalvarAnuncioRequest(
            _empresaId, produtoId, null,
            "Titulo do Anuncio", "Conteudo do anuncio...", null, 300);

        var result = await _controller.SalvarAnuncio(request);

        var created = result.Should().BeOfType<CreatedResult>().Subject;
        var envelope = created.Value.Should().BeOfType<ApiResponse<SalvarRascunhoAnuncioResult>>().Subject;
        envelope.Data.ProdutoId.Should().Be(produtoId);
        envelope.Data.Titulo.Should().Be("Titulo do Anuncio");
    }
}
