using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Notifications.InApp;

public sealed class InAppCanal(
    INotificacaoRepository notificacaoRepository,
    ILogger<InAppCanal> logger) : ICanalNotificacao
{
    public CanalNotificacao Canal => CanalNotificacao.InApp;

    public async Task<ResultadoEnvio> EnviarAsync(MensagemPronta mensagem, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // InApp reutiliza a entidade Notificacao existente para a caixa de entrada
            if (!Guid.TryParse(mensagem.Destinatario, out var usuarioId))
            {
                return new ResultadoEnvio(Sucesso: false, ProviderUsado: "inapp",
                    ErroDetalhado: "Destinatario InApp deve ser um UsuarioId (GUID).");
            }

            // Precisamos do EmpresaId — extraído do OutboxId via comentário de design:
            // Para InApp, o "Destinatario" é o UsuarioId e o EmpresaId vem do contexto.
            // Como não temos EmpresaId direto aqui, usamos Guid.Empty como placeholder
            // — o Worker resolverá via OutboxMensagem.EmpresaId em PR4.
            var notificacao = Notificacao.Criar(
                empresaId: Guid.Empty,
                tipo: MapearTipo(mensagem.Categoria),
                mensagem: mensagem.Corpo,
                referenciaId: mensagem.OutboxId);

            notificacao.OutboxMensagemId = mensagem.OutboxId;

            await notificacaoRepository.AddAsync(notificacao);

            sw.Stop();
            logger.LogDebug(
                "InApp criada para usuario={UsuarioId} outbox={OutboxId}",
                usuarioId, mensagem.OutboxId);

            return new ResultadoEnvio(Sucesso: true, ProviderUsado: "inapp",
                DuracaoMs: sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Falha InApp para outbox={OutboxId}", mensagem.OutboxId);
            return new ResultadoEnvio(Sucesso: false, ProviderUsado: "inapp",
                ErroDetalhado: ex.Message, DuracaoMs: sw.ElapsedMilliseconds);
        }
    }

    private static TipoAlertaEstoque MapearTipo(CategoriaConteudoNotificacao categoria) =>
        categoria switch
        {
            CategoriaConteudoNotificacao.Transacional => TipoAlertaEstoque.PedidoRecebido,
            CategoriaConteudoNotificacao.Marketing => TipoAlertaEstoque.PedidoRecebido,
            _ => TipoAlertaEstoque.ValidadeProxima
        };
}
