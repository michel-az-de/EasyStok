using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.UseCases.Storefront.Avaliacao;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Events.Storefront;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Events.Storefront.Handlers;

/// <summary>
/// Envia link de avaliação via WhatsApp quando um pedido fica elegível (+24h após entrega).
/// </summary>
public sealed class EnviarLinkAvaliacaoWhatsAppHandler(
    AvaliacaoTokenService tokenService,
    IProvedorWhatsApp whatsApp,
    IConfiguration configuration,
    ILogger<EnviarLinkAvaliacaoWhatsAppHandler> logger)
{
    public async Task HandleAsync(
        NotificarClienteSolicitarAvaliacaoEvent evento,
        CancellationToken ct = default)
    {
        var baseUrl = configuration["App:BaseUrl"]?.TrimEnd('/')
            ?? "https://casadababa.com";

        var token = tokenService.Gerar(evento.PedidoId);
        var link = $"{baseUrl}/avaliar/abrir?p={evento.PedidoId}&t={token}";

        var primeiroNome = evento.NomeCliente.Split(' ')[0];
        var corpo = $"Oi {primeiroNome}! Como foi o pedido? Adoraríamos ouvir sua avaliação 😊\n\n👉 {link}";

        var mensagem = new MensagemPronta(
            OutboxId: Guid.NewGuid(),
            EmpresaId: evento.EmpresaId,
            Destinatario: evento.TelefoneCliente,
            Assunto: "Avalie seu pedido Casa da Babá",
            Corpo: corpo,
            Canal: CanalNotificacao.WhatsApp,
            Categoria: CategoriaConteudoNotificacao.Transacional);

        var resultado = await whatsApp.EnviarAsync(mensagem, ct);

        if (resultado.Sucesso)
        {
            logger.LogInformation(
                "Link de avaliação enviado. pedidoId={PedidoId} telefone={Telefone}",
                evento.PedidoId, MascararTelefone(evento.TelefoneCliente));
        }
        else
        {
            logger.LogWarning(
                "Falha ao enviar link de avaliação. pedidoId={PedidoId} erro={Erro}",
                evento.PedidoId, resultado.ErroDetalhado);
        }
    }

    private static string MascararTelefone(string telefone)
    {
        if (telefone.Length < 9) return "+55*****";
        return $"{telefone[..5]}*****{telefone[^4..]}";
    }
}
