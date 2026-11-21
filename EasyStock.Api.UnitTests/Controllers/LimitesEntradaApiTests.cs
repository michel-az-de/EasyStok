using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using EasyStock.Api.Controllers;
using EasyStock.Api.Http;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Inteligencia.EstoqueBaixo;
using EasyStock.Application.UseCases.Analytics.Reposicao;
using EasyStock.Domain.Reposicao;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>#3082: entrada sem teto na API (paginação, upload e IA).</summary>
public class LimitesEntradaApiTests
{
    [Fact]
    public async Task Inteligencia_EstoqueBaixo_ComPageSizeAbsurdo_LimitaA200()
    {
        var repo = Substitute.For<IAnalyticsRepository>();
        repo.GetSnapshotReposicaoAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ProdutoReposicaoSnapshot>());
        var obterReposicao = new ObterReposicaoUseCase(repo, Substitute.For<IConfiguracaoLojaRepository>(),
            NullLogger<ObterReposicaoUseCase>.Instance);
        var controller = new InteligenciaController(null!, null!, null!, null!,
            new ObterEstoqueBaixoUseCase(obterReposicao, NullLogger<ObterEstoqueBaixoUseCase>.Instance),
            null!, null!, null!);

        var result = await controller.EstoqueBaixo(Guid.NewGuid(), null, null, 1, int.MaxValue);

        var envelope = ((OkObjectResult)result).Value.Should().BeAssignableTo<ApiResponse<IEnumerable<EstoqueBaixoResult>>>().Subject;
        var meta = envelope.Meta.Should().BeOfType<PagedMeta>().Subject;
        meta.Limit.Should().BeLessThanOrEqualTo(200);
    }

    [Fact]
    public async Task Upload_AvatarAcimaDoTeto_RecusaAntesDeCopiarParaMemoria()
    {
        var controller = new UploadsController(null!, Substitute.For<ICurrentUserAccessor>());
        var arquivo = Substitute.For<IFormFile>();
        arquivo.Length.Returns(50L * 1024 * 1024);
        arquivo.FileName.Returns("grande.png");

        var acao = () => controller.UploadAvatar(arquivo, CancellationToken.None);

        await acao.Should().ThrowAsync<UseCaseValidationException>();
        await arquivo.DidNotReceive().CopyToAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(nameof(UploadsController.UploadAvatar))]
    [InlineData(nameof(UploadsController.UploadLogoLoja))]
    [InlineData(nameof(UploadsController.UploadFotoProduto))]
    public void Upload_EndpointsDeImagem_TemTetoNoKestrel(string acao)
    {
        var metodo = typeof(UploadsController).GetMethod(acao)!;
        metodo.GetCustomAttribute<RequestSizeLimitAttribute>().Should().NotBeNull(
            "sem o atributo o Kestrel aceita até 30 MB por pedido antes de o controller recusar");
    }

    [Fact]
    public async Task Ia_FalhaDoProvedor_NaoDevolveMensagemInternaAoCliente()
    {
        const string interno = "upstream 529 sk-ant-chave-interna overloaded";
        var gerador = Substitute.For<IGeradorAutoPreenchimento>();
        gerador.GerarDescricaoProdutoStreamAsync(default!, default, default, default, default)
            .ReturnsForAnyArgs(_ => Falhar(interno));
        var controller = new IaAutoPreenchimentoController(gerador, NullLogger<IaAutoPreenchimentoController>.Instance);
        var corpo = new MemoryStream();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { Response = { Body = corpo } } };

        await controller.CompletarProdutoSse(new CompletarProdutoRequest("Bolo", null, null, null), CancellationToken.None);

        var texto = Encoding.UTF8.GetString(corpo.ToArray());
        texto.Should().Contain("event: erro");
        texto.Should().NotContain("sk-ant").And.NotContain("upstream");
    }

    [Fact]
    public void Ia_CamposDaRequisicao_TemTamanhoMaximo()
    {
        var longo = new string('x', 5000);
        var request = new CompletarProdutoRequest(longo, longo, longo, longo);
        var parametros = typeof(CompletarProdutoRequest).GetConstructors().Single().GetParameters();

        foreach (var p in parametros)
        {
            var teto = p.GetCustomAttribute<StringLengthAttribute>();
            teto.Should().NotBeNull($"{p.Name} entra no prompt pago e precisa de teto");
            teto!.IsValid(longo).Should().BeFalse();
        }
        request.NomeProduto.Should().HaveLength(5000);
    }

    private static async IAsyncEnumerable<string> Falhar(string mensagem)
    {
        await Task.Yield();
        throw new InvalidOperationException(mensagem);
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
