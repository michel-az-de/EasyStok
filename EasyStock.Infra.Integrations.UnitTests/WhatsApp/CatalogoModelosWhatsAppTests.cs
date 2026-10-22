using System.Net;
using System.Text;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Integrations.WhatsApp;
using FluentAssertions;
using NSubstitute;

namespace EasyStock.Infra.Integrations.UnitTests.WhatsApp;

public class CatalogoModelosWhatsAppTests
{
    private readonly IEmpresaRepository _empresas = Substitute.For<IEmpresaRepository>();
    private readonly IIntegrationCredentialResolver _credenciais = Substitute.For<IIntegrationCredentialResolver>();
    private readonly Empresa _empresa = Empresa.Criar("Teste", "11111111000191");
    private readonly Handler _handler = new();
    private CatalogoModelosWhatsApp Catalogo => new(new HttpClient(_handler) { BaseAddress = new Uri("https://meta.test/v1/") }, _empresas, _credenciais);
    public CatalogoModelosWhatsAppTests()
    {
        _empresa.VincularWhatsApp("1234");
        _empresas.GetByIdAsync(_empresa.Id).Returns(_empresa);
        Credencial("1234");
    }
    private void Credencial(string numero) => _credenciais.ObterAsync<CredencialWhatsAppMeta>(
        _empresa.Id, CredencialWhatsAppMeta.ProviderKey, AmbienteIntegracao.Production, Arg.Any<CancellationToken>())
        .Returns(new CredencialWhatsAppMeta("token-sintetico", "waba1", numero, DateTime.UtcNow));

    [Fact]
    public async Task ContaETokenDoTenant_PaginaPorCursor_SoAprovados_ParametrosNumerados()
    {
        _handler.Respostas.Enqueue("""{"data":[{"name":"retomar","language":"pt_BR","category":"UTILITY","status":"APPROVED","components":[{"type":"BODY","text":"Olá {{1}}, {{2}}. {{1}}"}]},{"name":"pendente","status":"PENDING"}],"paging":{"next":"https://outra.test/?access_token=segredo","cursors":{"after":"cursor/+"}}} """);
        _handler.Respostas.Enqueue("""{"data":[{"name":"english","language":"en_US","category":"UTILITY","status":"APPROVED","components":[{"type":"BODY","text":"Hello"}]}]}""");
        var result = await Catalogo.ListarAsync(_empresa.Id);
        result.Should().HaveCount(2);
        result.Single(m => m.Nome == "retomar").QuantidadeParametros.Should().Be(2);
        result.Should().OnlyContain(m => m.Indisponivel == null);
        _handler.Urls.Should().HaveCount(2).And.OnlyContain(u => u.StartsWith("https://meta.test/v1/waba1/message_templates?"));
        _handler.Urls[1].Should().Contain("after=cursor%2F%2B");
        _handler.Tokens.Should().OnlyContain(t => t == "token-sintetico");
    }

    [Theory]
    [InlineData("\"HEADER\",\"format\":\"IMAGE\"")]
    [InlineData("\"BUTTONS\"")]
    [InlineData("\"HEADER\",\"format\":\"TEXT\",\"text\":\"Oi {{1}}\"")]
    public async Task ModelosComRecursosAusentes_SaoVisiveisComMotivo(string componente)
    {
        _handler.Respostas.Enqueue("{\"data\":[{\"name\":\"modelo\",\"language\":\"pt_BR\",\"category\":\"UTILITY\",\"status\":\"APPROVED\",\"components\":[{\"type\":\"BODY\",\"text\":\"Olá\"},{\"type\":" + componente + "}]}]}");
        (await Catalogo.ListarAsync(_empresa.Id)).Single().Indisponivel.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("Olá {{nome}}")]
    [InlineData("Olá {{2}}")]
    [InlineData("Olá {{100000000000000}}")]
    public async Task ParametrosNaoSuportados_NaoSaoOferecidosParaEnvio(string texto)
    {
        _handler.Respostas.Enqueue("{\"data\":[{\"name\":\"modelo\",\"language\":\"pt_BR\",\"category\":\"UTILITY\",\"status\":\"APPROVED\",\"components\":[{\"type\":\"BODY\",\"text\":\"" + texto + "\"}]}]}");
        (await Catalogo.ListarAsync(_empresa.Id)).Single().Indisponivel.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmpresaAlheiaOuCredencialDeOutroNumero_RecusaAntesDaRede(bool outroNumero)
    {
        if (outroNumero) Credencial("9999");
        var act = () => Catalogo.ListarAsync(outroNumero ? _empresa.Id : Guid.NewGuid());
        (await act.Should().ThrowAsync<CatalogoModelosException>()).Which.FaltaConexao.Should().BeTrue("#1765-9: vira 409, não 502");
        _handler.Urls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(403, "{\"error\":\"token-secreto\"}")]
    [InlineData(200, "invalido")]
    [InlineData(200, "{}")]
    public async Task FalhaDaMeta_NaoViraListaVaziaNemExpoeSegredo(int status, string corpo)
    {
        _handler.Status = (HttpStatusCode)status;
        _handler.Respostas.Enqueue(corpo);
        var act = () => Catalogo.ListarAsync(_empresa.Id);
        var erro = (await act.Should().ThrowAsync<CatalogoModelosException>()).Which;
        erro.Message.Should().NotContain("token-secreto");
        erro.FaltaConexao.Should().BeFalse("falha da Meta segue 502");
    }

    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        public List<string?> Tokens { get; } = [];
        public Queue<string> Respostas { get; } = new();
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString()); Tokens.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Respostas.Dequeue(), Encoding.UTF8, "application/json") });
        }
    }
}
