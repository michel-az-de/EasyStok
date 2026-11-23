using EasyStock.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace EasyStock.Api.UnitTests.Middleware;

/// <summary>
/// Issue #3251 (P2 da #3114): o APK de homologação declara o ambiente em todo pedido, e a API
/// recusa quem se declara outra coisa que não "oficial". Sem o cabeçalho, nada muda.
/// </summary>
public class TravaDeAmbienteMiddlewareTests
{
    private static (TravaDeAmbienteMiddleware mw, int[] chamadas) Build(string? aceita = null)
    {
        var chamadas = new int[1];
        var dados = new Dictionary<string, string?>();
        if (aceita is not null) dados[TravaDeAmbienteMiddleware.ChaveAceitaHomologacao] = aceita;
        var configuracao = new ConfigurationBuilder().AddInMemoryCollection(dados).Build();
        var mw = new TravaDeAmbienteMiddleware(
            _ => { chamadas[0]++; return Task.CompletedTask; },
            configuracao,
            NullLogger<TravaDeAmbienteMiddleware>.Instance);
        return (mw, chamadas);
    }

    private static DefaultHttpContext Ctx(params string[]? valores)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Path = "/api/mobile/devices/pair";
        if (valores is not null) ctx.Request.Headers[TravaDeAmbienteMiddleware.Cabecalho] = new StringValues(valores);
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static string Corpo(HttpContext ctx)
    {
        ctx.Response.Body.Position = 0;
        return new StreamReader(ctx.Response.Body).ReadToEnd();
    }

    [Theory]
    [InlineData("homologacao")]
    [InlineData("")]
    [InlineData("OFICIAL")]
    [InlineData("oficial ")]
    [InlineData("producao")]
    public async Task Cliente_que_se_declara_outro_ambiente_e_recusado_por_padrao(string valor)
    {
        var (mw, chamadas) = Build();
        var ctx = Ctx(valor);

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        chamadas[0].Should().Be(0, "a recusa vem antes de autenticar, parear ou gravar");
        Corpo(ctx).Should().Contain("AMBIENTE_RECUSADO");
    }

    [Fact]
    public async Task Cabecalho_repetido_e_recusado_mesmo_com_oficial()
    {
        var (mw, chamadas) = Build();
        var ctx = Ctx("oficial", "homologacao");

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        chamadas[0].Should().Be(0);
    }

    [Fact]
    public async Task Cliente_sem_cabecalho_segue_como_hoje()
    {
        var (mw, chamadas) = Build();
        var ctx = Ctx(null);

        await mw.InvokeAsync(ctx);

        chamadas[0].Should().Be(1, "APK oficial, Console, Web e PWA não mandam o cabeçalho");
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Cliente_que_se_declara_oficial_segue()
    {
        var (mw, chamadas) = Build();
        await mw.InvokeAsync(Ctx("oficial"));
        chamadas[0].Should().Be(1);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("nao")]
    public async Task So_a_chave_verdadeira_desliga_a_trava(string valor)
    {
        var (mw, chamadas) = Build(valor);
        var ctx = Ctx("homologacao");

        await mw.InvokeAsync(ctx);

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        chamadas[0].Should().Be(0);
    }

    [Fact]
    public async Task Desenvolvimento_e_laboratorio_com_a_chave_aceitam_a_homologacao()
    {
        var (mw, chamadas) = Build("true");
        await mw.InvokeAsync(Ctx("homologacao"));
        chamadas[0].Should().Be(1);
    }

    [Fact]
    public async Task Cors_libera_o_cabecalho_no_preflight_e_a_recusa_leva_a_origem()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins("https://localhost").AllowAnyHeader().AllowAnyMethod()));
        var provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);
        app.UseCors();
        app.UseMiddleware<TravaDeAmbienteMiddleware>();
        app.Run(ctx => { ctx.Response.StatusCode = StatusCodes.Status200OK; return Task.CompletedTask; });
        var pipeline = app.Build();

        var preflight = new DefaultHttpContext { RequestServices = provider };
        preflight.Request.Method = "OPTIONS";
        preflight.Request.Path = "/api/mobile/sync";
        preflight.Request.Headers.Origin = "https://localhost";
        preflight.Request.Headers.AccessControlRequestMethod = "POST";
        preflight.Request.Headers.AccessControlRequestHeaders = "x-cdb-ambiente,content-type";
        await pipeline(preflight);

        preflight.Response.StatusCode.Should().Be(StatusCodes.Status204NoContent);
        preflight.Response.Headers.AccessControlAllowHeaders.ToString().Should().ContainEquivalentOf("x-cdb-ambiente");

        // O CORS aplica a origem quando a resposta começa; aqui não há servidor, então o teste dispara.
        var resposta = new RespostaQueComeca();
        var recursos = new FeatureCollection();
        recursos.Set<IHttpRequestFeature>(new HttpRequestFeature());
        recursos.Set<IHttpResponseFeature>(resposta);
        recursos.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(new MemoryStream()));
        var pedido = new DefaultHttpContext(recursos) { RequestServices = provider };
        pedido.Request.Method = "POST";
        pedido.Request.Path = "/api/mobile/sync";
        pedido.Request.Headers.Origin = "https://localhost";
        pedido.Request.Headers[TravaDeAmbienteMiddleware.Cabecalho] = "homologacao";
        await pipeline(pedido);
        await resposta.ComecarAsync();

        pedido.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        pedido.Response.Headers.AccessControlAllowOrigin.ToString().Should().Be("https://localhost",
            "sem a origem o WebView esconde o motivo da recusa");
    }

    private sealed class RespostaQueComeca : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Acao, object Estado)> _aoComecar = [];

        public override void OnStarting(Func<object, Task> callback, object state) => _aoComecar.Add((callback, state));

        public async Task ComecarAsync()
        {
            for (var i = _aoComecar.Count - 1; i >= 0; i--) await _aoComecar[i].Acao(_aoComecar[i].Estado);
        }
    }
}
