using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Domain.Entities;

namespace EasyStock.Infra.Async.Pagamentos;

/// <summary>
/// Adapter que expoe <see cref="IEfiPixService"/> via a abstracao generica
/// <see cref="IPagamentoGateway"/>. Mantem o servico legado intacto — apenas
/// envolve as chamadas e converte tipos.
///
/// <para>
/// F11 — implementacao completa de <c>ConsultarAsync</c> via <c>GET /v2/cob/{txid}</c>
/// e <c>EstornarAsync</c> via <c>PUT /v2/pix/{e2eId}/devolucao/{idSolicitacao}</c>.
/// Reconciliacao automatica (F6) agora destrava para Pix.
/// </para>
/// </summary>
public sealed class EfiPixGatewayAdapter(IEfiPixService pixService) : IPagamentoGateway
{
    public string Provedor => "EfiPix";

    public bool SuportaMetodo(string metodo) =>
        string.Equals(metodo, "pix", StringComparison.OrdinalIgnoreCase);

    public async Task<InstrucaoPagamento> CriarAsync(
        Fatura fatura,
        string metodo,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fatura);
        if (!SuportaMetodo(metodo))
            throw new NotSupportedException($"EfiPix nao suporta metodo '{metodo}'.");

        // Txid Efi: aceita 26-35 chars alfanum. Quando o orchestrator (Onda P0)
        // injeta idempotencyKey (SHA-256 hex 64 chars), derivamos os primeiros 32
        // chars como txid — assim duas tentativas com mesma key reusam a mesma
        // cobranca Efi (que ja deduplica por txid). Sem key (legado), usa Guid
        // sem hifens (32 chars) por fatura.
        var txid = !string.IsNullOrWhiteSpace(idempotencyKey) && idempotencyKey.Length >= 32
            ? idempotencyKey[..32].ToLowerInvariant()
            : fatura.Id.ToString("N");
        var descricao = $"Fatura {fatura.Numero}";

        var efi = await pixService.CriarCobrancaAsync(txid, fatura.Total, descricao, ct);

        return new InstrucaoPagamento(
            Provedor: Provedor,
            TransactionId: efi.Txid,
            PixCopiaCola: efi.PixCopiaCola,
            QrCodeBase64: efi.QrCodeBase64,
            ExpiracaoEm: efi.ExpiracaoEm,
            DadosGatewayJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                txid = efi.Txid,
                pix_copia_cola = efi.PixCopiaCola,
                qr_code = efi.QrCodeBase64,
                expiracao_em = efi.ExpiracaoEm
            })
        );
    }

    public async Task<StatusGateway> ConsultarAsync(string transactionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(transactionId)) return StatusGateway.Desconhecido;
        var status = await pixService.ConsultarCobrancaAsync(transactionId, ct);
        return status.Status switch
        {
            EfiCobrancaStatus.Concluida => StatusGateway.Confirmado,
            EfiCobrancaStatus.Ativa => StatusGateway.Pendente,
            EfiCobrancaStatus.RemovidaPeloUsuario => StatusGateway.Falhou,
            EfiCobrancaStatus.RemovidaPeloPsp => StatusGateway.Falhou,
            _ => StatusGateway.Desconhecido
        };
    }

    public async Task<EstornoResult> EstornarAsync(string transactionId, decimal valor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
            return new EstornoResult(false, Mensagem: "transactionId vazio.");

        // transactionId aqui e o txid da cobranca. Para estornar Pix, precisamos
        // do e2eId (end-to-end ID do pagamento recebido), que esta no Pix
        // recebido — consultamos a cobranca primeiro.
        var consulta = await pixService.ConsultarCobrancaAsync(transactionId, ct);
        if (string.IsNullOrWhiteSpace(consulta.E2eId))
            return new EstornoResult(false, Mensagem: "e2eId nao disponivel — cobranca nao foi paga ou Efi nao retornou o id.");

        var idSolicitacao = $"rev{Guid.NewGuid():N}"[..32]; // Efi exige <=35 chars alfanum
        var resultado = await pixService.EstornarAsync(consulta.E2eId, idSolicitacao, valor, ct);
        return new EstornoResult(
            Sucesso: resultado.Sucesso,
            ProtocoloEstorno: resultado.Id ?? idSolicitacao,
            Mensagem: resultado.Mensagem
        );
    }
}
