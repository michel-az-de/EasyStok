using System.Net;
using EasyStock.Web.Controllers;
using EasyStock.Web.Models.ViewModels.Analytics;
using EasyStock.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Web.UnitTests.Controllers;

/// <summary>
/// #2744: o resumo de movimentações passou a exigir a permissão de relatórios. Quem não a tem
/// precisa ver que não tem acesso, e não uma lista vazia que diz "nenhuma movimentação".
/// </summary>
public class MovimentacoesSemPermissaoDeRelatoriosTests
{
    private sealed class TudoProibido : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
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

    [Fact]
    public async Task Sem_permissao_a_tela_diz_que_nao_tem_acesso_em_vez_de_lista_vazia()
    {
        var api = new ApiClient(new HttpClient(new TudoProibido()) { BaseAddress = new Uri("http://api.test/") },
            NullLogger<ApiClient>.Instance);
        var http = new DefaultHttpContext { Session = new SessaoEmMemoria() };
        http.Session.SetString("empresa_atual_id", "11111111-1111-1111-1111-111111111111");
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(http);
        var session = new SessionService(accessor);
        var controller = new AnalyticsController(new AnalyticsService(api, session), session)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };

        var resultado = await controller.Movimentacoes();

        var vm = resultado.Should().BeOfType<ViewResult>().Subject.Model.Should().BeOfType<MovimentacoesViewModel>().Subject;
        vm.Itens.Should().BeEmpty();
        vm.ErroCarga.Should().Be("Você não tem permissão para esta ação.");
    }
}
