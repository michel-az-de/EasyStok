using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Avisos da cobrança do pedido na conversa de origem (S11): link novo depois da expiração, link da
/// troca de forma e cancelamento por falta de pagamento. Sai pela porta do canal da conversa
/// (<see cref="ResolvedorCanal"/>, S34) e fica gravado como <c>Mensagem(Saida, Sistema)</c>.
///
/// <para>
/// Best-effort: conversa encerrada, fora da janela de atendimento ou falha do canal não derruba a
/// operação de cobrança (que já foi gravada); só loga e devolve <c>false</c>. Aviso transacional: não
/// depende de consentimento de marketing (S38).
/// </para>
/// </summary>
public sealed class AvisoCobrancaConversa(
    IConversaRepository conversaRepository,
    ResolvedorCanal resolvedorCanal,
    IUnitOfWork unitOfWork,
    ILogger<AvisoCobrancaConversa> logger)
{
    public static string TextoNovoLink(string link) =>
        $"O link de pagamento anterior expirou. Aqui está um novo, válido por 30 minutos (Pix ou cartão, como preferir): {link}";

    public static string TextoLinkTrocado(string link) =>
        $"Aqui está o link para pagar seu pedido, válido por 30 minutos (Pix ou cartão, como preferir): {link}";

    /// <summary>S32: o Mercado Pago recusou o pagamento; o mesmo link continua valendo.</summary>
    public static string TextoPagamentoRecusado(string? link) => link is null
        ? "O pagamento não foi aprovado. Você pode tentar de novo com outro cartão ou pelo Pix."
        : $"O pagamento não foi aprovado. Você pode tentar de novo com outro cartão ou pelo Pix, no mesmo link: {link}";

    public const string TextoPedidoCancelado =
        "Seu pedido foi cancelado porque o pagamento não foi confirmado a tempo. Se ainda quiser, é só pedir de novo por aqui.";

    public async Task<bool> EnviarAsync(Guid empresaId, Guid conversaId, string texto, DateTime agora, CancellationToken ct = default)
    {
        try
        {
            var conversa = await conversaRepository.ObterPorIdAsync(empresaId, conversaId, ct);
            if (conversa is null || !conversa.EstaAberta || !conversa.DentroDaJanela(agora))
            {
                logger.LogInformation(
                    "Aviso de cobranca nao enviado conversaId={ConversaId}: conversa ausente, encerrada ou fora da janela",
                    conversaId);
                return false;
            }

            var canal = resolvedorCanal.Obter(conversa.Canal);
            var externoId = await canal.EnviarTextoAsync(conversa.ContatoIdExterno, texto, ct);

            var mensagem = Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Sistema, agora,
                TipoConteudoMensagem.Texto, texto, externoId);
            conversa.RegistrarSaida(agora);
            await conversaRepository.AddMensagemAsync(mensagem, ct);
            await unitOfWork.CommitAsync();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Falha ao enviar aviso de cobranca conversaId={ConversaId}", conversaId);
            return false;
        }
    }
}
