using System.Text;
using EasyStock.Api.Controllers.Webhooks;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Domain.Integration;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Controllers.Webhooks;

public class WebhookWppConnectControllerTests
{
    private const string TokenDeTeste = "segredo-webhook-2071";
    private static readonly Guid Empresa = Guid.Parse("c8ebd039-563e-4f34-a606-c5f7a788c156");

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("curto", "curto", false)]
    [InlineData("segredo-webhook-2071", "segredo-webhook-2071", true)]
    [InlineData("segredo-webhook-outra", "segredo-webhook-2071", false)]
    [InlineData(null, "segredo-webhook-2071", false)]
    public void Callback_exige_token_valido(string? recebido, string? configurado, bool esperado) =>
        Assert.Equal(esperado, WebhookWppConnectController.TokenValido(recebido, configurado));

    [Fact]
    public async Task Callback_sem_identificador_registra_o_formato_sem_valor_de_cliente()
    {
        // #2591: o corpo recusado não fica gravado; o aviso precisa dizer o formato para a causa ser observável.
        var sessao = CredencialWppConnect.NomeSessao(Empresa);
        var raw = $$$"""{"event":"unreadmessages","session":"{{{sessao}}}","from":"5511999999999@c.us","type":"chat","body":"Quero um bolo de cenoura","t":1793000000,"sender":{"pushname":"Maria Teste"}}""";
        var credenciais = Substitute.For<IIntegrationCredentialResolver>();
        credenciais.ObterAsync<CredencialWppConnect>(Empresa, CredencialWppConnect.ProviderKey, AmbienteIntegracao.Production, Arg.Any<CancellationToken>())
            .Returns(new CredencialWppConnect(sessao, "token-da-sessao-de-teste"));
        var processar = new ProcessarWebhookWppConnectUseCase(credenciais, Substitute.For<ITenantContextAccessor>(),
            Substitute.For<IWppConnectSessaoClient>(), null!);
        var configuracao = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Atendimento:WppConnect:Habilitado"] = "true",
            ["Atendimento:WppConnect:WebhookToken"] = TokenDeTeste,
        }).Build();
        var registro = new RegistroEmMemoria();
        var controller = new WebhookWppConnectController(processar, configuracao, registro)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(raw));

        var resposta = await controller.Receber(TokenDeTeste, CancellationToken.None);

        resposta.Should().BeOfType<BadRequestObjectResult>();
        var aviso = registro.Mensagens.Should().ContainSingle().Subject;
        aviso.Should().Contain("Mensagem WPPConnect sem identificador.")
            .And.Contain("evento=unreadmessages tipo=chat campos{event:string(14),session:string(35),from:string(18),")
            .And.NotContainAny("5511999999999", "bolo", "Maria");
    }

    private sealed class RegistroEmMemoria : ILogger<WebhookWppConnectController>
    {
        public List<string> Mensagens { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Mensagens.Add(formatter(state, exception));
    }
}
