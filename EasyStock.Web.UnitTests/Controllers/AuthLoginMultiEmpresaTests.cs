using System.Net;
using System.Text;
using EasyStock.Web.Controllers;
using EasyStock.Web.Models.ViewModels.Auth;
using EasyStock.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Web.UnitTests.Controllers;

/// <summary>
/// Login em duas etapas (ADR-0047, #1007). A Api emite token SEM <c>empresaId</c> quando o
/// usuario tem 2+ empresas ativas — e o Web tratava isso como erro terminal ("entre em
/// contato com o suporte"), deixando esse usuario sem conseguir entrar. Agora ele escolhe
/// a empresa e o login e concluido com ela.
///
/// <para>
/// Alem do caminho novo, estes testes travam o que NAO pode mudar: o login de empresa
/// unica segue direto, e a senha guardada entre os dois passos sai da sessao no primeiro
/// uso — tenha ele sucesso ou nao.
/// </para>
/// </summary>
public class AuthLoginMultiEmpresaTests
{
    // JWT so precisa ser lido pelo IJwtClaimsReader substituido; o conteudo nao importa.
    private const string TokenSemEmpresa = "token.sem.empresa";
    private const string TokenComEmpresa = "token.com.empresa";
    private const string EmpresaEscolhida = "11111111-1111-1111-1111-111111111111";

    private static (AuthController ctrl, FakeSession sessao, RoteandoHandler handler) Montar(
        string tokenDoLogin = TokenComEmpresa,
        string? empresaDoToken = EmpresaEscolhida,
        string listaEmpresasJson = """{"isSuperAdmin":false,"empresas":[]}""")
    {
        var handler = new RoteandoHandler(tokenDoLogin, listaEmpresasJson);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };
        var api = new ApiClient(http, NullLogger<ApiClient>.Instance);

        var fakeSession = new FakeSession();
        var httpCtx = new DefaultHttpContext { Session = fakeSession };

        var authService = Substitute.For<IAuthenticationService>();
        var services = new ServiceCollection();
        services.AddSingleton(authService);
        httpCtx.RequestServices = services.BuildServiceProvider();

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpCtx);
        var session = new SessionService(accessor);

        var jwt = Substitute.For<IJwtClaimsReader>();
        jwt.TryReadClaim(TokenSemEmpresa, "empresaId").Returns((string?)null);
        jwt.TryReadClaim(TokenComEmpresa, "empresaId").Returns(empresaDoToken);

        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns("Development");

        var ctrl = new AuthController(api, session, env, jwt, Substitute.For<IConfiguration>(),
            NullLogger<AuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpCtx },
            TempData = new TempDataDictionary(httpCtx, Substitute.For<ITempDataProvider>()),
        };

        var url = Substitute.For<IUrlHelper>();
        url.IsLocalUrl(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>()?.StartsWith('/') == true);
        ctrl.Url = url;

        return (ctrl, fakeSession, handler);
    }

    private static LoginViewModel Credenciais(bool manterLogado = false) =>
        new() { Email = "felipe@easystok.com", Senha = "senha-secreta", ManterLogado = manterLogado };

    // ── regressao: empresa unica nao muda em nada ────────────────────

    [Fact]
    public async Task Login_com_empresa_unica_autentica_direto()
    {
        var (ctrl, sessao, _) = Montar();

        var res = await ctrl.Login(Credenciais(), returnUrl: null);

        res.Should().BeOfType<RedirectToActionResult>();
        sessao.GetString("access_token").Should().Be(TokenComEmpresa);
        sessao.GetString("empresa_atual_id").Should().Be(EmpresaEscolhida);
        sessao.GetString("login_pendente").Should().BeNull("nao ha o que escolher");
    }

    [Fact]
    public async Task Deep_link_continua_vencendo_o_portal()
    {
        var (ctrl, _, _) = Montar();

        var res = await ctrl.Login(Credenciais(), returnUrl: "/pedidos/123");

        res.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/pedidos/123");
    }

    // ── caminho novo: 2+ empresas ────────────────────────────────────

    [Fact]
    public async Task Login_multi_empresa_manda_pro_seletor_sem_autenticar()
    {
        var (ctrl, sessao, _) = Montar(
            tokenDoLogin: TokenSemEmpresa,
            listaEmpresasJson: DuasEmpresas);

        var res = await ctrl.Login(Credenciais(), returnUrl: null);

        res.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be(nameof(AuthController.SelecionarEmpresa));
        // Nada de sessao autenticada antes da escolha: o token sem empresa nao serve.
        sessao.GetString("access_token").Should().BeNull();
        sessao.GetString("empresa_atual_id").Should().BeNull();
    }

    [Fact]
    public async Task Usuario_sem_vinculo_mantem_a_mensagem_de_suporte()
    {
        // Anomalia real (SuperAdmin sem empresa, vinculo faltando): nada a escolher.
        var (ctrl, _, _) = Montar(tokenDoLogin: TokenSemEmpresa);

        var res = await ctrl.Login(Credenciais(), returnUrl: null);

        res.Should().BeOfType<ViewResult>();
        ctrl.ModelState[string.Empty]!.Errors.Single().ErrorMessage.Should().Contain("suporte");
    }

    [Fact]
    public async Task Seletor_lista_as_empresas_do_usuario()
    {
        var (ctrl, _, _) = Montar(tokenDoLogin: TokenSemEmpresa, listaEmpresasJson: DuasEmpresas);
        await ctrl.Login(Credenciais(), returnUrl: null);

        var vm = ctrl.SelecionarEmpresa().Should().BeOfType<ViewResult>()
            .Which.Model.Should().BeOfType<SelecionarEmpresaViewModel>().Subject;

        vm.Empresas.Select(e => e.Nome).Should().Equal("Casa da Babá", "FMA Informática");
        vm.Email.Should().Be("felipe@easystok.com");
    }

    [Fact]
    public async Task Escolher_empresa_conclui_o_login_com_ela()
    {
        var (ctrl, sessao, handler) = Montar(tokenDoLogin: TokenSemEmpresa, listaEmpresasJson: DuasEmpresas);
        await ctrl.Login(Credenciais(), returnUrl: null);
        handler.TokenDoLogin = TokenComEmpresa;   // 2o POST /auth/login ja vem com empresa

        var res = await ctrl.SelecionarEmpresa(EmpresaEscolhida);

        res.Should().BeOfType<RedirectToActionResult>();
        sessao.GetString("access_token").Should().Be(TokenComEmpresa);
        sessao.GetString("empresa_atual_id").Should().Be(EmpresaEscolhida);
        handler.UltimoLoginBody.Should().Contain(EmpresaEscolhida, "a empresa escolhida vai no passo 2");
    }

    [Fact]
    public async Task Return_url_sobrevive_a_escolha_de_empresa()
    {
        var (ctrl, _, handler) = Montar(tokenDoLogin: TokenSemEmpresa, listaEmpresasJson: DuasEmpresas);
        await ctrl.Login(Credenciais(), returnUrl: "/contas-a-receber");
        handler.TokenDoLogin = TokenComEmpresa;

        var res = await ctrl.SelecionarEmpresa(EmpresaEscolhida);

        res.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("/contas-a-receber");
    }

    // ── seguranca: a senha nao fica ──────────────────────────────────

    [Fact]
    public async Task Senha_sai_da_sessao_apos_a_escolha_bem_sucedida()
    {
        var (ctrl, sessao, handler) = Montar(tokenDoLogin: TokenSemEmpresa, listaEmpresasJson: DuasEmpresas);
        await ctrl.Login(Credenciais(), returnUrl: null);
        handler.TokenDoLogin = TokenComEmpresa;

        await ctrl.SelecionarEmpresa(EmpresaEscolhida);

        sessao.GetString("login_pendente").Should().BeNull();
    }

    [Fact]
    public async Task Senha_sai_da_sessao_mesmo_quando_o_passo_2_falha()
    {
        var (ctrl, sessao, handler) = Montar(tokenDoLogin: TokenSemEmpresa, listaEmpresasJson: DuasEmpresas);
        await ctrl.Login(Credenciais(), returnUrl: null);
        handler.FalharLogin = true;

        var res = await ctrl.SelecionarEmpresa(EmpresaEscolhida);

        res.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be(nameof(AuthController.Login));
        sessao.GetString("login_pendente").Should().BeNull("uso unico: falhou, some do mesmo jeito");
    }

    [Fact]
    public async Task Voltar_ao_login_descarta_a_pendencia()
    {
        var (ctrl, sessao, _) = Montar(tokenDoLogin: TokenSemEmpresa, listaEmpresasJson: DuasEmpresas);
        await ctrl.Login(Credenciais(), returnUrl: null);

        ctrl.Login(returnUrl: null, bye: null);

        sessao.GetString("login_pendente").Should().BeNull();
    }

    [Fact]
    public async Task Empresa_fora_da_lista_nao_e_aceita()
    {
        // Sem esta checagem, um POST forjado tentaria autenticar numa empresa que o passo 1
        // nao ofereceu. (A Api tambem revalida o vinculo — isto evita gastar a ida.)
        var (ctrl, _, handler) = Montar(tokenDoLogin: TokenSemEmpresa, listaEmpresasJson: DuasEmpresas);
        await ctrl.Login(Credenciais(), returnUrl: null);
        handler.LoginsChamados = 0;

        var res = await ctrl.SelecionarEmpresa("99999999-9999-9999-9999-999999999999");

        res.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be(nameof(AuthController.Login));
        handler.LoginsChamados.Should().Be(0, "nem chega a tentar autenticar");
    }

    [Fact]
    public void Seletor_sem_pendencia_volta_pro_login()
    {
        var (ctrl, _, _) = Montar();

        ctrl.SelecionarEmpresa().Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be(nameof(AuthController.Login));
    }

    [Fact]
    public async Task Pendencia_expirada_volta_pro_login()
    {
        var (ctrl, sessao, _) = Montar(tokenDoLogin: TokenSemEmpresa, listaEmpresasJson: DuasEmpresas);
        await ctrl.Login(Credenciais(), returnUrl: null);

        // Simula o relogio passando do TTL reescrevendo a pendencia com validade no passado.
        var vencida = sessao.GetString("login_pendente")!.Replace(
            DateTime.UtcNow.Year.ToString(), (DateTime.UtcNow.Year - 1).ToString());
        sessao.SetString("login_pendente", vencida);

        var res = await ctrl.SelecionarEmpresa(EmpresaEscolhida);

        res.Should().BeOfType<RedirectToActionResult>()
            .Which.ActionName.Should().Be(nameof(AuthController.Login));
        sessao.GetString("login_pendente").Should().BeNull();
    }

    private const string DuasEmpresas = """
        {"isSuperAdmin":false,"empresas":[
            {"id":"11111111-1111-1111-1111-111111111111","nome":"Casa da Babá"},
            {"id":"22222222-2222-2222-2222-222222222222","nome":"FMA Informática"}]}
        """;

    /// <summary>Responde por rota: login, lista-empresas, me e lojas (1 loja).</summary>
    private sealed class RoteandoHandler(string tokenDoLogin, string listaEmpresasJson) : HttpMessageHandler
    {
        public string TokenDoLogin { get; set; } = tokenDoLogin;
        public bool FalharLogin { get; set; }
        public int LoginsChamados { get; set; }
        public string? UltimoLoginBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("auth/lista-empresas", StringComparison.Ordinal))
                return Json(listaEmpresasJson);

            if (path.EndsWith("auth/login", StringComparison.Ordinal))
            {
                LoginsChamados++;
                UltimoLoginBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken);

                return FalharLogin
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    {
                        Content = new StringContent("""{"error":{"message":"nao"}}""", Encoding.UTF8, "application/json")
                    }
                    : Json("{\"token\":\"" + TokenDoLogin + "\",\"refreshToken\":\"rt\","
                        + "\"usuario\":{\"id\":\"u1\",\"nome\":\"Felipe\",\"email\":\"felipe@easystok.com\",\"nivel\":\"Admin\"}}");
            }

            if (path.EndsWith("auth/me", StringComparison.Ordinal))
                return Json("""{"temaPreferido":"light"}""");

            // Envelope {"data": ...} como a Api real (DataOk).
            if (path.EndsWith("lojas", StringComparison.Ordinal))
                return Json("""
                    {"data":[{"id":"loja-1","empresaId":"11111111-1111-1111-1111-111111111111",
                              "nome":"Matriz","emoji":"🏪","cidade":"SP","plano":"Pro","ativa":true}]}
                    """);

            return Json("{}");
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    /// <summary>Sessao em memoria — a real e server-side, o teste so precisa que guarde.</summary>
    private sealed class FakeSession : ISession
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
}
