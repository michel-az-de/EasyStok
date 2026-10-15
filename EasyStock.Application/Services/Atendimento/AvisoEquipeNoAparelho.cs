using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Avisos do Balcão no aparelho (#1807): com o console fechado, contato novo, pagamento confirmado e etapa do pedido
/// viram Web Push para todos os aparelhos inscritos da empresa (rotina Push, destinatário <c>empresa:{id}</c>). O
/// aparelho decide se recebe: desligar os avisos nele desfaz a inscrição, e o <c>WebPushCanal</c> só entrega para as
/// ativas.
///
/// <para>
/// A carga leva só o primeiro nome e o evento: nada de telefone, valor ou texto do cliente, porque o aviso aparece na
/// tela de bloqueio. A <c>tag</c> é a mesma da notificação do console (<c>casa-da-baba-{tipo}-{conversa}</c>), então
/// o aviso do servidor substitui o do console em vez de aparecer duas vezes. A chave <c>referência|marco</c> vira a
/// <c>IdempotencyKey</c> do outbox: o evento de integração reentregue (at-least-once) não repete o aviso.
/// </para>
/// </summary>
public sealed class AvisoEquipeNoAparelho(
    INotificadorService notificador,
    IConversaRepository conversas,
    IPedidoRepository pedidos,
    ITenantContextAccessor tenantContext,
    ILogger<AvisoEquipeNoAparelho> logger)
{
    /// <summary>Nome quando o contato não tem nome ou o nome é um telefone.</summary>
    public const string NomeSemContato = "Cliente";

    /// <summary>Tipos da tag, os mesmos de <c>TIPOS_DE_AVISO</c> do console (<c>dominio/aviso.js</c>).</summary>
    public const string TagContatoNovo = "contato-novo";
    public const string TagPassouParaVoce = "precisa";
    public const string TagPagamento = "pagamento";
    public const string TagEtapa = "etapa";

    public async Task ContatoNovoAsync(Guid empresaId, Guid conversaId, CancellationToken ct)
    {
        tenantContext.SetCurrentTenant(empresaId);
        var conversa = await conversas.ObterPorIdAsync(empresaId, conversaId, ct);
        if (conversa is null || conversa.EmpresaId != empresaId)
        {
            logger.LogWarning("Aviso de contato novo sem conversa: conversaId={ConversaId}", conversaId);
            return;
        }

        await EnfileirarAsync(TipoEventoNotificacao.ContatoNovoNoBalcao, empresaId, conversa.Id, TagContatoNovo,
            PrimeiroNome(conversa.ContatoNome), "contato-novo", null, ct);
    }

    public async Task PedidoPagoAsync(Guid empresaId, Guid pedidoId, CancellationToken ct)
    {
        tenantContext.SetCurrentTenant(empresaId);
        if (await DoPedidoAsync(empresaId, pedidoId, ct) is not { } alvo) return;

        await EnfileirarAsync(TipoEventoNotificacao.PedidoPagoNoBalcao, empresaId, alvo.Referencia, TagPagamento,
            alvo.Nome, "pago", null, ct);
    }

    public async Task EtapaDoPedidoAsync(Guid empresaId, Guid pedidoId, string statusNovo, CancellationToken ct)
    {
        if (RotuloDaEtapa(statusNovo) is not { } rotulo) return;
        tenantContext.SetCurrentTenant(empresaId);
        if (await DoPedidoAsync(empresaId, pedidoId, ct) is not { } alvo) return;

        await EnfileirarAsync(TipoEventoNotificacao.PedidoMudouEtapaNoBalcao, empresaId, alvo.Referencia, TagEtapa,
            alvo.Nome, $"etapa:{statusNovo}", rotulo, ct);
    }

    /// <summary>
    /// Rótulo da etapa como o console mostra (<c>dominio/esteira.js</c>); nulo para o que não é etapa da esteira.
    /// Pago tem aviso próprio, e rascunho e aguardando não pedem ninguém fora do console.
    /// </summary>
    public static string? RotuloDaEtapa(string? status) => status switch
    {
        StatusPedidoMapper.Preparando => "Em preparo",
        StatusPedidoMapper.Pronto => "Embalado",
        StatusPedidoMapper.SaiuParaEntrega => "Em entrega",
        StatusPedidoMapper.Entregue => "Entregue",
        StatusPedidoMapper.Cancelado => "Cancelado",
        _ => null,
    };

    /// <summary>Primeiro nome do contato; sem nome, ou quando o nome é um telefone, <see cref="NomeSemContato"/>.</summary>
    public static string PrimeiroNome(string? nome)
    {
        var primeiro = (nome ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return primeiro is null || primeiro.Any(char.IsDigit) ? NomeSemContato : primeiro;
    }

    /// <summary>Tag da notificação, igual à do console: <c>casa-da-baba-{tipo}-{referência}</c>.</summary>
    public static string Tag(string tipo, Guid referencia) => $"casa-da-baba-{tipo}-{referencia}";

    private async Task<(Guid Referencia, string Nome)?> DoPedidoAsync(Guid empresaId, Guid pedidoId, CancellationToken ct)
    {
        var pedido = await pedidos.GetByIdAsync(empresaId, pedidoId);
        if (pedido is null || pedido.EmpresaId != empresaId)
        {
            logger.LogWarning("Aviso do Balcão sem pedido: pedidoId={PedidoId}", pedidoId);
            return null;
        }

        // O Balcão lista conversas: com conversa, a tag é a dela, como no console.
        var conversaId = await conversas.ObterIdPorPedidoAsync(empresaId, pedidoId, ct);
        var conversa = conversaId is { } id ? await conversas.ObterPorIdAsync(empresaId, id, ct) : null;
        return (conversa?.Id ?? pedido.Id, PrimeiroNome(conversa?.ContatoNome ?? pedido.ClienteNome));
    }

    private async Task EnfileirarAsync(
        TipoEventoNotificacao tipo, Guid empresaId, Guid referencia, string tipoTag, string nome, string marco,
        string? etapa, CancellationToken ct)
    {
        var payload = new Dictionary<string, string>
        {
            ["nome"] = nome,
            ["tag"] = Tag(tipoTag, referencia),
            [NotificadorService.ChaveIdempotenciaPayload] = $"{referencia:N}|{marco}",
        };
        if (etapa is not null) payload["etapa"] = etapa;

        await notificador.EnfileirarEventoAsync(tipo, empresaId, JsonSerializer.Serialize(payload), referencia, ct);
    }
}
