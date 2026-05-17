namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>
/// Resultado da criacao de uma cobranca via gateway. Contem os dados que o
/// cliente precisa para pagar — formato varia por metodo:
/// </summary>
/// <list type="bullet">
///   <item>Pix: <c>PixCopiaCola</c> + <c>QrCodeBase64</c> + <c>ExpiracaoEm</c></item>
///   <item>Boleto: <c>BoletoCodigo</c> (linha digitavel) + <c>BoletoUrl</c></item>
///   <item>Cartao (gateway): <c>UrlCheckout</c> ou <c>SessionId</c> via metadados</item>
///   <item>Manual: vazio — admin registra pagamento sem cobrar via gateway</item>
/// </list>
/// <param name="DadosGatewayJson">JSON livre com dados adicionais do gateway para auditoria.</param>
public sealed record InstrucaoPagamento(
    string Provedor,
    string TransactionId,
    string? PixCopiaCola = null,
    string? QrCodeBase64 = null,
    string? BoletoCodigo = null,
    string? BoletoUrl = null,
    string? UrlCheckout = null,
    DateTime? ExpiracaoEm = null,
    string? DadosGatewayJson = null
);

/// <summary>Estado retornado por <c>IPagamentoGateway.ConsultarAsync</c>.</summary>
public enum StatusGateway
{
    /// <summary>Aguardando pagamento.</summary>
    Pendente,
    /// <summary>Pago e confirmado pelo gateway.</summary>
    Confirmado,
    /// <summary>Pagamento falhou ou foi recusado.</summary>
    Falhou,
    /// <summary>Estorno completo concluido.</summary>
    Estornado,
    /// <summary>Estado desconhecido (gateway indisponivel ou erro de comunicacao).</summary>
    Desconhecido
}

/// <summary>Resultado de uma operacao de estorno via gateway.</summary>
public sealed record EstornoResult(
    bool Sucesso,
    string? ProtocoloEstorno = null,
    string? Mensagem = null
);
