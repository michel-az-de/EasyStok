using System.Net;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using FluentAssertions;
using NSubstitute;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace EasyStock.Infra.Async.UnitTests.Email;

/// <summary>
/// N3 (#1351): o SendGrid fica no ar, mas o <c>EnviarAsync</c> dele diz a verdade: 4xx (menos 408 e 429) e permanente, o
/// resto e transitorio, o sandbox devolve 2xx sem entregar (entao e simulado, nao enviado) e o detalhe leva so o status,
/// porque o corpo da resposta pode repetir o endereco do destinatario.
/// </summary>
public class SendGridEmailServiceTests
{
    private static (SendGridEmailService Servico, ISendGridClient Cliente) Montar(HttpStatusCode status, string corpo = "", bool sandbox = false)
    {
        var cliente = Substitute.For<ISendGridClient>();
        cliente.SendEmailAsync(Arg.Any<SendGridMessage>(), Arg.Any<CancellationToken>())
            .Returns(new Response(status, new StringContent(corpo), null!));
        return (new SendGridEmailService(cliente, "avisos@easystok.online", "EasyStok", sandbox), cliente);
    }

    private static MensagemEmail Mensagem() => new("maria.souza@example.com", "Assunto", "<p>ok</p>", Html: true);

    [Fact]
    public async Task Status202ViraEnviado()
    {
        var (servico, _) = Montar(HttpStatusCode.Accepted);

        var resultado = await servico.EnviarAsync(Mensagem());

        resultado.Desfecho.Should().Be(DesfechoEnvio.Enviado);
        resultado.ProviderUsado.Should().Be("sendgrid");
    }

    [Fact]
    public async Task SandboxViraSimuladoPorqueONadaSai()
    {
        // O sandbox valida a requisicao e devolve 2xx sem entregar: dizer "Enviado" faria o operador achar que a
        // senha temporaria chegou.
        var (servico, _) = Montar(HttpStatusCode.OK, sandbox: true);

        var resultado = await servico.EnviarAsync(Mensagem());

        resultado.Desfecho.Should().Be(DesfechoEnvio.Simulado);
        resultado.ProviderUsado.Should().Be("sendgrid");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData((HttpStatusCode)413)]
    public async Task Status4xxExceto408E429EPermanente(HttpStatusCode status)
    {
        var (servico, _) = Montar(status);

        var resultado = await servico.EnviarAsync(Mensagem());

        resultado.Desfecho.Should().Be(DesfechoEnvio.FalhaPermanente);
        resultado.StatusHttp.Should().Be((int)status);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Status408429E5xxSaoTransitorios(HttpStatusCode status)
    {
        var (servico, _) = Montar(status);

        var resultado = await servico.EnviarAsync(Mensagem());

        resultado.Desfecho.Should().Be(DesfechoEnvio.FalhaTransitoria);
    }

    [Fact]
    public async Task DetalheLevaSoOStatusENuncaOCorpoDaResposta()
    {
        var (servico, _) = Montar(HttpStatusCode.BadRequest, "{\"errors\":[{\"message\":\"maria.souza@example.com is invalid\"}]}");

        var resultado = await servico.EnviarAsync(Mensagem());

        resultado.ErroDetalhado.Should().Be("SendGrid HTTP 400");
    }

    [Fact]
    public async Task ErroDeRedeViraFalhaTransitoriaSemOMensagemDaExcecao()
    {
        var cliente = Substitute.For<ISendGridClient>();
        cliente.SendEmailAsync(Arg.Any<SendGridMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Response>(new HttpRequestException("falha para maria.souza@example.com")));
        var servico = new SendGridEmailService(cliente, "avisos@easystok.online", null);

        var resultado = await servico.EnviarAsync(Mensagem());

        resultado.Desfecho.Should().Be(DesfechoEnvio.FalhaTransitoria);
        resultado.ErroDetalhado.Should().Be(nameof(HttpRequestException));
    }

    [Fact]
    public async Task CancelamentoDoChamadorPropaga()
    {
        var cliente = Substitute.For<ISendGridClient>();
        cliente.SendEmailAsync(Arg.Any<SendGridMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Response>(new OperationCanceledException()));
        var servico = new SendGridEmailService(cliente, "avisos@easystok.online", null);
        using var cancelamento = new CancellationTokenSource();
        await cancelamento.CancelAsync();

        var act = async () => await servico.EnviarAsync(Mensagem(), cancelamento.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void ChaveDaApiVaziaRecusaConstruirNomeandoAChave()
    {
        var act = () => new SendGridEmailService("  ", "avisos@easystok.online", null);

        act.Should().Throw<ArgumentException>().WithMessage("*SendGrid:ApiKey*");
    }
}
