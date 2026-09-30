using System.Net;
using System.Reflection;
using System.Text;
using EasyStock.Api.Controllers;
using EasyStock.Api.Services.Operacao;
using EasyStock.Application.Ports.Output;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// S18 (#1146): <c>GET api/operacao/eventos</c> é SSE autenticado por JWT, filtra pela empresa da claim e
/// manda heartbeat a cada 25 s.
/// </summary>
public class OperacaoEventosControllerTests
{
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task SemJwt401()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(new OperacaoEventBroker(NullLogger<OperacaoEventBroker>.Instance));
        builder.Services.AddScoped(_ => Substitute.For<ICurrentUserAccessor>());
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = "teste",
                ValidAudience = "teste",
                IssuerSigningKey = new SymmetricSecurityKey(new byte[32]),
            });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers()
            .ConfigureApplicationPartManager(m =>
            {
                m.ApplicationParts.Add(new Microsoft.AspNetCore.Mvc.ApplicationParts.AssemblyPart(typeof(OperacaoEventosController).Assembly));
                m.FeatureProviders.Clear();
                m.FeatureProviders.Add(new SoOperacaoEventos());
            });

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();

        var endereco = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var http = new HttpClient { BaseAddress = new Uri(endereco) };

        var semToken = await http.GetAsync("api/operacao/eventos");
        var tokenInvalido = new HttpRequestMessage(HttpMethod.Get, "api/operacao/eventos");
        tokenInvalido.Headers.Authorization = new("Bearer", "nao-e-um-jwt");
        var comTokenInvalido = await http.SendAsync(tokenInvalido);

        semToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        comTokenInvalido.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await app.StopAsync();
    }

    [Fact]
    public async Task RecebePedidoPagoDaPropriaEmpresaENaoDeOutra()
    {
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        var broker = new OperacaoEventBroker(NullLogger<OperacaoEventBroker>.Instance);
        var (controller, corpo) = Controller(broker, empresaA);
        using var cts = new CancellationTokenSource(Limite);

        var stream = controller.Get(cts.Token);
        await Aguardar(() => broker.Ouvintes == 1);
        var pedidoB = Guid.NewGuid();
        var pedidoA = Guid.NewGuid();
        broker.PublicarOperacao(empresaB, "pedido.pago", new { pedidoId = pedidoB });
        broker.PublicarOperacao(empresaA, "pedido.pago", new { pedidoId = pedidoA });
        await Aguardar(() => corpo.Texto().Contains(pedidoA.ToString()));
        await cts.CancelAsync();
        await stream;

        controller.Response.ContentType.Should().Be("text/event-stream");
        corpo.Texto().Should().Contain($"event: pedido.pago\ndata: {{\"pedidoId\":\"{pedidoA}\"}}\n\n")
            .And.NotContain(pedidoB.ToString());
        broker.Ouvintes.Should().Be(0, "o ouvinte sai do broker quando o cliente desconecta");
    }

    [Fact]
    public async Task SemEmpresaNaClaim403()
    {
        var broker = new OperacaoEventBroker(NullLogger<OperacaoEventBroker>.Instance);
        var (controller, _) = Controller(broker, Guid.Empty);

        await controller.Get(CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        broker.Ouvintes.Should().Be(0);
    }

    [Fact]
    public void ExigeAutorizacaoSemExcecaoAnonima()
    {
        typeof(OperacaoEventosController).GetCustomAttributes<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .Should().NotBeEmpty();
        typeof(OperacaoEventosController).GetMethods()
            .SelectMany(m => m.GetCustomAttributes<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>())
            .Should().BeEmpty();
    }

    [Fact]
    public void HeartbeatPadraoDe25Segundos() =>
        OperacaoEventosController.IntervaloHeartbeat.Should().Be(TimeSpan.FromSeconds(25));

    [Fact]
    public async Task HeartbeatChegaNoIntervalo()
    {
        var broker = new OperacaoEventBroker(NullLogger<OperacaoEventBroker>.Instance);
        using var ouvinte = broker.SubscribeOperacao("hb", Guid.NewGuid());
        var context = new DefaultHttpContext();
        var corpo = new CorpoObservavel();
        context.Response.Body = corpo;
        using var cts = new CancellationTokenSource(Limite);

        var stream = TransmissaoSse.TransmitirAsync(context.Response, ouvinte.Slot, TimeSpan.FromMilliseconds(50), cts.Token);
        await Aguardar(() => Ocorrencias(corpo.Texto(), ": heartbeat\n\n") >= 2);
        await cts.CancelAsync();
        await stream;

        corpo.Texto().Should().StartWith("event: ready\n");
    }

    private static (OperacaoEventosController, CorpoObservavel) Controller(OperacaoEventBroker broker, Guid empresaId)
    {
        var usuario = Substitute.For<ICurrentUserAccessor>();
        usuario.EmpresaId.Returns(empresaId);
        var corpo = new CorpoObservavel();
        var context = new DefaultHttpContext();
        context.Response.Body = corpo;
        var controller = new OperacaoEventosController(broker, usuario)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        return (controller, corpo);
    }

    private static async Task Aguardar(Func<bool> condicao)
    {
        var fim = DateTime.UtcNow + Limite;
        while (!condicao())
        {
            if (DateTime.UtcNow > fim) throw new TimeoutException("condição não atingida");
            await Task.Delay(10);
        }
    }

    private static int Ocorrencias(string texto, string trecho)
    {
        var n = 0;
        for (var i = texto.IndexOf(trecho, StringComparison.Ordinal); i >= 0; i = texto.IndexOf(trecho, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }

    private sealed class SoOperacaoEventos : ControllerFeatureProvider
    {
        protected override bool IsController(TypeInfo typeInfo) => typeInfo.AsType() == typeof(OperacaoEventosController);
    }

    /// <summary>Corpo da resposta legível enquanto o stream ainda escreve.</summary>
    private sealed class CorpoObservavel : MemoryStream
    {
        private readonly object _trava = new();

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (_trava) base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            lock (_trava) base.Write(buffer);
        }

        public string Texto()
        {
            lock (_trava) return Encoding.UTF8.GetString(ToArray());
        }
    }
}
