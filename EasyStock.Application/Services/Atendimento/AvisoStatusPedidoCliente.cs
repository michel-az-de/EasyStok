using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Aviso de status do pedido ao cliente pelo WhatsApp (S13). Monta as variáveis do template (nome, número,
/// previsão da janela, Instagram da loja) e enfileira um <c>EventoNotificacao</c> no outbox de notificações; o
/// Avaliador aplica rotina, template e canal (texto na janela de 24 h, template da Meta fora dela).
///
/// <para>
/// Aviso de status é transacional: sai mesmo com a conversa assumida pela dona e mesmo sem consentimento de
/// marketing; só a revogação explícita do transacional no WhatsApp (S38) o bloqueia. Sem telefone válido não
/// enfileira. A chave <c>pedidoId|marco</c> vira a <c>IdempotencyKey</c> do outbox: reprocessar o evento de
/// integração (at-least-once) não duplica a mensagem.
/// </para>
/// <para>
/// #1991-1: pedido cuja conversa é do chat do site recebe o aviso nela (<see cref="AvisoPedidoNaConversa"/>), sem
/// telefone e com o consentimento do canal do chat; o pedido de avaliação (agendado, com botões) segue pelo WhatsApp.
/// </para>
/// </summary>
public sealed class AvisoStatusPedidoCliente(
    IPedidoRepository pedidoRepository,
    IClienteRepository clienteRepository,
    IVagaOcupadaRepository vagaRepository,
    IStorefrontRepository storefrontRepository,
    PoliticaEnvioCliente politicaEnvio,
    INotificadorService notificador,
    ITenantContextAccessor tenantContext,
    ILogger<AvisoStatusPedidoCliente> logger, IConversaRepository? conversas = null,
    EasyStock.Application.UseCases.Atendimento.Entregas.ConfiguracaoLogistica? logistica = null,
    AvisoPedidoNaConversa? naConversa = null)
{
    /// <summary>Previsão quando o pedido não tem vaga de entrega (ex.: balcão).</summary>
    public const string PrevisaoSemJanela = "a confirmar";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Enfileira o aviso <paramref name="tipo"/> do pedido. <paramref name="marco"/> identifica o fato no pedido
    /// (status novo, ou <c>pago</c>) e compõe a chave de idempotência. Retorna se enfileirou.
    /// </summary>
    /// <param name="enviarApos">Quando informado, a mensagem só sai a partir desse instante (S26).</param>
    /// <param name="respeitaPreferenciaAvisos">
    /// Pós-venda (S26): não enfileira quando o cliente desligou os avisos (<c>Cliente.AvisosStatusAtivos</c>).
    /// </param>
    public async Task<bool> EnfileirarAsync(
        TipoEventoNotificacao tipo, Guid empresaId, Guid pedidoId, string marco, CancellationToken ct,
        DateTime? enviarApos = null, bool respeitaPreferenciaAvisos = false, string? previsao = null,
        string? mensagem = null)
    {
        // O dispatcher de integração roda sem claim JWT: sem o tenant, o filtro global e a RLS zeram a busca.
        if (logistica is not null && logistica.EmpresaDemonstracaoId != Guid.Empty && empresaId == logistica.EmpresaDemonstracaoId)
            return false;
        tenantContext.SetCurrentTenant(empresaId);

        var pedido = await pedidoRepository.GetByIdAsync(empresaId, pedidoId);
        if (pedido is null || pedido.EmpresaId != empresaId)
        {
            logger.LogWarning("Aviso {Tipo} sem pedido: pedidoId={PedidoId}", tipo, pedidoId);
            return false;
        }

        if (tipo == TipoEventoNotificacao.PedidoPagoConfirmado && pedido.StatusEnum == StatusPedido.Cancelado)
            return false;

        var cliente = pedido.ClienteId is { } clienteId
            ? await clienteRepository.GetByIdAsync(empresaId, clienteId)
            : null;

        if (cliente is { Bloqueado: true } || cliente is not null && cliente.EmpresaId != empresaId) return false;

        if (respeitaPreferenciaAvisos && cliente is { AvisosStatusAtivos: false })
        {
            logger.LogInformation("Aviso {Tipo} não enfileirado: cliente do pedido {PedidoId} desligou os avisos", tipo, pedidoId);
            return false;
        }

        var conversaId = conversas is null ? null : await conversas.ObterIdPorPedidoAsync(empresaId, pedidoId, ct);
        var conversa = conversaId is { } cid ? await conversas!.ObterPorIdAsync(empresaId, cid, ct) : null;
        var chave = $"{pedido.Id:N}|{marco}";

        // #1991-1: o cliente do chat do site recebe o aviso na conversa do pedido, sem telefone. O pedido de avaliação
        // (agendado, com botões) segue o caminho do WhatsApp.
        if (naConversa is not null && enviarApos is null && conversa is not null && AvisoPedidoNaConversa.Atende(conversa))
        {
            if (pedido.ClienteId is { } idChat
                && !await politicaEnvio.PodeEnviarAsync(empresaId, idChat, conversa.Canal, FinalidadeContato.Transacional, ct))
            {
                logger.LogInformation("Aviso {Tipo} não gravado: cliente do pedido {PedidoId} revogou o chat do site", tipo, pedidoId);
                return false;
            }
            var variaveis = await VariaveisAsync(tipo, empresaId, pedido, cliente, conversa, previsao, mensagem, ct);
            return await naConversa.EntregarAsync(tipo, conversa,
                variaveis.ToDictionary(v => v.Key, v => (object?)v.Value, StringComparer.OrdinalIgnoreCase), chave, ct);
        }

        var telefone = TelefoneE164(cliente?.Telefone) ?? TelefoneE164(pedido.ClienteTelefone);
        if (telefone is null)
        {
            logger.LogInformation("Aviso {Tipo} não enfileirado: pedido {PedidoId} sem telefone válido", tipo, pedidoId);
            return false;
        }

        if (pedido.ClienteId is { } id
            && !await politicaEnvio.PodeEnviarAsync(empresaId, id, CanalConversa.WhatsApp, FinalidadeContato.Transacional, ct))
        {
            logger.LogInformation("Aviso {Tipo} não enfileirado: cliente do pedido {PedidoId} revogou o WhatsApp", tipo, pedidoId);
            return false;
        }

        var payload = await VariaveisAsync(tipo, empresaId, pedido, cliente, conversa, previsao, mensagem, ct);
        payload["telefone"] = telefone;
        payload[NotificadorService.ChaveIdempotenciaPayload] = chave;
        if (enviarApos is { } instante)
            payload[NotificadorService.EnviarAposPayload] = DateTime.SpecifyKind(instante, DateTimeKind.Utc)
                .ToString("O", CultureInfo.InvariantCulture);

        await notificador.EnfileirarEventoAsync(tipo, empresaId, JsonSerializer.Serialize(payload), pedido.Id, ct);
        logger.LogInformation("Aviso {Tipo} enfileirado para o pedido {PedidoId}", tipo, pedidoId);
        return true;
    }

    /// <summary>Variáveis dos modelos de aviso, as mesmas no WhatsApp e no chat do site.</summary>
    private async Task<Dictionary<string, string?>> VariaveisAsync(
        TipoEventoNotificacao tipo, Guid empresaId, Domain.Entities.Pedido pedido, Domain.Entities.Cliente? cliente,
        Domain.Entities.Atendimento.Conversa? conversa, string? previsao, string? mensagem, CancellationToken ct)
    {
        var variaveis = new Dictionary<string, string?>
        {
            ["pedidoId"] = pedido.Id.ToString(),
            ["clienteId"] = pedido.ClienteId?.ToString(),
            ["nome"] = PrimeiroNome(cliente?.Nome ?? pedido.ClienteNome),
            ["numero"] = conversa is { NumeroComanda: > 0 } ? conversa.NumeroComanda.ToString(CultureInfo.InvariantCulture)
                : pedido.Id.ToString("N")[..8].ToUpperInvariant(),
            ["previsao"] = previsao ?? await PrevisaoAsync(pedido.Id, ct),
        };
        if (mensagem is not null) variaveis["mensagem"] = mensagem;
        if (tipo == TipoEventoNotificacao.PedidoEntregue)
            variaveis["instagram"] = (await storefrontRepository.GetByEmpresaAsync(empresaId, ct))?.InstagramUrl ?? string.Empty;
        return variaveis;
    }

    private async Task<string> PrevisaoAsync(Guid pedidoId, CancellationToken ct)
    {
        var vagas = await vagaRepository.GetByPedidoIdsAsync([pedidoId], ct);
        return vagas.TryGetValue(pedidoId, out var vaga)
            ? $"{vaga.Janela.Label}, {vaga.Vaga.DataEntrega.ToString("dd/MM", PtBr)}"
            : PrevisaoSemJanela;
    }

    private static string? TelefoneE164(string? telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone)) return null;
        try
        {
            return NormalizadorTelefone.NormalizarE164Br(telefone);
        }
        catch (TelefoneInvalidoException)
        {
            return null;
        }
    }

    private static string PrimeiroNome(string? nome) =>
        string.IsNullOrWhiteSpace(nome) ? "cliente" : nome.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
}
