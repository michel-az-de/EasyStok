using System.Diagnostics;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Notifications.Email;

/// <summary>
/// Canal de e-mail do outbox (N3, #1351). So traduz: manda a mensagem ao <see cref="IEmailService"/> com o token do
/// chamador e o remetente da categoria (<see cref="CategoriaConteudoNotificacao.Seguranca"/> sai da caixa de seguranca,
/// o resto da de avisos) e devolve o desfecho e o provider que o servico informou. Nao classifica excecao nem fixa
/// <c>smtp</c>: o console devolve <c>console</c>, e a unica camada de retentativa e o outbox.
/// </summary>
public sealed class SmtpEmailCanal(
    IEmailService emailService,
    ILogger<SmtpEmailCanal> logger) : ICanalNotificacao
{
    public CanalNotificacao Canal => CanalNotificacao.Email;

    public async Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
    {
        var remetente = mensagem.Categoria == CategoriaConteudoNotificacao.Seguranca
            ? RemetenteEmail.Seguranca
            : RemetenteEmail.Avisos;
        var sw = Stopwatch.StartNew();

        try
        {
            var resultado = await emailService.EnviarAsync(
                new MensagemEmail(
                    mensagem.Destinatario,
                    mensagem.Assunto,
                    mensagem.Corpo,
                    Html: true,
                    Remetente: remetente,
                    OutboxId: mensagem.OutboxId),
                ct);

            // Sem o endereco: dado pessoal fora do log (LGPD, #1292); o OutboxId leva a mensagem.
            logger.LogInformation(
                "Email outbox={OutboxId} categoria={Categoria} desfecho={Desfecho} provider={Provider} em {Ms}ms",
                mensagem.OutboxId, mensagem.Categoria, resultado.Desfecho, resultado.ProviderUsado, sw.ElapsedMilliseconds);

            return resultado;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Rede de seguranca: o servico de e-mail devolve falha de envio como desfecho e nao lanca. Se algo lancar
            // mesmo assim (um IEmailService alheio ao MailKit), o canal nao derruba o lote nem inventa provider, e o log
            // leva so o tipo da excecao: a mensagem dela pode repetir o endereco.
            sw.Stop();
            logger.LogError(
                "Falha inesperada ao enviar email outbox={OutboxId} categoria={Categoria} erro={Erro}",
                mensagem.OutboxId, mensagem.Categoria, ex.GetType().Name);

            return new ResultadoEnvio(
                Sucesso: false,
                ProviderUsado: null,
                ErroDetalhado: ex.GetType().Name,
                DuracaoMs: sw.ElapsedMilliseconds);
        }
    }
}
