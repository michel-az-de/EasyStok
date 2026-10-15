using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Async;
using EasyStock.Infra.Async.Email;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Email;

/// <summary>
/// Guarda anti-falso-verde dos testes com o Mailpit (N3, #1351), no molde do <see cref="HarnessCanaryTests"/>. Na CI a
/// env var <c>EASYSTOCK_IT_PG</c> existe (o mesmo Docker serve ao Postgres e ao Mailpit), entao este teste NAO pula: se o
/// container nao subir, os <c>[SkippableFact]</c> de <see cref="MailpitSmtpIntegrationTests"/> pulariam verdes e a
/// entrega real por MailKit sumiria sem alarme. Aqui isso vira VERMELHO. Localmente, sem a env var, pula VISIVEL
/// (<c>Skip.If</c>, ADR-0023).
///
/// Nao basta o container estar de pe: o canario envia uma mensagem pelo servico real e le de volta pela API do Mailpit.
/// </summary>
[Collection("Mailpit")]
public class MailpitCanaryTests(MailpitFixture mailpit)
{
    [SkippableFact]
    public async Task Mailpit_sobe_de_verdade_quando_ci()
    {
        var envPresente = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EASYSTOCK_IT_PG"));
        Skip.If(!envPresente, "EASYSTOCK_IT_PG ausente (rodada local sem o ambiente do CI).");

        mailpit.IsAvailable.Should().BeTrue(
            "EASYSTOCK_IT_PG setada (CI) mas o Mailpit/Testcontainers esta indisponivel: os testes de entrega real "
            + "por MailKit pulariam verdes: " + (mailpit.UnavailableReason ?? "(sem motivo)"));

        await mailpit.LimparAsync();
        var servico = new SmtpEmailService(
            new SmtpOpcoes
            {
                Host = mailpit.Host,
                Port = mailpit.PortaSmtp.ToString(),
                Modo = "Nenhum",
                FromEmail = "avisos@easystok.online",
            }.Resolver("Development"),
            NullLogger<SmtpEmailService>.Instance);

        var resultado = await servico.EnviarAsync(new MensagemEmail("canario@example.com", "Canario do Mailpit", "<p>ok</p>", Html: true));

        resultado.Desfecho.Should().Be(DesfechoEnvio.Enviado, "o canario prova o caminho inteiro, nao so o container de pe");
        var chegada = (await mailpit.AguardarAsync(1)).Single();
        chegada.Assunto.Should().Be("Canario do Mailpit");
        chegada.Para.Should().Equal("canario@example.com");
    }
}
