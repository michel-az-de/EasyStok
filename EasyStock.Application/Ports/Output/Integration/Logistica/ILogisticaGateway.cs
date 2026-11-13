using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Ports.Output.Integration.Logistica;

/// <summary>Ponto da corrida: coordenadas e endereço por extenso, que o motorista lê.</summary>
public sealed record PontoCorrida(double Lat, double Lng, string Endereco);

public sealed record ContatoCorrida(string Nome, string Telefone);

/// <summary>Uma coleta e uma entrega (R7: uma parada por corrida). Sem <paramref name="AgendarPara"/>, é para agora.</summary>
public sealed record PedidoCotacaoCorrida(PontoCorrida Coleta, PontoCorrida Entrega, VeiculoCorrida Veiculo, DateTimeOffset? AgendarPara);

/// <summary>Cotação do provedor, já em centavos. Os ids das paradas voltam na chamada.</summary>
public sealed record CotacaoCorrida(
    string CotacaoId, int ValorCentavos, DateTimeOffset ExpiraEm, int? DistanciaMetros, string ColetaParadaId, string EntregaParadaId);

/// <summary>
/// Chamada de uma cotação. <paramref name="CorridaId"/> vai no <c>metadata</c> do pedido (R4): num timeout,
/// é por ele que se reconhece o pedido criado.
/// </summary>
public sealed record PedidoChamadaCorrida(
    CotacaoCorrida Cotacao, ContatoCorrida Remetente, ContatoCorrida Destinatario, string? Observacao, Guid CorridaId);

/// <summary>Como o provedor vê o pedido agora. É a fonte da verdade do status (ADR-0064 D1).</summary>
public sealed record SituacaoCorridaProvedor(
    string IdExterno, StatusCorrida Status, string? LinkRastreio, int? ValorCentavos, string? MotoristaIdExterno, Guid? CorridaId);

public sealed record MotoristaCorrida(string? IdExterno, string? Nome, string? Telefone, string? Placa);

/// <summary>
/// Despacho externo pela API (#2160, ADR-0064). Cada chamada usa a conexão da loja: o ambiente da conexão
/// (produção, senão sandbox) decide o host. O POST de criação nunca é repetido (R3).
/// </summary>
public interface ILogisticaGateway
{
    EmpresaEntregador Provedor { get; }

    /// <summary>A loja tem conexão utilizável com o provedor.</summary>
    Task<bool> ConectadoAsync(Guid empresaId, CancellationToken ct = default);

    Task<CotacaoCorrida> CotarAsync(Guid empresaId, PedidoCotacaoCorrida pedido, CancellationToken ct = default);

    /// <summary>Relê a cotação no provedor: o preço da corrida nunca vem do navegador.</summary>
    Task<CotacaoCorrida> ObterCotacaoAsync(Guid empresaId, string cotacaoId, CancellationToken ct = default);

    Task<SituacaoCorridaProvedor> ChamarAsync(Guid empresaId, PedidoChamadaCorrida pedido, CancellationToken ct = default);

    /// <summary><see langword="null"/> quando o provedor não conhece o pedido.</summary>
    Task<SituacaoCorridaProvedor?> ConsultarAsync(Guid empresaId, string idExterno, CancellationToken ct = default);

    Task<MotoristaCorrida?> ObterMotoristaAsync(Guid empresaId, string idExterno, string motoristaIdExterno, CancellationToken ct = default);

    Task CancelarAsync(Guid empresaId, string idExterno, CancellationToken ct = default);

    Task AdicionarGorjetaAsync(Guid empresaId, string idExterno, int centavos, CancellationToken ct = default);

    /// <summary>Confere a chave e a assinatura do webhook com a conexão da loja. Nunca lança.</summary>
    Task<bool> WebhookAutenticoAsync(Guid empresaId, string corpo, CancellationToken ct = default);
}

/// <summary>A loja não tem conexão com o provedor (ou ela está ilegível).</summary>
public sealed class LogisticaNaoConectadaException(string provedor)
    : Exception($"A loja não está conectada à {provedor}. Conecte em Gestão > Integrações.");

/// <summary>
/// O provedor recusou com resposta definitiva (4xx): nada foi criado. A mensagem é para o operador e nunca
/// carrega o corpo da resposta.
/// </summary>
public sealed class LogisticaRecusadaException(string mensagem, string? codigo = null) : Exception(mensagem)
{
    public string? Codigo { get; } = codigo;
}

/// <summary>Timeout ou 5xx: não se sabe se o provedor criou o pedido. Não repetir o POST (R4).</summary>
public sealed class LogisticaIndisponivelException(string mensagem, Exception? interna = null) : Exception(mensagem, interna);
