using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Entradas;
using EasyStock.Web.Models.ViewModels.Estoque;
using EasyStock.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Web.UnitTests.Integration;

public class EstoqueFracionadoTests
{
    [Theory]
    [InlineData("0.5")]
    [InlineData("1.5")]
    [InlineData("2.125")]
    [InlineData("10")]
    public async Task ApiClient_LeSaldoFracionadoESaldoInteiro(string saldo)
    {
        using var http = new HttpClient(new EstoqueHandler(saldo)) { BaseAddress = new Uri("http://api.test/") };
        var api = new ApiClient(http, NullLogger<ApiClient>.Instance);

        var resposta = await api.GetAsync<EstoqueSku>("estoque/item");

        resposta.Success.Should().BeTrue();
        resposta.Data!.Qty.Should().Be(decimal.Parse(saldo, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("en-US")]
    public async Task Views_RenderizamSaldoComPontoNosArgumentosJavaScript(string cultura)
    {
        var anterior = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultura);
        try
        {
            using var factory = new WebApplicationFactory<WebTestEntryPoint>();
            using var scope = factory.Services.CreateScope();
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var item = JsonSerializer.Deserialize<EstoqueSku>(ItemJson("1.5"), options)!;
            var reposicao = JsonSerializer.Deserialize<ReposicaoFormViewModel>("{\"estoqueAtual\":1.5}", options)!;

            var estoqueHtml = await RenderizarAsync(scope.ServiceProvider, "/Views/Estoque/Index.cshtml",
                new EstoqueListViewModel { Itens = [item] });
            var reposicaoHtml = await RenderizarAsync(scope.ServiceProvider, "/Views/Entradas/Reposicao.cshtml", reposicao);

            Regex.Match(estoqueHtml, @"openModal\('item'[^)]*\)").Value
                .Should().Be("openModal('item', 'R2 estoque', 1.5, null)");
            Regex.Match(reposicaoHtml, @"estoqueAtual:[^\r\n]*").Value
                .Should().Be("estoqueAtual: 1.5,");
        }
        finally { CultureInfo.CurrentCulture = anterior; }
    }

    private static async Task<string> RenderizarAsync(IServiceProvider services, string caminho, object model)
    {
        var http = new DefaultHttpContext { RequestServices = services };
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "Render estoque"));
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var engine = services.GetRequiredService<IRazorViewEngine>();
        var resultado = engine.GetView(null, caminho, isMainPage: false);
        resultado.Success.Should().BeTrue();
        var view = resultado.View!;
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var data = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
        var context = new ViewContext(action, view, data,
            new TempDataDictionary(http, services.GetRequiredService<ITempDataProvider>()), writer, new HtmlHelperOptions());
        await view.RenderAsync(context);
        // As duas views guardam seu JavaScript na seção Scripts, normalmente emitida pelo layout.
        if (view is RazorView razor && razor.RazorPage.SectionWriters.TryGetValue("Scripts", out var scripts))
            await scripts();
        return WebUtility.HtmlDecode(writer.ToString());
    }

    private static string ItemJson(string saldo) =>
        $$$"""{"id":"item","produtoId":"produto","varId":"","sku":"R2","qty":{{{saldo}}},"status":"ok","produto":{"id":"produto","sku":"R2","nome":"R2 estoque","categoria":"categoria","status":"Ativo"}}""";

    private sealed class EstoqueHandler(string saldo) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":" + ItemJson(saldo) + "}", Encoding.UTF8, "application/json")
            });
    }
}
