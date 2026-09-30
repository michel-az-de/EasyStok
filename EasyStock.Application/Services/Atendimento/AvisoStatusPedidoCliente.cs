using System.Globalization;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Exceptions.Storefront;

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
/// </summary>
public sealed class AvisoStatusPedidoCliente(
    IPedidoRepository pedidoRepository,
    IClienteRepository clienteRepository,
    IVagaOcupadaRepository vagaRepository,
    IStorefrontRepository storefrontRepository,
    PoliticaEnvioCliente politicaEnvio,
    INotificadorService notificador,
    ITenantContextAccessor tenantContext,
    ILogger<AvisoStatusPedidoCliente> logger)
{
    /// <summary>Previsão quando o pedido não tem vaga de entrega (ex.: balcão).</summary>
    public const string PrevisaoSemJanela = "a confirmar";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Enfileira o aviso <paramref name="tipo"/> do pedido. <paramref name="marco"/> identifica o fato no pedido
    /// (status novo, ou <c>pago</c>) e compõe a chave de idempotência. Retorna se enfileirou.
    /// </summary>
    public async Task<bool> EnfileirarAsync(
        TipoEventoNotificacao tipo, Guid empresaId, Guid pedidoId, string marco, CancellationToken ct)
    {
        // O dispatcher de integração roda sem claim JWT: sem o tenant, o filtro global e a RLS zeram a busca.
        tenantContext.SetCurrentTenant(empresaId);

        var pedido = await pedidoRepository.GetByIdAsync(empresaId, pedidoId);
        if (pedido is null)
        {
            logger.LogWarning("Aviso {Tipo} sem pedido: pedidoId={PedidoId}", tipo, pedidoId);
            return false;
        }

        var cliente = pedido.ClienteId is { } clienteId
            ? await clienteRepository.GetByIdAsync(empresaId, clienteId)
            : null;

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

        var nome = cliente?.Nome ?? pedido.ClienteNome;
        var payload = new Dictionary<string, string?>
        {
            ["pedidoId"] = pedido.Id.ToString(),
            ["telefone"] = telefone,
            ["nome"] = PrimeiroNome(nome),
            ["numero"] = pedido.Id.ToString("N")[..8].ToUpperInvariant(),
            ["previsao"] = await PrevisaoAsync(pedido.Id, ct),
            [NotificadorService.ChaveIdempotenciaPayload] = $"{pedido.Id:N}|{marco}",
        };
        if (tipo == TipoEventoNotificacao.PedidoEntregue)
            payload["instagram"] = (await storefrontRepository.GetByEmpresaAsync(empresaId, ct))?.InstagramUrl ?? string.Empty;

        await notificador.EnfileirarEventoAsync(tipo, empresaId, JsonSerializer.Serialize(payload), pedido.Id, ct);
        logger.LogInformation("Aviso {Tipo} enfileirado para o pedido {PedidoId}", tipo, pedidoId);
        return true;
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
