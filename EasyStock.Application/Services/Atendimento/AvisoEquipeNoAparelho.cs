using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Avisos do Balcão no aparelho (#1807): com o console fechado, contato novo, pagamento confirmado e etapa do pedido
/// viram Web Push para os aparelhos inscritos de cada pessoa da equipe (audiência <c>equipe</c>, destinatário
/// <c>usuario:{id}</c>). O aparelho decide se recebe: desligar os avisos nele desfaz a inscrição, e o
/// <c>WebPushCanal</c> só entrega para as ativas. #1765-7: Push que falha (sem inscrição, sem chave) cai no sino
/// (In-App) da mesma pessoa.
///
/// <para>
/// A carga leva só o primeiro nome e o evento: nada de telefone, valor ou texto do cliente, porque o aviso aparece na
/// tela de bloqueio. A <c>tag</c> é a mesma da notificação do console (<c>casa-da-baba-{tipo}-{conversa}</c>), então
/// o aviso do servidor substitui o do console em vez de aparecer duas vezes. A chave <c>fato|marco</c> (conversa ou pedido) vira a
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

        await EnfileirarAsync(TipoEventoNotificacao.ContatoNovoNoBalcao, empresaId, conversa.Id, conversa.Id, TagContatoNovo,
            NomesDeContato.PrimeiroNome(conversa.ContatoNome, NomeSemContato), "contato-novo", null, ct);
    }

    public async Task PedidoPagoAsync(Guid empresaId, Guid pedidoId, CancellationToken ct)
    {
        tenantContext.SetCurrentTenant(empresaId);
        if (await DoPedidoAsync(empresaId, pedidoId, ct) is not { } alvo) return;

        await EnfileirarAsync(TipoEventoNotificacao.PedidoPagoNoBalcao, empresaId, alvo.Referencia, pedidoId, TagPagamento,
            alvo.Nome, "pago", null, ct);
    }

    /// <summary>
    /// #2004: a automática de pagamento confirmado não teve resposta do canal e pode não ter chegado ao cliente. Sino da
    /// equipe (audiência <c>gestores</c>) com o primeiro nome e a referência do pedido, uma vez por pedido.
    /// </summary>
    public async Task PagamentoSemAvisoAoClienteAsync(Guid empresaId, Guid pedidoId, CancellationToken ct)
    {
        tenantContext.SetCurrentTenant(empresaId);
        var pedido = await pedidos.GetByIdAsync(empresaId, pedidoId);
        if (pedido is null || pedido.EmpresaId != empresaId)
        {
            logger.LogWarning("Aviso de pagamento sem confirmação ao cliente sem pedido: pedidoId={PedidoId}", pedidoId);
            return;
        }

        var chave = $"pagamento-sem-aviso:{pedido.Id:N}";
        var payload = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["nome"] = NomesDeContato.PrimeiroNome(pedido.ClienteNome, NomeSemContato),
            ["referencia"] = CodigoCurto.De(pedido.Id),
            [NotificadorService.ChaveIdempotenciaPayload] = chave,
        });
        await notificador.EnfileirarEventoAsync(TipoEventoNotificacao.PagamentoSemAvisoAoCliente, empresaId, payload, pedido.Id, ct, chave);
    }

    public async Task EtapaDoPedidoAsync(Guid empresaId, Guid pedidoId, string statusNovo, CancellationToken ct)
    {
        if (RotuloDaEtapa(statusNovo) is not { } rotulo) return;
        tenantContext.SetCurrentTenant(empresaId);
        if (await DoPedidoAsync(empresaId, pedidoId, ct) is not { } alvo) return;

        await EnfileirarAsync(TipoEventoNotificacao.PedidoMudouEtapaNoBalcao, empresaId, alvo.Referencia, pedidoId, TagEtapa,
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
        return (conversa?.Id ?? pedido.Id, NomesDeContato.PrimeiroNome(conversa?.ContatoNome ?? pedido.ClienteNome, NomeSemContato));
    }

    /// <param name="referencia">De quem é a tag: a conversa (ou o pedido sem conversa), como no console.</param>
    /// <param name="fato">De quem é a chave de idempotência: o pedido, porque uma conversa tem mais de um pedido.</param>
    private async Task EnfileirarAsync(
        TipoEventoNotificacao tipo, Guid empresaId, Guid referencia, Guid fato, string tipoTag, string nome, string marco,
        string? etapa, CancellationToken ct)
    {
        var payload = new Dictionary<string, string>
        {
            ["nome"] = nome,
            ["tag"] = Tag(tipoTag, referencia),
            [NotificadorService.ChaveIdempotenciaPayload] = $"{fato:N}|{marco}",
        };
        if (etapa is not null) payload["etapa"] = etapa;

        await notificador.EnfileirarEventoAsync(tipo, empresaId, JsonSerializer.Serialize(payload), fato, ct);
    }
}
