using System.Net;
using System.Text;
using EasyStock.Web.Controllers;
using EasyStock.Web.Models.ViewModels.Analytics;
using EasyStock.Web.Models.ViewModels.Dashboard;
using EasyStock.Web.Models.ViewModels.Saidas;
using EasyStock.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Web.UnitTests.Controllers;

/// <summary>
/// #2744: o resumo de estoque e os indicadores de saída seguem abertos à equipe, mas a API devolve a receita nula a
/// quem não tem a permissão de relatórios. A tela precisa dizer "sem acesso", e não mostrar R$ 0,00 nem acusar
/// produtos sem preço.
/// </summary>
public class ReceitaSemPermissaoDeRelatoriosTests
{
    private sealed class ApiSemReceita : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var caminho = request.RequestUri!.AbsolutePath;
            string? json = caminho switch
            {
                _ when caminho.EndsWith("/analytics/dashboard") =>
                    """{"data":{"totalSkus":12,"receitaEstimadaPeriodo":null},"meta":{}}""",
                _ when caminho.EndsWith("/movimentacoes/kpis") =>
                    """{"data":{"totalUnidades":7,"receitaTotal":null,"totalVendas":3,"totalPerdas":1},"meta":{}}""",
                _ when caminho.EndsWith("/movimentacoes") =>
                    """{"data":[],"meta":{"total":0,"page":1,"pages":0,"limit":20}}""",
                _ => null,
            };
            return Task.FromResult(json is null
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class SessaoEmMemoria : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new(StringComparer.Ordinal);
        public bool IsAvailable => true;
        public string Id => "test";
        public IEnumerable<string> Keys => _store.Keys;
        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
    }

    private static (ApiClient Api, SessionService Sessao, DefaultHttpContext Http) Montar()
    {
        var api = new ApiClient(new HttpClient(new ApiSemReceita()) { BaseAddress = new Uri("http://api.test/") },
            NullLogger<ApiClient>.Instance);
        var http = new DefaultHttpContext { Session = new SessaoEmMemoria() };
        http.Session.SetString("empresa_atual_id", "11111111-1111-1111-1111-111111111111");
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(http);
        return (api, new SessionService(accessor), http);
    }

    private static object? Propriedade(object vm, string nome) => vm.GetType().GetProperty(nome)?.GetValue(vm);

    [Fact]
    public async Task Tela_inicial_mostra_o_estoque_e_marca_a_receita_como_sem_acesso()
    {
        var (api, sessao, http) = Montar();
        var controller = new DashboardController(api, sessao) { ControllerContext = new ControllerContext { HttpContext = http } };

        var vm = (await controller.Index()).Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<DashboardViewModel>().Subject;

        vm.TotalProdutos.Should().Be(12);
        Propriedade(vm, "ReceitaDisponivel").Should().Be(false);
    }

    [Fact]
    public async Task Analises_marca_a_receita_do_periodo_como_sem_acesso()
    {
        var (api, sessao, http) = Montar();
        var controller = new AnalyticsController(new AnalyticsService(api, sessao), sessao)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        var vm = (await controller.Index()).Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<AnalyticsViewModel>().Subject;

        Propriedade(vm, "ReceitaDisponivel").Should().Be(false);
    }

    [Fact]
    public async Task Historico_de_saidas_mostra_as_unidades_e_deixa_a_receita_vazia()
    {
        var (api, sessao, http) = Montar();
        var controller = new SaidasController(new SaidasService(api, sessao), sessao)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        var vm = (await controller.Historico()).Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<SaidasHistoricoViewModel>().Subject;

        vm.TotalUnidades.Should().Be(7);
        Propriedade(vm, "ReceitaTotal").Should().BeNull();
    }
}
