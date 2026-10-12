using EasyStock.Api.Services.Impressao;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Services.Impressao;

/// <summary>
/// S49 (#1269): o console busca o impresso com JWT e o exibe em outra origem; sem CORS na resposta da fonte, o
/// navegador bloqueia Lora e Nunito Sans e o papel sai na fonte de fallback.
/// </summary>
public class FontesImpressaoTests
{
    private static HttpContext Responder(string caminho)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = caminho;
        FontesImpressao.LiberarCors(new StaticFileResponseContext(http, Substitute.For<IFileInfo>()));
        return http;
    }

    [Fact]
    public void FonteDoImpressoLiberadaParaQualquerOrigem()
    {
        Responder(PedidoImpressoHtml.CaminhoFontes + "/Lora.ttf")
            .Response.Headers.AccessControlAllowOrigin.ToString().Should().Be("*");
    }

    [Theory]
    [InlineData("/pwa/index.html")]
    [InlineData("/impressao/outra.css")]
    [InlineData("/impressao/fontesx/Lora.ttf")]
    public void DemaisArquivosSemCors(string caminho)
    {
        Responder(caminho).Response.Headers.ContainsKey("Access-Control-Allow-Origin").Should().BeFalse();
    }
}
