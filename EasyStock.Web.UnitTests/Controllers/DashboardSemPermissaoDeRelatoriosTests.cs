using System.Net;
using System.Text;
using EasyStock.Web.Controllers;
using EasyStock.Web.Models.ViewModels.Dashboard;
using EasyStock.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Web.UnitTests.Controllers;

/// <summary>
/// #2744: quem não tem a permissão de relatórios recebe 403 no resumo do dia. A tela inicial segue de pé e não convida a
/// "criar a primeira categoria" ou "abrir o caixa" de uma loja que já faz isso, só porque não pôde ler o resumo.
/// </summary>
public class DashboardSemPermissaoDeRelatoriosTests
{
    private sealed class SoOEstoqueResponde : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/analytics/dashboard"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"data":{"totalSkus":12},"meta":{}}""", Encoding.UTF8, "application/json"),
                });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        }
    }

    [Fact]
    public async Task Sem_o_resumo_do_dia_a_tela_inicial_nao_mostra_o_checklist_de_ativacao()
    {
        var api = new ApiClient(new HttpClient(new SoOEstoqueResponde()) { BaseAddress = new Uri("http://api.test/") },
            NullLogger<ApiClient>.Instance);
        var http = new DefaultHttpContext();
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(http);
        var controller = new DashboardController(api, new SessionService(accessor))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        var resultado = await controller.Index();

        var vm = resultado.Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<DashboardViewModel>().Subject;
        vm.TotalProdutos.Should().Be(12, "o resumo de estoque continua aberto a quem não lê relatórios");
        vm.MostrarChecklist.Should().BeFalse("sem o resumo do dia não dá para saber o que a loja já fez");
    }
}
