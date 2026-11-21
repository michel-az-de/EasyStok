using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using EasyStock.Web.Controllers;
using EasyStock.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Web.UnitTests.Controllers;

/// <summary>
/// #2727: o campo datetime-local da tela de Detalhe do pedido é horário de Brasília. Ele chega sem fuso
/// (Kind Unspecified) e a API trata data sem fuso como UTC, então agendar às 14:00 gravava 11:00. A tela
/// manda o instante em UTC, e o toast mostra a hora que a pessoa digitou.
/// </summary>
public class PedidoAgendarFusoTests
{
    private const string EmpresaId = "11111111-1111-1111-1111-111111111111";
    private const string PedidoId = "22222222-2222-2222-2222-222222222222";

    private sealed class Captura : HttpMessageHandler
    {
        public string? Corpo { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Corpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":{"id":"22222222-2222-2222-2222-222222222222","status":"aguardando"},"meta":{}}""",
                    Encoding.UTF8, "application/json")
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

    private static PedidosController Montar(Captura captura)
    {
        var api = new ApiClient(new HttpClient(captura) { BaseAddress = new Uri("http://api.test/") },
            NullLogger<ApiClient>.Instance);
        var http = new DefaultHttpContext { Session = new SessaoEmMemoria() };
        http.Session.SetString("empresa_atual_id", EmpresaId);
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(http);
        var sessao = new SessionService(accessor);
        return new PedidosController(new PedidosService(api, sessao), new ClientesService(api, sessao),
            new ProdutosService(api, sessao), sessao)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, Substitute.For<ITempDataProvider>()),
        };
    }

    [Fact]
    public async Task Agendar_pela_tela_manda_a_hora_de_Brasilia_em_UTC_e_mostra_a_hora_digitada()
    {
        var captura = new Captura();
        var controller = Montar(captura);
        // O que o model binding entrega para "2026-11-20T14:00" do datetime-local.
        var digitado = new DateTime(2026, 11, 20, 14, 0, 0, DateTimeKind.Unspecified);

        await controller.Agendar(PedidoId, digitado);

        using var corpo = JsonDocument.Parse(captura.Corpo!);
        // Lido como a API lê (DataUtc.ParaUtc: sem fuso é UTC), sem depender do fuso da máquina do teste.
        var enviado = DateTime.Parse(corpo.RootElement.GetProperty("agendadoParaEm").GetString()!, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        enviado.Should().Be(new DateTime(2026, 11, 20, 17, 0, 0, DateTimeKind.Utc));
        controller.TempData["Toast"]!.ToString().Should().Contain("20/11/2026 14:00");
    }

    [Fact]
    public async Task Remover_agendamento_segue_mandando_nulo()
    {
        var captura = new Captura();
        var controller = Montar(captura);

        await controller.Agendar(PedidoId, null);

        using var corpo = JsonDocument.Parse(captura.Corpo!);
        corpo.RootElement.GetProperty("agendadoParaEm").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
