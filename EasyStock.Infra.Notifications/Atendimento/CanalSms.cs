using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Notifications.Atendimento;

/// <summary>
/// SMS na porta de canal (S37, ADR-0051): só texto, pelo provedor ativo
/// (<c>Notifications:Sms:Provider</c>, Twilio em produção). O contato da conversa vem em dígitos
/// E.164 sem <c>+</c> (S34); o provedor espera com <c>+</c>.
/// </summary>
public sealed class CanalSms([FromKeyedServices("sms:active")] IProvedorSms provedor) : ICanalMensageria
{
    public CanalConversa Canal => CanalConversa.Sms;

    public async Task<string> EnviarTextoAsync(string contatoIdExterno, string texto, CancellationToken ct = default)
    {
        var destino = contatoIdExterno.StartsWith('+') ? contatoIdExterno : "+" + contatoIdExterno;
        var mensagem = new MensagemPronta(Guid.NewGuid(), Guid.Empty, destino, "", texto,
            CanalNotificacao.Sms, CategoriaConteudoNotificacao.Transacional);

        var resultado = await provedor.EnviarAsync(mensagem, ct);
        if (!resultado.Sucesso)
            throw new EnvioCanalFalhouException(resultado.ErroDetalhado ?? "Falha no envio de SMS.", resultado.FalhaPermanente);

        return mensagem.OutboxId.ToString("N");
    }

    public Task<string> EnviarImagemAsync(string contatoIdExterno, string urlPublica, string? legenda = null, CancellationToken ct = default) =>
        throw new NotSupportedException("SMS não envia imagem.");

    public Task<string> EnviarBotoesAsync(string contatoIdExterno, string corpo, IReadOnlyList<(string Id, string Titulo)> botoes, CancellationToken ct = default) =>
        throw new NotSupportedException("SMS não envia botões.");

    public Task<string> EnviarModeloAsync(string contatoIdExterno, string nome, string idioma, IReadOnlyList<string> parametros, CancellationToken ct = default) =>
        throw new NotSupportedException("SMS não tem modelo aprovado: não tem janela.");

    // SMS não tem confirmação de leitura.
    public Task MarcarComoLidaAsync(string idMensagemExterna, CancellationToken ct = default) => Task.CompletedTask;
}
