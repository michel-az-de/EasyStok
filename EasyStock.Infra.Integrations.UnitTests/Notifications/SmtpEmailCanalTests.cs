using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Notifications.Email;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Infra.Integrations.UnitTests.Notifications;

/// <summary>
/// N3 (#1351): o canal de e-mail do outbox só traduz. Passa ao serviço a categoria (Segurança vira o remetente de
/// segurança), o token de cancelamento do chamador, e devolve o desfecho e o provider que o serviço informou, sem
/// classificar exceção nem fixar <c>smtp</c> (o console devolve <c>console</c>).
/// </summary>
public class SmtpEmailCanalTests
{
    private static MensagemPronta Mensagem(CategoriaConteudoNotificacao categoria = CategoriaConteudoNotificacao.Operacional) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "maria.souza@example.com", "Assunto", "<p>Corpo</p>",
            CanalNotificacao.Email, categoria);

    private static (SmtpEmailCanal Canal, IEmailService Servico) Montar(ResultadoEnvio? resultado = null)
    {
        var servico = Substitute.For<IEmailService>();
        servico.EnviarAsync(Arg.Any<MensagemEmail>(), Arg.Any<CancellationToken>())
            .Returns(resultado ?? new ResultadoEnvio(true, "smtp"));
        return (new SmtpEmailCanal(servico, NullLogger<SmtpEmailCanal>.Instance), servico);
    }

    [Fact]
    public async Task RepassaDesfechoEProviderDoServico()
    {
        var casos = new[]
        {
            new ResultadoEnvio(true, "smtp", DuracaoMs: 12),
            ResultadoEnvio.Simulado("console"),
            new ResultadoEnvio(false, "smtp", "SMTP 550: recusado", FalhaPermanente: true),
            new ResultadoEnvio(false, "smtp", "SMTP 421: indisponivel"),
        };

        foreach (var esperado in casos)
        {
            var (canal, _) = Montar(esperado);

            var resultado = await canal.EnviarAsync(Mensagem());

            resultado.Desfecho.Should().Be(esperado.Desfecho);
            resultado.ProviderUsado.Should().Be(esperado.ProviderUsado, "o provider é o do serviço: o console não vira smtp");
            resultado.Sucesso.Should().Be(esperado.Sucesso);
            resultado.FalhaPermanente.Should().Be(esperado.FalhaPermanente);
            resultado.ErroDetalhado.Should().Be(esperado.ErroDetalhado);
        }
    }

    [Theory]
    [InlineData(CategoriaConteudoNotificacao.Seguranca, RemetenteEmail.Seguranca)]
    [InlineData(CategoriaConteudoNotificacao.Operacional, RemetenteEmail.Avisos)]
    [InlineData(CategoriaConteudoNotificacao.Transacional, RemetenteEmail.Avisos)]
    [InlineData(CategoriaConteudoNotificacao.Marketing, RemetenteEmail.Avisos)]
    public async Task SegurancaUsaRemetenteDeSeguranca(CategoriaConteudoNotificacao categoria, RemetenteEmail esperado)
    {
        var (canal, servico) = Montar();

        await canal.EnviarAsync(Mensagem(categoria));

        await servico.Received(1).EnviarAsync(
            Arg.Is<MensagemEmail>(m => m.Remetente == esperado),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RepassaOTokenDeCancelamento()
    {
        var (canal, servico) = Montar();
        using var cancelamento = new CancellationTokenSource();

        await canal.EnviarAsync(Mensagem(), cancelamento.Token);

        await servico.Received(1).EnviarAsync(Arg.Any<MensagemEmail>(), cancelamento.Token);
    }

    [Fact]
    public async Task LevaDestinatarioAssuntoCorpoEmHtmlEORastroDoOutbox()
    {
        var (canal, servico) = Montar();
        var mensagem = Mensagem();

        await canal.EnviarAsync(mensagem);

        await servico.Received(1).EnviarAsync(
            Arg.Is<MensagemEmail>(m =>
                m.Destinatario == mensagem.Destinatario
                && m.Assunto == mensagem.Assunto
                && m.Corpo == mensagem.Corpo
                && m.Html
                && m.OutboxId == mensagem.OutboxId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExcecaoInesperadaDoServicoViraFalhaTransitoriaSemProviderFixo()
    {
        var servico = Substitute.For<IEmailService>();
        servico.EnviarAsync(Arg.Any<MensagemEmail>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ResultadoEnvio>(new InvalidOperationException("falha de maria.souza@example.com")));
        var canal = new SmtpEmailCanal(servico, NullLogger<SmtpEmailCanal>.Instance);

        var resultado = await canal.EnviarAsync(Mensagem());

        resultado.Desfecho.Should().Be(DesfechoEnvio.FalhaTransitoria);
        resultado.ProviderUsado.Should().BeNull("o canal não sabe qual provider falhou e não finge que foi o smtp");
        resultado.ErroDetalhado.Should().Be(nameof(InvalidOperationException), "o detalhe vai para o banco e não leva a mensagem da exceção");
    }

    [Fact]
    public async Task CancelamentoDoChamadorPropagaEmVezDeViraFalha()
    {
        var servico = Substitute.For<IEmailService>();
        servico.EnviarAsync(Arg.Any<MensagemEmail>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ResultadoEnvio>(new OperationCanceledException()));
        var canal = new SmtpEmailCanal(servico, NullLogger<SmtpEmailCanal>.Instance);

        var act = async () => await canal.EnviarAsync(Mensagem());

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
