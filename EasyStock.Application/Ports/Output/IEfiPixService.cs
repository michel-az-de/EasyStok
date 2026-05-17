namespace EasyStock.Application.Ports.Output;

public sealed record EfiCobrancaResult(
    string Txid,
    string PixCopiaCola,
    string QrCodeBase64,
    DateTime ExpiracaoEm);

/// <summary>
/// Status de uma cobranca Pix consultada via <c>GET /v2/cob/{txid}</c>.
/// Espelha os possiveis valores do campo <c>status</c> retornados pela API Efi.
/// </summary>
public enum EfiCobrancaStatus
{
    /// <summary>ATIVA — cobranca emitida, aguardando pagamento.</summary>
    Ativa,
    /// <summary>CONCLUIDA — pagamento Pix recebido e confirmado.</summary>
    Concluida,
    /// <summary>REMOVIDA_PELO_USUARIO_RECEBEDOR — cancelada pelo recebedor.</summary>
    RemovidaPeloUsuario,
    /// <summary>REMOVIDA_PELO_PSP — cancelada pelo provedor de servico.</summary>
    RemovidaPeloPsp,
    /// <summary>Status desconhecido (string nao mapeada ou erro de comunicacao).</summary>
    Desconhecido
}

public sealed record EfiCobrancaStatusResult(
    string Txid,
    EfiCobrancaStatus Status,
    decimal? ValorOriginal = null,
    decimal? ValorPago = null,
    DateTime? CriadoEm = null,
    DateTime? PagoEm = null,
    string? E2eId = null);

public sealed record EfiEstornoResult(
    bool Sucesso,
    string? Id = null,
    string? Status = null,
    string? Mensagem = null);

public interface IEfiPixService
{
    /// <summary>
    /// Emite uma cobranca Pix imediata via <c>POST /v2/cob/{txid}</c>.
    /// Retorna QR code Base64 e copia-e-cola prontos para exibir ao pagador.
    /// </summary>
    Task<EfiCobrancaResult> CriarCobrancaAsync(
        string txid,
        decimal valor,
        string descricao,
        CancellationToken ct = default);

    /// <summary>
    /// Consulta o estado atual de uma cobranca Pix no Efi via
    /// <c>GET /v2/cob/{txid}</c>. Usado pela reconciliacao (F11) para
    /// detectar pagamentos quando o webhook se perdeu.
    /// </summary>
    Task<EfiCobrancaStatusResult> ConsultarCobrancaAsync(
        string txid,
        CancellationToken ct = default);

    /// <summary>
    /// Solicita estorno de pagamento Pix recebido via
    /// <c>PUT /v2/pix/{e2eId}/devolucao/{idSolicitacao}</c>. Permitido
    /// ate 90 dias apos o pagamento. Estorno parcial e aceito.
    /// </summary>
    Task<EfiEstornoResult> EstornarAsync(
        string e2eId,
        string idSolicitacao,
        decimal valor,
        CancellationToken ct = default);
}
