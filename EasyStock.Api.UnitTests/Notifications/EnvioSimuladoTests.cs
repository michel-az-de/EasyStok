using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Async;
using EasyStock.Infra.Async.DependencyInjection;
using EasyStock.Infra.Notifications.Email;
using EasyStock.Infra.Notifications.Sms;
using EasyStock.Infra.Notifications.WhatsApp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// N2: stub e console dizem a verdade. O provider que não envia nada devolve <see cref="DesfechoEnvio.Simulado"/>
/// com o nome real (<c>stub</c>, <c>console</c>), nunca <c>smtp</c> nem sucesso comum, e o outbox deixa de
/// ficar <c>Enviado</c> sem nada ter saído.
/// </summary>
public class EnvioSimuladoTests
{
    private const string Telefone = "+5511999990001";
    private const string Email = "maria.souza@example.com";

    private static MensagemPronta Mensagem(string destinatario, CanalNotificacao canal) =>
        new(Guid.NewGuid(), Guid.NewGuid(), destinatario, "Assunto", "Corpo", canal, CategoriaConteudoNotificacao.Transacional);

    [Fact]
    public async Task Stub_de_whatsapp_devolve_Simulado_com_provider_stub()
    {
        var stub = new StubWhatsAppProvider(NullLogger<StubWhatsAppProvider>.Instance);
        var mensagem = Mensagem(Telefone, CanalNotificacao.WhatsApp);

        var resultado = await stub.EnviarAsync(mensagem);

        resultado.Desfecho.Should().Be(DesfechoEnvio.Simulado);
        resultado.ProviderUsado.Should().Be("stub");
        resultado.Sucesso.Should().BeTrue("fora do motor Simulado segue como sucesso");
        stub.MensagensEnviadas.Should().ContainSingle().Which.Should().BeSameAs(mensagem);
    }

    [Fact]
    public async Task Stub_de_sms_devolve_Simulado_com_provider_stub()
    {
        var stub = new StubSmsProvider(NullLogger<StubSmsProvider>.Instance);
        var mensagem = Mensagem(Telefone, CanalNotificacao.Sms);

        var resultado = await stub.EnviarAsync(mensagem);

        resultado.Desfecho.Should().Be(DesfechoEnvio.Simulado);
        resultado.ProviderUsado.Should().Be("stub");
        resultado.Sucesso.Should().BeTrue("fora do motor Simulado segue como sucesso");
        stub.MensagensEnviadas.Should().ContainSingle().Which.Should().BeSameAs(mensagem);
    }

    [Fact]
    public async Task Stub_com_SimularFalha_continua_sendo_falha_comum()
    {
        var whatsapp = await new StubWhatsAppProvider(NullLogger<StubWhatsAppProvider>.Instance) { SimularFalha = true }
            .EnviarAsync(Mensagem(Telefone, CanalNotificacao.WhatsApp));
        var sms = await new StubSmsProvider(NullLogger<StubSmsProvider>.Instance) { SimularFalha = true }
            .EnviarAsync(Mensagem(Telefone, CanalNotificacao.Sms));

        foreach (var resultado in new[] { whatsapp, sms })
        {
            resultado.Desfecho.Should().Be(DesfechoEnvio.FalhaTransitoria);
            resultado.Sucesso.Should().BeFalse();
            resultado.ProviderUsado.Should().Be("stub");
        }
    }

    [Fact]
    public async Task Canal_de_email_sobre_o_console_devolve_Simulado_e_nao_smtp()
    {
        var console = new ConsoleEmailService(NullLogger<ConsoleEmailService>.Instance);
        var canal = new SmtpEmailCanal(console, NullLogger<SmtpEmailCanal>.Instance);

        var resultado = await canal.EnviarAsync(Mensagem(Email, CanalNotificacao.Email));

        resultado.Desfecho.Should().Be(DesfechoEnvio.Simulado);
        resultado.ProviderUsado.Should().Be("console").And.NotBe("smtp");
        resultado.Sucesso.Should().BeTrue("fora do motor Simulado segue como sucesso");
    }

    [Fact]
    public async Task Canal_de_email_sobre_servico_real_devolve_Enviado_com_provider_smtp()
    {
        var email = Substitute.For<IEmailService>();
        var canal = new SmtpEmailCanal(email, NullLogger<SmtpEmailCanal>.Instance);

        var resultado = await canal.EnviarAsync(Mensagem(Email, CanalNotificacao.Email));

        resultado.Desfecho.Should().Be(DesfechoEnvio.Enviado);
        resultado.ProviderUsado.Should().Be("smtp");
        await email.Received(1).SendAsync(Email, "Assunto", "Corpo", true);
    }

    [Fact]
    public void So_o_console_e_simulador_de_email_e_o_nome_dele_continua_o_mesmo()
    {
        // O diagnóstico decide "SMTP configurado?" por GetType().Name == "ConsoleEmailService" (ver
        // DiagnosticoController e DiagnosticoEmailReportJob): o nome é contrato, e o canal usa a interface.
        var console = new ConsoleEmailService(NullLogger<ConsoleEmailService>.Instance);

        console.GetType().Name.Should().Be("ConsoleEmailService");
        console.Should().BeAssignableTo<IEmailServiceSimulado>().Which.Provider.Should().Be("console");
        typeof(SmtpEmailService).Should().NotBeAssignableTo<IEmailServiceSimulado>();
        typeof(SendGridEmailService).Should().NotBeAssignableTo<IEmailServiceSimulado>();
    }
}
