using EasyStock.Web.Models.Api;
using EasyStock.Web.Services;
using EasyStock.Web.TagHelpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Web.UnitTests.TagHelpers;

/// <summary>
/// Estrutura HTML do menu (ADR-0032, fatia 5) via o Process publico do TagHelper:
/// ativo-por-rota (aria-current), badges (data-badge), favorito aparece 2x (Meu dia +
/// grupo) com ids unicos, grupos como &lt;details data-group&gt; abertos pela rota.
/// </summary>
public class EsSidebarTagHelperTests
{
    private static string Render(string path, IReadOnlyList<string>? favoritos, bool kds, MenuResumoRaw resumo)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());

        var fonte = Substitute.For<IPreferenciaMenuFonte>();
        fonte.ObterAsync(Arg.Any<string?>())
            .Returns((new FavoritosMenuApi { Favoritos = favoritos?.ToList(), KdsHabilitado = kds }, true));
        var favSvc = new PreferenciaMenuService(fonte, cache);

        var source = Substitute.For<IMenuResumoSource>();
        source.FetchAsync().Returns(resumo);
        var resumoSvc = new MenuResumoService(source, cache);

        var httpCtx = new DefaultHttpContext { Session = new FakeSession() };
        httpCtx.Request.Path = path;
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpCtx);
        var session = new SessionService(accessor);

        var env = Substitute.For<IWebHostEnvironment>();
        env.WebRootPath.Returns(Path.GetTempPath()); // sem icones -> Icon emite span vazio
        var icons = new LucideIconResolver(env, Substitute.For<ILogger<LucideIconResolver>>());

        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary());
        var actionCtx = new ActionContext(httpCtx, new RouteData(), new ActionDescriptor());
        var viewCtx = new ViewContext(actionCtx, Substitute.For<IView>(), viewData,
            Substitute.For<ITempDataDictionary>(), TextWriter.Null, new HtmlHelperOptions());

        var config = Substitute.For<IConfiguration>();
        var th = new EsSidebarTagHelper(favSvc, resumoSvc, session, icons, config) { ViewContext = viewCtx };

        var ctx = new TagHelperContext(new TagHelperAttributeList(), new Dictionary<object, object>(), "t");
        var output = new TagHelperOutput("es-sidebar", new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        th.ProcessAsync(ctx, output).GetAwaiter().GetResult();

        output.TagName.Should().Be("nav");
        output.Attributes.ContainsName("data-es-sidebar").Should().BeTrue();
        return output.Content.GetContent();
    }

    private static MenuResumoRaw Resumo(int criticos, int vencidos, int pedidos) =>
        new(new DashboardResumoApi { AlertasEstoqueBaixo = criticos, AlertasVencidos = vencidos },
            new ResumoDiaApi { PedidosPendentes = pedidos }, Ok: true);

    [Fact]
    public void Rota_ativa_recebe_aria_current_e_abre_o_grupo()
    {
        var html = Render("/pedidos", new[] { "pedidos" }, kds: true, Resumo(0, 0, 0));

        html.Should().Contain("aria-current=\"page\"");
        html.Should().Contain("data-group=\"operacao\" open");
    }

    [Fact]
    public void Favorito_aparece_duas_vezes_com_ids_unicos()
    {
        var html = Render("/dashboard", new[] { "pedidos" }, kds: true, Resumo(0, 0, 0));

        html.Should().Contain("id=\"es-row-pedidos-fav\"");   // instancia em "Meu dia"
        html.Should().Contain("id=\"es-row-pedidos\"");        // instancia no grupo de origem
        System.Text.RegularExpressions.Regex.Matches(html, "data-menu-key=\"pedidos\"").Count.Should().Be(2);
    }

    [Fact]
    public void Badges_aparecem_com_data_badge_e_valor()
    {
        var html = Render("/dashboard", null, kds: true, Resumo(criticos: 2, vencidos: 5, pedidos: 3));

        html.Should().Contain("data-badge=\"pedidos-abertos\"");
        html.Should().Contain("data-badge=\"produtos-criticos\"");
        html.Should().Contain("data-badge=\"lotes-vencidos\"");
        // Dashboard = criticos + vencidos = 7.
        html.Should().Contain("data-badge=\"dashboard-total\"");
        html.Should().Contain(">7</span>");
    }

    [Fact]
    public void Meu_dia_some_quando_sem_favoritos()
    {
        var html = Render("/dashboard", Array.Empty<string>(), kds: false, Resumo(0, 0, 0));
        html.Should().NotContain("data-meu-dia");
    }

    [Fact]
    public void Flag_kds_off_nao_renderiza_kds_operacao()
    {
        var html = Render("/dashboard", Array.Empty<string>(), kds: false, Resumo(0, 0, 0));
        html.Should().NotContain("data-menu-key=\"kds-operacao\"");
    }

    private sealed class FakeSession : ISession
    {
        public bool IsAvailable => true;
        public string Id => "test";
        public IEnumerable<string> Keys => Array.Empty<string>();
        public void Clear() { }
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public void Set(string key, byte[] value) { }
        public bool TryGetValue(string key, out byte[] value) { value = null!; return false; }
    }
}
