using System.Net;
using System.Text;
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
/// #2507 (N1 da auditoria de 13/11): o toast do fechamento decide pelo sinal tipado que a API devolve
/// (<c>lancamentosForaDoFechamento</c>), nunca pela frase das observações. A frase mudou na #2256 e o aviso
/// ficou cego: o fechamento com lançamentos órfãos aparecia como sucesso. O JSON aqui é o shape que
/// <c>FechamentoCaixaResult</c> serializa; o teste do use case trava o mesmo nome de campo do outro lado.
/// </summary>
public class CaixaFechamentoAvisoTests
{
    private const string EmpresaId = "11111111-1111-1111-1111-111111111111";

    // Texto que o FecharCaixaUseCase grava desde a #2256.
    private const string AvisoAtual =
        "Atenção: há lançamentos entre 12/10/2026 e 27/10/2026 nesta sessão que não entraram neste fechamento. " +
        "Revise e reconcilie manualmente.";

    private sealed class RespostaFixa(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
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

    private static CaixaController Montar(string? observacoes, bool? foraDoFechamento)
    {
        var obs = observacoes is null ? "null" : System.Text.Json.JsonSerializer.Serialize(observacoes);
        var sinal = foraDoFechamento is null ? "" : $""","lancamentosForaDoFechamento":{(foraDoFechamento.Value ? "true" : "false")}""";
        var json = """{"data":{"id":"22222222-2222-2222-2222-222222222222","empresaId":""" + $"\"{EmpresaId}\"" +
            ""","data":"2026-10-11","saldoInicial":100,"saldoFinal":350,"observacoes":""" + obs +
            ""","fechadoEm":"2026-10-28T12:00:00Z" """ + sinal + """},"meta":{}}""";
        var api = new ApiClient(new HttpClient(new RespostaFixa(json)) { BaseAddress = new Uri("http://api.test/") },
            NullLogger<ApiClient>.Instance);
        var http = new DefaultHttpContext { Session = new SessaoEmMemoria() };
        http.Session.SetString("empresa_atual_id", EmpresaId);
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(http);
        var sessao = new SessionService(accessor);
        return new CaixaController(new CaixaService(api, sessao), sessao)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, Substitute.For<ITempDataProvider>()),
        };
    }

    [Fact]
    public async Task Fechamento_com_lancamentos_fora_avisa()
    {
        var controller = Montar(AvisoAtual, foraDoFechamento: true);

        await controller.Fechar(null);

        controller.TempData["Toast"]!.ToString().Should().StartWith("warning|")
            .And.Contain("11/10/2026");
    }

    [Fact]
    public async Task Fechamento_sem_lancamentos_fora_e_sucesso_mesmo_com_nota_parecida()
    {
        // A decisão é do sinal: a nota do operador (ou a frase antiga gravada em fechamento legado) não liga o aviso.
        var controller = Montar("Conferi tudo, nao foram incluidos os vales.", foraDoFechamento: false);

        await controller.Fechar(null);

        controller.TempData["Toast"]!.ToString().Should().StartWith("success|");
    }

    [Fact]
    public async Task Api_sem_o_campo_e_sucesso()
    {
        // Api antiga (ou fechamento idempotente) não traz o sinal: não inventa aviso.
        var controller = Montar(null, foraDoFechamento: null);

        await controller.Fechar(null);

        controller.TempData["Toast"]!.ToString().Should().StartWith("success|");
    }
}
