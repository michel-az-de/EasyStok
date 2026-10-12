using System.Net;
using System.Text;
using EasyStock.Web.Controllers;
using EasyStock.Web.Models.ViewModels.Usuarios;
using EasyStock.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Web.UnitTests.Controllers;

/// <summary>
/// N9: o formulário "Convidar" não pede senha (a dona nunca define a senha de ninguém): manda nome, e-mail, perfil, loja e,
/// opcionalmente, telefone com o atestado de WhatsApp; a API cria o convidado e envia o convite com link.
/// </summary>
public class UsuariosConvidarTests
{
    private const string EmpresaId = "11111111-1111-1111-1111-111111111111";

    private sealed class GravaRequisicoes(HttpStatusCode status = HttpStatusCode.Created) : HttpMessageHandler
    {
        public List<(string Metodo, string Caminho, string Corpo)> Pedidos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Pedidos.Add((request.Method.Method, request.RequestUri!.AbsolutePath,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(status)
            {
                Content = new StringContent("""{"data":{"usuarioId":"22222222-2222-2222-2222-222222222222"},"meta":{}}""", Encoding.UTF8, "application/json")
            };
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

    private static (UsuariosController Controller, GravaRequisicoes Api) Montar(HttpStatusCode status = HttpStatusCode.Created)
    {
        var handler = new GravaRequisicoes(status);
        var api = new ApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }, NullLogger<ApiClient>.Instance);
        var http = new DefaultHttpContext { Session = new SessaoEmMemoria() };
        http.Session.SetString("empresa_atual_id", EmpresaId);
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(http);
        var sessao = new SessionService(accessor);
        var controller = new UsuariosController(new UsuariosService(api, sessao), new LojasService(api, sessao), sessao)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, Substitute.For<ITempDataProvider>()),
        };
        return (controller, handler);
    }

    [Fact]
    public async Task ConvidarNaoPedeSenha()
    {
        var (controller, api) = Montar();

        var resultado = await controller.Convidar(new ConvidarUsuarioViewModel { Nome = "Ana Souza", Email = "ana@casadababa.com" });

        resultado.Should().BeOfType<RedirectToActionResult>();
        var pedido = api.Pedidos.Should().ContainSingle().Subject;
        pedido.Metodo.Should().Be("POST");
        pedido.Caminho.Should().EndWith("/usuarios");
        pedido.Corpo.ToLowerInvariant().Should().NotContain("senha", "sem senha a API cria o convidado e envia o convite");
        pedido.Corpo.Should().Contain("ana@casadababa.com").And.Contain(EmpresaId);
        controller.TempData["Toast"]!.ToString().Should().StartWith("success|").And.Contain("Convite");
    }

    [Fact]
    public async Task ConvidarLevaOTelefoneEOAtestadoDeWhatsApp()
    {
        var (controller, api) = Montar();

        await controller.Convidar(new ConvidarUsuarioViewModel
        {
            Nome = "Ana Souza", Email = "ana@casadababa.com", Telefone = "(11) 99999-1234", AtestaOptInWhatsApp = true,
        });

        api.Pedidos.Single().Corpo.Should().Contain("\"telefone\":\"(11) 99999-1234\"").And.Contain("\"atestaOptInWhatsApp\":true");
    }

    [Fact]
    public async Task ConvidarSemNomeOuEmailNaoChamaAApi()
    {
        var (controller, api) = Montar();

        await controller.Convidar(new ConvidarUsuarioViewModel { Nome = "", Email = "" });

        api.Pedidos.Should().BeEmpty();
        controller.TempData["Toast"]!.ToString().Should().StartWith("error|");
    }

    [Fact]
    public async Task ReenviarConvitePostaNaApiDoUsuario()
    {
        var (controller, api) = Montar(HttpStatusCode.OK);
        var id = Guid.NewGuid().ToString();

        var resultado = await controller.ReenviarConvite(id);

        resultado.Should().BeOfType<RedirectToActionResult>();
        var pedido = api.Pedidos.Should().ContainSingle().Subject;
        pedido.Metodo.Should().Be("POST");
        pedido.Caminho.Should().EndWith($"/usuarios/{id}/convite");
    }
}
