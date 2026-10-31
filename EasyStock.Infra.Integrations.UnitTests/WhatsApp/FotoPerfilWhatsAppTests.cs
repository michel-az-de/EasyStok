using System.Net;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Infra.Integrations.UnitTests.WhatsApp.Waha;
using EasyStock.Infra.Integrations.WhatsApp;
using EasyStock.TestHelpers.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Infra.Integrations.UnitTests.WhatsApp;

/// <summary>#2155: foto do perfil pela ponte e o download dela no CDN do WhatsApp.</summary>
public class FotoPerfilWhatsAppTests
{
    private const string UrlDaFoto = "https://pps.whatsapp.net/v/t61/foto.jpg?oh=abc";
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    private sealed class CdnFalso(HttpStatusCode status = HttpStatusCode.OK, string tipo = "image/jpeg") : HttpMessageHandler
    {
        public List<HttpRequestMessage> Pedidos { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Pedidos.Add(request);
            var resposta = new HttpResponseMessage(status) { Content = new ByteArrayContent(Jpeg) };
            resposta.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(tipo);
            return Task.FromResult(resposta);
        }
    }

    private static BaixadorFotoPerfilWhatsApp Baixador(CdnFalso cdn) =>
        new(new HttpClient(cdn), NullLogger<BaixadorFotoPerfilWhatsApp>.Instance);

    [Theory]
    [InlineData(UrlDaFoto, true)]
    [InlineData("http://pps.whatsapp.net/foto.jpg", false)]
    [InlineData("https://pps.whatsapp.net.evil.com/foto.jpg", false)]
    [InlineData("https://169.254.169.254/latest", false)]
    [InlineData("api/files/foto.jpg", false)]
    [InlineData(null, false)]
    public void SoSegueHttpsDoCdnDoWhatsApp(string? url, bool permitido) =>
        BaixadorFotoPerfilWhatsApp.EnderecoPermitido(url, out _).Should().Be(permitido);

    [Fact]
    public async Task WahaDevolveAFotoDoPerfilSemMandarAChaveAoCdn()
    {
        var waha = new WahaFalso();
        waha.Responder("api/contacts/profile-picture", HttpStatusCode.OK, $$"""{"profilePictureURL":"{{UrlDaFoto}}"}""");
        var cdn = new CdnFalso();

        var foto = await waha.Cliente(fotos: Baixador(cdn)).ObterFotoPerfilAsync(WahaFalso.TelefoneDoCliente);

        foto!.Conteudo.Should().Equal(Jpeg);
        foto.MimeType.Should().Be("image/jpeg");
        waha.Em("api/contacts/profile-picture").Should().ContainSingle();
        cdn.Pedidos.Single().RequestUri!.ToString().Should().Be(UrlDaFoto);
        cdn.Pedidos.Single().Headers.Contains("X-Api-Key").Should().BeFalse("a chave do WAHA nunca sai para fora da rede interna");
    }

    [Fact]
    public async Task WahaSemFotoVisivelDevolveNulo()
    {
        var waha = new WahaFalso();
        waha.Responder("api/contacts/profile-picture", HttpStatusCode.OK, """{"profilePictureURL":null}""");
        var cdn = new CdnFalso();

        var foto = await waha.Cliente(fotos: Baixador(cdn)).ObterFotoPerfilAsync(WahaFalso.TelefoneDoCliente);

        foto.Should().BeNull();
        cdn.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task WppConnectDevolveAFotoDoPerfilPeloEurl()
    {
        var wpp = new HttpFalso();
        var sessao = new CredencialWppConnect("ez-c8ebd039563e4f34a606c5f7a788c156", "token-da-sessao");
        var remetente = Substitute.For<IRemetenteWhatsApp>();
        remetente.ObterSessaoWebAsync(Arg.Any<CancellationToken>()).Returns(sessao);
        wpp.Quando(HttpMethod.Get, $"/api/{sessao.Sessao}/profile-pic/5511999999999")
            .Responde(HttpStatusCode.OK, "{\"status\":\"success\",\"response\":{\"eurl\":\"" + UrlDaFoto + "\",\"id\":{\"_serialized\":\"5511999999999@c.us\"}}}");
        var cdn = new CdnFalso();
        var cliente = new WppConnectClient(wpp.CriarClient("http://wpp.test/"),
            Options.Create(new WppConnectOptions { Habilitado = true, SecretKey = "segredo-teste", WebhookToken = "token-webhook-teste" }),
            remetente, Baixador(cdn));

        var foto = await cliente.ObterFotoPerfilAsync("5511999999999");

        foto!.Conteudo.Should().Equal(Jpeg);
        wpp.UltimaChamada!.Cabecalho("Authorization").Should().Be("Bearer token-da-sessao");
        cdn.Pedidos.Single().Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task CdnQueNaoDevolveImagemNaoViraFoto()
    {
        var foto = await Baixador(new CdnFalso(tipo: "text/html")).BaixarAsync(UrlDaFoto, default);

        foto.Should().BeNull();
    }
}
