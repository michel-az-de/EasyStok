using System.Text.Json.Serialization;
using EasyStock.Domain.Entities.Ia;
using EasyStock.Domain.Enums.Ia;

namespace EasyStock.Application.Ports.Output.Ai;

/// <summary>
/// #3064 (S2b de #2948): a chamada nem saiu (disjuntor aberto, cliente desligado, loja sem rota, sem chave, cota
/// estourada ou tenant divergente). Herda <see cref="HttpRequestException"/> para quem já trata falha de LLM seguir igual;
/// o registro de uso não grava Erro por ela (achado A1 da S2a).
/// </summary>
public sealed class ProvedorIaIndisponivelException(string mensagem, Exception? inner = null)
    : HttpRequestException(HttpRequestError.ConnectionError, mensagem, inner);

/// <summary>#3064: o tenant da requisição atual (claim, webhook ou job); vazio quando nenhum foi definido.</summary>
public interface ITenantAtual
{
    Guid EmpresaId { get; }
}

/// <summary>
/// #3064: provedor e modelo da loja para uma chamada, e o gasto do mês por provedor. A implementação lê com o tenant da
/// própria loja (RLS), num escopo próprio.
/// </summary>
public interface IRotasIa
{
    /// <summary>
    /// O modelo ligado da loja cujo <c>id_externo</c> casa com <paramref name="modeloPedido"/>; sem casamento, o padrão.
    /// Nulo quando o modelo escolhido não existe ou o provedor dele está desligado.
    /// </summary>
    Task<RotaIa?> ObterAsync(Guid empresaId, string? modeloPedido, CancellationToken ct = default);

    /// <summary>Soma de <c>custo_usd</c> do mês corrente (de Brasília) da loja nesse provedor.</summary>
    Task<decimal> GastoDoMesAsync(Guid empresaId, Guid provedorId, DateTime agoraUtc, CancellationToken ct = default);
}

/// <summary>
/// <paramref name="CaiuNoPadrao"/>: o modelo pedido não casou com nenhum modelo ligado da loja.
/// <paramref name="PrazoSegundos"/>: prazo da chamada ao provedor (#3304); nulo segue o global do <c>.env</c>.
/// </summary>
public sealed record RotaIa(
    Guid ProvedorId,
    string BaseUrl,
    AutenticacaoProvedorIa Autenticacao,
    decimal? OrcamentoMensalUsd,
    ModeloIa Modelo,
    bool CaiuNoPadrao,
    int? PrazoSegundos = null)
{
    public string ProviderKey => $"ia:{ProvedorId}";
}

/// <summary>#3064: fala com um provedor no formato Anthropic Messages, com URL, autenticação e chave da loja.</summary>
public interface IClienteProvedorIa
{
    Task<RespostaLlm> EnviarAsync(DestinoIa destino, RequisicaoLlm requisicao, CancellationToken ct = default);
}

/// <summary>
/// Para onde a chamada vai. O <see cref="ToString"/> nunca mostra a chave.
/// <paramref name="SaidaMax"/>: o máximo de tokens de saída que o modelo aceita (#3304); nem a nova tentativa depois de
/// <c>max_tokens</c> passa dele. <paramref name="PrazoSegundos"/>: prazo da chamada ao provedor; nulo em ambos segue o global do <c>.env</c>.
/// No Assistente, com o prazo do turno ligado (180 s por padrão; <c>PrazoTurnoSegundos</c> 0 desliga), um prazo de provedor acima disso não alonga o turno.
/// Com o prazo do turno desligado, o prazo do provedor vale por chamada e a nova tentativa depois de falha transitória o repete: 300 s viram até 600 s.
/// </summary>
public sealed record DestinoIa(Guid ProvedorId, string BaseUrl, AutenticacaoProvedorIa Autenticacao, string Chave, string Modelo,
    int? SaidaMax = null, int? PrazoSegundos = null)
{
    public override string ToString() => $"DestinoIa {{ ProvedorId = {ProvedorId}, BaseUrl = {BaseUrl}, Modelo = {Modelo} }}";
}

/// <summary>Payload cifrado no cofre (<c>CategoriaIntegracao.Ia</c>, <c>ia:{provedorId}</c>).</summary>
public sealed record ChaveProvedorIa([property: JsonPropertyName("apiKey")] string ApiKey)
{
    public override string ToString() => "ChaveProvedorIa { *** }";
}
